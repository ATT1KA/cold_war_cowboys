using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Night 4: Plays interactive narrative sequences for high-stakes missions
/// (Difficulty >= 70). Sits between Assignment and Resolution phases —
/// the player walks through 3-5 branching nodes that modify skill weights,
/// difficulty, and stress before the Resolver runs.
///
/// Flow: GameManager detects missions with NarrativeSequence != null →
/// feeds them to the runner → UI presents nodes one at a time →
/// player picks choices → runner accumulates NarrativeOverrides →
/// Resolver receives overrides at resolution time.
/// </summary>
public sealed class MissionNarrativeRunner
{
	private NarrativeSequence? _sequence;
	private Mission? _mission;
	private WorldState? _world;
	private int _currentNodeIndex;
	private readonly List<string> _approachHistory = new();
	/// <summary>Accumulated overrides from player choices. Fed to Resolver.</summary>
	public NarrativeOverrides Overrides { get; private set; } = new();

	/// <summary>True when a sequence is active and has more nodes to present.</summary>
	public bool IsActive => _sequence != null && _currentNodeIndex < _eligibleNodes.Count;

	/// <summary>True when the sequence has been fully played through.</summary>
	public bool IsComplete => _sequence != null && _currentNodeIndex >= _eligibleNodes.Count;

	/// <summary>The current node to present to the player. Null if not active.</summary>
	public NarrativeNode? CurrentNode => IsActive ? _eligibleNodes[_currentNodeIndex] : null;

	/// <summary>
	/// The current node's choices with per-choice stat gates applied — a choice
	/// with RequiresStat "Hacking:55" is only offered when an assigned operative
	/// clears the bar. Falls back to the full list if gating would leave the
	/// player with nothing to pick. UI and harnesses must present THIS, not
	/// CurrentNode.Choices.
	/// </summary>
	public IReadOnlyList<NarrativeChoice> CurrentChoices
	{
		get
		{
			var node = CurrentNode;
			if ( node == null || _mission == null || _world == null )
				return Array.Empty<NarrativeChoice>();
			var offered = node.Choices
				.Where( c => MeetsStatRequirement( c.RequiresStat, _mission, _world ) )
				.ToList();
			return offered.Count > 0 ? offered : node.Choices;
		}
	}

	/// <summary>Current node index (0-based) for UI progress display.</summary>
	public int CurrentStep => _currentNodeIndex;

	/// <summary>Total eligible nodes in the sequence.</summary>
	public int TotalSteps => _eligibleNodes.Count;

	/// <summary>The mission being narrated.</summary>
	public Mission? ActiveMission => _mission;

	private List<NarrativeNode> _eligibleNodes = new();

	/// <summary>
	/// Begin a narrative sequence for the given mission. Call before Resolution.
	/// Returns false if the mission has no narrative sequence.
	/// </summary>
	public bool Begin( Mission mission, WorldState world )
	{
		if ( mission.NarrativeSequence == null || mission.NarrativeSequence.Nodes.Count == 0 )
			return false;

		_sequence = mission.NarrativeSequence;
		_mission = mission;
		_world = world;
		_currentNodeIndex = 0;
		_approachHistory.Clear();
		Overrides = new NarrativeOverrides();

		// Filter eligible nodes based on conditions
		_eligibleNodes = FilterEligibleNodes( _sequence.Nodes, mission, world );

		return _eligibleNodes.Count > 0;
	}

	/// <summary>
	/// Apply the player's choice at the current node. Advances to next node.
	/// Returns the choice aftermath text (if any).
	/// </summary>
	public string? ApplyChoice( NarrativeChoice choice, WorldState world )
	{
		if ( !IsActive || _mission == null ) return null;

		_approachHistory.Add( choice.Approach );

		// The sequence layer must leave traces the scene director can read.
		if ( !string.IsNullOrEmpty( choice.Approach ) )
			world.NarrativeFlags.Add( $"seq:{_mission.TemplateId}:{choice.Approach}" );
		foreach ( var f in choice.FlagsOnPick )
			world.NarrativeFlags.Add( f );
		if ( choice.WetWork )
		{
			world.NarrativeFlags.Add( "seq:wetwork_chosen" );
			world.Corruption.RegisterChoice( +5.0 );
		}
		else if ( choice.Approach is "social" or "compromise" )
		{
			world.Corruption.RegisterChoice( -1.0 );
		}

		world.ChoiceLog.Add( new ChoiceRecord
		{
			Cycle = world.Corporate.Cycle,
			Source = "sequence",
			SourceId = _mission.TemplateId,
			Label = choice.Text,
			Flags = new List<string>( choice.FlagsOnPick ),
		} );

		// Accumulate skill weight overrides
		foreach ( var (skill, weight) in choice.SkillWeightOverride )
		{
			if ( Enum.TryParse<SkillKind>( skill, true, out var sk ) )
			{
				if ( !Overrides.SkillWeightDeltas.ContainsKey( sk ) )
					Overrides.SkillWeightDeltas[sk] = 0;
				Overrides.SkillWeightDeltas[sk] += weight;
			}
		}

		// Accumulate difficulty modifier
		Overrides.DifficultyDelta += choice.DifficultyModifier;

		// Apply immediate stress to assigned operatives
		if ( choice.StressModifier != 0 )
		{
			foreach ( var opId in _mission.AssignedOperativeIds )
			{
				var op = world.GetOperative( opId );
				if ( op != null )
					op.Psychology.Stress = Math.Clamp(
						op.Psychology.Stress + choice.StressModifier, 0, 100 );
			}
		}

		// Track wet-work escalation
		if ( choice.WetWork )
			Overrides.ForcedWetWork = true;

		// Advance to next eligible node
		_currentNodeIndex++;

		// Re-filter remaining nodes (approach gating may open/close paths)
		if ( _currentNodeIndex < _eligibleNodes.Count )
		{
			var remaining = _eligibleNodes.Skip( _currentNodeIndex ).ToList();
			var refiltered = FilterEligibleNodes( remaining, _mission, world );
			_eligibleNodes = _eligibleNodes.Take( _currentNodeIndex ).Concat( refiltered ).ToList();
		}

		return choice.Aftermath;
	}

	/// <summary>
	/// Cancel the active sequence. Partial overrides are discarded.
	/// </summary>
	public void Cancel()
	{
		_sequence = null;
		_mission = null;
		_world = null;
		_currentNodeIndex = 0;
		_eligibleNodes.Clear();
		_approachHistory.Clear();
		Overrides = new NarrativeOverrides();
	}

	/// <summary>
	/// Resolve token placeholders in node text using mission and world state.
	/// </summary>
	public string ResolveText( string text )
	{
		if ( _mission == null || _world == null ) return text;

		var ops = _mission.AssignedOperativeIds
			.Select( id => _world.GetOperative( id ) )
			.Where( o => o != null )
			.Cast<Operative>()
			.ToList();

		var leader = ops.OrderByDescending( o => o.Skills.Combat + o.Skills.Stealth ).FirstOrDefault();
		string leaderName = leader != null
			? ( string.IsNullOrEmpty( leader.Codename ) ? leader.Name : leader.Codename )
			: "the team";

		string client = !string.IsNullOrEmpty( _mission.ClientFactionId )
			? _world.GetFaction( _mission.ClientFactionId )?.Name ?? _mission.ClientFactionId
			: "the client";

		string target = !string.IsNullOrEmpty( _mission.TargetFactionId )
			? _world.GetFaction( _mission.TargetFactionId )?.Name ?? _mission.TargetFactionId
			: "the target";

		return text
			.Replace( "{leader}", leaderName )
			.Replace( "{target}", target )
			.Replace( "{client}", client )
			.Replace( "{location}", _world.Setting.Location )
			.Replace( "{corp}", _world.Setting.CorpName );
	}

	// ---- Node filtering ----

	private List<NarrativeNode> FilterEligibleNodes(
		IEnumerable<NarrativeNode> nodes, Mission mission, WorldState world )
	{
		var eligible = new List<NarrativeNode>();
		foreach ( var node in nodes )
		{
			// Approach gating: skip if requires an approach we haven't taken
			if ( !string.IsNullOrEmpty( node.RequiresApproach )
				&& !_approachHistory.Contains( node.RequiresApproach ) )
				continue;

			// Stat gating: skip if operative stats don't meet threshold
			if ( !MeetsStatRequirement( node.RequiresStat, mission, world ) )
				continue;

			// Roll gating: random probability filter
			if ( node.RollThreshold.HasValue )
			{
				// Deterministic from node index for consistency
				double roll = (eligible.Count * 0.37 + mission.Difficulty * 0.01) % 1.0;
				if ( roll >= node.RollThreshold.Value ) continue;
			}

			eligible.Add( node );
		}
		return eligible;
	}

	/// <summary>
	/// "Skill:threshold" gate shared by node- and choice-level RequiresStat:
	/// true when any ASSIGNED operative meets the bar (or the spec is empty/malformed).
	/// </summary>
	private static bool MeetsStatRequirement( string? spec, Mission mission, WorldState world )
	{
		if ( string.IsNullOrEmpty( spec ) ) return true;
		var parts = spec.Split( ':' );
		if ( parts.Length != 2 || !int.TryParse( parts[1], out int threshold ) ) return true;
		return mission.AssignedOperativeIds
			.Select( id => world.GetOperative( id ) )
			.Where( o => o != null )
			.Any( o => GetSkillValue( o!, parts[0] ) >= threshold );
	}

	private static int GetSkillValue( Operative op, string skill ) => skill.ToLowerInvariant() switch
	{
		"combat" => op.Skills.Combat,
		"stealth" => op.Skills.Stealth,
		"hacking" => op.Skills.Hacking,
		"deception" => op.Skills.Deception,
		"intimidation" => op.Skills.Intimidation,
		"persuasion" => op.Skills.Persuasion,
		_ => 0,
	};
}
