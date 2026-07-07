// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Corporate;
using CWC.Domain;
using CWC.Generation;
using CWC.Generation.Templates;
using CWC.Missions;
using CWC.Narrative;
using CWC.Save;

namespace CWC.Game;

/// <summary>
/// Top-level orchestrator. Owns the phase loop:
///   Briefing (decay/recovery, board refresh, role drift)
///   → Assignment (player assigns; narrative sequences play here)
///   → Resolution (missions resolve, corruption recomputes)
///   → Corporate (mission fallout applies FIRST, then factions/board act)
///   → Aftermath (scene director fires)
///   → Review.
/// </summary>
public sealed class GameManager
{
	public WorldState World { get; private set; } = new();
	public PhaseManager Phase { get; } = new();
	public Rng Rng { get; private set; } = new( 1 );
	public EventBus Bus { get; } = new();

	public MissionGenerator MissionGen { get; private set; } = new( new List<MissionTemplate>() );
	public MissionBoard Board { get; private set; }
	public MissionResolver Resolver { get; } = new();
	public ConsequenceProcessor Consequences { get; } = new();
	public MissionNarrativeRunner NarrativeRunner { get; } = new();
	public NarrativeDirector Director { get; private set; } = new( new List<SceneTemplate>() );

	// Corporate-sim layer.
	public CorporateConsequenceProcessor CorpConsequences { get; private set; }
	public FactionSystem? Factions { get; private set; }
	public PoliticsSystem? Politics { get; private set; }
	public ContractSystem? Contracts { get; private set; }
	public ReputationSystem? Reputation { get; private set; }
	public CorporateEventGenerator? CorporateEvents { get; private set; }

	// Save/load
	public SaveSystem SaveSystem { get; } = new();

	public List<MissionResult> LastResolutionResults { get; private set; } = new();

	/// <summary>
	/// Template load errors + content validation problems from the last NewGame.
	/// Empty means all content loaded clean. Surfaced by the engine entry point
	/// and asserted on by the smoke test — silent content loss is a bug class
	/// this project is not allowed to have anymore.
	/// </summary>
	public List<string> ContentWarnings { get; } = new();

	/// <summary>
	/// Long-lived RNG streams (corporate systems advance theirs across cycles).
	/// Tracked by label so their positions can be serialized into saves —
	/// determinism survives a load.
	/// </summary>
	private readonly Dictionary<string, Rng> _persistentStreams = new();
	public IReadOnlyDictionary<string, Rng> PersistentStreams => _persistentStreams;

	public event Action? StateChanged;

	public GameManager()
	{
		Board = new MissionBoard( MissionGen );
		CorpConsequences = new CorporateConsequenceProcessor( Bus );
	}

	/// <summary>Load a saved game from a named slot.</summary>
	public bool LoadGame( string slotName )
	{
		var data = SaveSystem.Load( slotName );
		if ( data == null ) return false;

		// Re-initialize from the saved seed to rebuild templates
		NewGame( data.Seed );

		// Overwrite with saved state
		SaveSystem.Restore( data, this );
		StateChanged?.Invoke();
		return true;
	}

	public void NewGame( ulong seed )
	{
		Rng = new Rng( seed );
		Phase.Reset();
		NarrativeRunner.Cancel();
		LastResolutionResults.Clear();
		ContentWarnings.Clear();
		_persistentStreams.Clear();

		// Drop subscriptions from any prior run so old captured systems don't
		// double-fire mutations on top of the new ones we'll wire below.
		Bus.Clear();

		var loader = new TemplateLoader();
		try
		{
			World = new WorldGenerator().Generate( seed );
		}
		catch ( Exception e )
		{
			// Defensive fallback so the game still launches if templates are missing —
			// but never silently.
			CwcLog.Warn( $"World generation failed ({e.Message}); using fallback world." );
			ContentWarnings.Add( $"world generation failed: {e.Message}" );
			World = new WorldState { Seed = seed };
			ScaffoldFallbackWorld();
		}

		MissionGen = new MissionGenerator( loader );
		Board = new MissionBoard( MissionGen );

		var scenes = loader.LoadScenes();
		Director = new NarrativeDirector( scenes );

		// Seed opening mission and refresh the board for cycle 1.
		Board.SeedFromScenario( World.SeedMissionTemplateId, World, Rng.Fork( "scenario_seed" ) );
		Board.Refresh( World, Rng.Fork( "board:1" ) );

		WireCorporateLayer( loader );

		// Content validation — loud at boot, not silent at runtime. Runs after
		// the corporate layer so faction cross-references check against the
		// COMPLETE faction list (world.json + corporate/factions.json).
		var factionIds = new HashSet<string>( World.Factions.Select( f => f.Id ) );
		ContentWarnings.AddRange( loader.Errors );
		ContentWarnings.AddRange( TemplateValidator.ValidateScenes( scenes ) );
		ContentWarnings.AddRange( TemplateValidator.ValidateMissions( MissionGen.Templates.ToList(), factionIds ) );
		ContentWarnings.AddRange( TemplateValidator.ValidateArchetypes( loader.LoadArchetypes(), loader.LoadTraits() ) );
		foreach ( var w in ContentWarnings )
			CwcLog.Warn( "content: " + w );

		StateChanged?.Invoke();
	}

	private Rng PersistentFork( string label )
	{
		var rng = Rng.Fork( label );
		_persistentStreams[label] = rng;
		return rng;
	}

	private void WireCorporateLayer( TemplateLoader loader )
	{
		// Reset and wire fresh systems for this run.
		CorpConsequences = new CorporateConsequenceProcessor( Bus );
		Factions       = new FactionSystem( PersistentFork( "corp_factions" ), Bus, CorpConsequences );
		Politics       = new PoliticsSystem( PersistentFork( "corp_politics" ), Bus, CorpConsequences );
		Contracts      = new ContractSystem( PersistentFork( "corp_contracts" ), Bus, CorpConsequences );
		Reputation     = new ReputationSystem( PersistentFork( "corp_reputation" ), Bus, CorpConsequences );
		CorporateEvents = new CorporateEventGenerator( PersistentFork( "corp_events" ), Bus, CorpConsequences );

		RebuildCorporateViews();

		// Layer corporate templates on top of any defaults already provided by
		// world.json. The loader MERGES into existing faction objects — corp and
		// world views must reference the same instances (no split-brain).
		CorporateDataLoader.LoadFactions( World, loader );
		CorporateDataLoader.LoadDirectives( World.Corporate, loader );
		CorporateDataLoader.LoadEvents( CorporateEvents, loader );

		// Subscribe corporate systems to mission-resolution events.
		Bus.Subscribe<MissionResolved>( r => Factions!.OnMissionResolved( World.Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Politics!.OnMissionResolved( World.Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Reputation!.OnMissionResolved( World.Corporate, World, r ) );

		// Corporate events become world texture and narrative hooks — the bus
		// carries consequences to systems its publishers don't know about.
		Bus.Subscribe<CorporateEventFired>( e => World.Headlines.Add( e.Narrative ) );
		Bus.Subscribe<RankChanged>( e => World.NarrativeFlags.Add(
			e.To > e.From ? "corp:promoted" : "corp:demoted" ) );
		Bus.Subscribe<ReputationThresholdCrossed>( e =>
			World.NarrativeFlags.Add( $"corp:threshold:{e.Kind.ToString().ToLowerInvariant()}" ) );
	}

	/// <summary>
	/// (Re)build the corporate-layer views over world data. The corp Roster and
	/// Factions dictionary are views of the SAME objects held by WorldState —
	/// called at NewGame and again after a save restore so the views never
	/// point at pre-restore phantoms.
	/// </summary>
	public void RebuildCorporateViews()
	{
		World.Corporate.Roster.Clear();
		foreach ( var op in World.Operatives )
			if ( !op.IsExecutive ) World.Corporate.Roster.Add( op );

		World.Corporate.Factions.Clear();
		foreach ( var f in World.Factions )
			World.Corporate.Factions[f.Id] = f;
	}

	public void AdvancePhase()
	{
		// Assignment gate: a high-stakes mission with an authored sequence must
		// play its nodes before resolution. This is the Telltale half's
		// ignition wire.
		if ( Phase.CurrentPhase == CyclePhase.Assignment )
		{
			if ( NarrativeRunner.IsActive )
			{
				// Mid-sequence — the player finishes the nodes before locking in.
				StateChanged?.Invoke();
				return;
			}
			if ( !NarrativeRunner.IsComplete && TryBeginNarrativeSequence() )
			{
				StateChanged?.Invoke();
				return;
			}
		}

		Phase.Advance();
		var current = Phase.CurrentPhase;

		switch ( current )
		{
			case CyclePhase.Briefing:
				// New cycle — bookkeeping the foundation owns.
				World.Corporate.Cycle++;
				World.Day++;
				RecoverInjuredOps();
				DecayStress();
				ClearRecoveredTripwires();
				ProcessAmbition();
				Director.ReEvaluateRoles( World.Operatives );
				Board.Refresh( World, Rng.Fork( $"board:{World.Corporate.Cycle}" ) );
				// Auto-save AFTER bookkeeping so a load resumes a coherent
				// cycle boundary (board refreshed, decay applied).
				SaveSystem.AutoSave( this );
				break;

			case CyclePhase.Resolution:
				ResolveActiveMissions();
				// Recompute corruption index after mission outcomes
				World.Corruption.Compute( World );
				break;

			case CyclePhase.Corporate:
				RunCorporatePhase();
				break;

			case CyclePhase.Aftermath:
				Director.ConsumeFlags( World );
				break;
		}

		StateChanged?.Invoke();
	}

	/// <summary>
	/// Start the narrative sequence for the first assigned mission that has one.
	/// Returns true if a sequence began (the phase advance is held).
	/// </summary>
	private bool TryBeginNarrativeSequence()
	{
		foreach ( var m in World.Missions )
		{
			if ( m.Status != MissionStatus.Active ) continue;
			if ( m.AssignedOperativeIds.Count == 0 ) continue;
			if ( m.NarrativeSequence == null ) continue;
			if ( NarrativeRunner.Begin( m, World ) ) return true;
		}
		return false;
	}

	private void ResolveActiveMissions()
	{
		LastResolutionResults.Clear();
		// Clear previous catastrophe flag before this cycle's resolution
		World.NarrativeFlags.Remove( "last_mission:catastrophe" );

		// Snapshot to allow status mutation while iterating.
		var active = new List<Mission>();
		foreach ( var m in World.Missions )
			if ( m.Status == MissionStatus.Active && m.AssignedOperativeIds.Count > 0 )
				active.Add( m );

		foreach ( var m in active )
		{
			// Use narrative overrides if this mission had a completed sequence
			NarrativeOverrides? overrides = null;
			if ( NarrativeRunner.IsComplete && NarrativeRunner.ActiveMission?.Id == m.Id )
			{
				overrides = NarrativeRunner.Overrides;
				overrides.SequenceCompleted = true;
			}

			var result = Resolver.Resolve( m, World, Rng.Fork( $"resolve:{m.Id}" ), overrides );
			Consequences.Apply( result, World );
			LastResolutionResults.Add( result );

			// Track consecutive successes and catastrophe flag for scene triggers
			if ( result.Outcome == MissionOutcome.Success )
				World.ConsecutiveSuccesses++;
			else
			{
				World.ConsecutiveSuccesses = 0;
				if ( result.Outcome == MissionOutcome.Catastrophe )
					World.NarrativeFlags.Add( "last_mission:catastrophe" );
			}

			// Notify corporate systems that subscribe via the bus.
			Bus.Publish( new MissionResolved(
				m,
				result.Outcome,
				m.Exposure,
				result.Outcome == MissionOutcome.Success ? m.Reward : 0
			) );
		}

		// Clear narrative runner after all missions resolve
		NarrativeRunner.Cancel();
	}

	private void RunCorporatePhase()
	{
		if ( Factions == null ) return; // pre-NewGame call — nothing to do.

		// Mission fallout queued during Resolution applies FIRST, so the board
		// evaluates this cycle's numbers — not last cycle's.
		CorpConsequences.ApplyAll( World.Corporate );

		Factions.ProcessTurn( World.Corporate );
		Contracts!.RefreshContracts( World.Corporate, World );
		CorporateEvents!.Roll( World.Corporate, World );
		Reputation!.Decay( World.Corporate, World );
		CorpConsequences.ApplyAll( World.Corporate );

		// Board evaluates after all corporate-phase fallout has landed.
		Politics!.EvaluateBoard( World.Corporate, World );
		CorpConsequences.ApplyAll( World.Corporate );

		// Surface contracts on the mission board so the player can see them.
		foreach ( var contract in World.Corporate.AvailableContracts )
		{
			if ( !World.Missions.Any( m => m.Id == contract.Id ) )
				World.Missions.Add( contract );
		}

		Director.CheckCorporateTriggers( World );
	}

	/// <summary>
	/// Assignment-phase action. Adds operative to mission roster.
	/// Returns false if assignment is invalid (op unavailable, mission not
	/// available), or if the operative REFUSES: a high-conscience operative
	/// will not take a wet-work contract. Refusal leaves a narrative flag
	/// (refusal:{id}) the scene director can react to.
	/// </summary>
	public bool Assign( int operativeId, string missionId )
	{
		var op = World.GetOperative( operativeId );
		var mission = World.GetMission( missionId );
		if ( op == null || mission == null ) return false;
		if ( op.IsExecutive ) return false;
		if ( !op.IsAvailable ) return false;
		if ( mission.Status != MissionStatus.Available && mission.Status != MissionStatus.Active )
			return false;
		if ( mission.AssignedOperativeIds.Contains( operativeId ) ) return false;

		// Conscience produces behavior, not just a gauge: some people won't
		// cross certain lines.
		if ( mission.IsWetWork && op.Psychology.Conscience >= 75 )
		{
			World.NarrativeFlags.Add( $"refusal:{op.Id}" );
			op.Psychology.Stress = Math.Clamp( op.Psychology.Stress + 5, 0, 100 );
			World.Corporate.RecentEventLog.Add(
				$"[Roster] {op.Codename} refused assignment to '{mission.Title}'." );
			StateChanged?.Invoke();
			return false;
		}

		mission.AssignedOperativeIds.Add( operativeId );
		op.CurrentMissionId = missionId;
		if ( mission.Status == MissionStatus.Available )
			mission.Status = MissionStatus.Active;

		StateChanged?.Invoke();
		return true;
	}

	public bool Unassign( int operativeId, string missionId )
	{
		var op = World.GetOperative( operativeId );
		var mission = World.GetMission( missionId );
		if ( op == null || mission == null ) return false;
		if ( !mission.AssignedOperativeIds.Remove( operativeId ) ) return false;
		op.CurrentMissionId = null;
		if ( mission.AssignedOperativeIds.Count == 0 && mission.Status == MissionStatus.Active )
			mission.Status = MissionStatus.Available;
		StateChanged?.Invoke();
		return true;
	}

	/// <summary>
	/// Spend political capital to negotiate better terms on a faction contract.
	/// The player-facing sink for the political-capital economy.
	/// </summary>
	public NegotiationResult NegotiateContract( string missionId, NegotiationLever lever )
	{
		var mission = World.GetMission( missionId );
		if ( mission == null || Contracts == null )
			return new NegotiationResult( false, "No such contract.", 0 );
		var result = Contracts.Negotiate( World.Corporate, mission, lever );
		StateChanged?.Invoke();
		return result;
	}

	// ---- end-of-cycle bookkeeping ----------------------------------------

	private void RecoverInjuredOps()
	{
		foreach ( var op in World.Operatives )
		{
			if ( op.IsExecutive ) continue;
			if ( op.Status == OperativeStatus.Injured ) op.Status = OperativeStatus.Active;
			if ( op.Status == OperativeStatus.Active ) op.Tenure++;
		}
	}

	/// <summary>
	/// Full psychology pass. Runs at start of each new cycle. Executives are
	/// excluded — they are furniture, not team members.
	///
	/// The humane path pays in resilience: a rested operative recovers stress
	/// faster, regains morale, and (below the Effective corruption threshold)
	/// even recovers conscience. Ambient conscience erosion only exists once
	/// the division itself has become corrupting (index >= 40) — the world
	/// darkens because you darkened it.
	/// </summary>
	private void DecayStress()
	{
		bool corruptCulture = World.Corruption.CorruptionIndex >= 40;

		foreach ( var op in World.Operatives )
		{
			if ( op.IsExecutive ) continue;
			if ( !op.Active ) continue;
			var p = op.Psychology;
			bool resting = op.CurrentMissionId == null;

			// Stress bleeds off naturally; genuinely resting doubles recovery.
			p.Stress = Math.Clamp( p.Stress - ( resting ? 6 : 3 ), 0, 100 );

			// Morale: rest restores it, constant deployment grinds it down.
			if ( resting && p.Stress < 50 )
				p.Morale = Math.Clamp( p.Morale + 1, 0, 100 );
			else
				p.Morale = Math.Clamp( p.Morale - 1, 0, 100 );

			// Conscience: erodes only inside a corrupt culture; recovers slowly
			// for rested operatives inside a humane one.
			if ( corruptCulture )
			{
				if ( World.Corporate.Cycle % 5 != 0 )
					p.Conscience = Math.Clamp( p.Conscience - 1, 0, 100 );
			}
			else if ( resting && World.Corporate.Cycle % 2 == 0 )
			{
				p.Conscience = Math.Clamp( p.Conscience + 1, 0, 100 );
			}

			// Loyalty: tenure builds commitment, but stress and low morale erode it
			int loyaltyDelta = 0;
			if ( op.Tenure > 2 ) loyaltyDelta += 1;
			if ( p.Stress > 70 ) loyaltyDelta -= 1;
			if ( p.Morale < 35 ) loyaltyDelta -= 1;
			p.Loyalty = Math.Clamp( p.Loyalty + loyaltyDelta, 0, 100 );
		}
	}

	/// <summary>
	/// Tripwire flags are states, not events: when an operative recovers, the
	/// flag clears — otherwise a loyalty crisis becomes wallpaper that re-fires
	/// scenes forever.
	/// </summary>
	private void ClearRecoveredTripwires()
	{
		foreach ( var op in World.Operatives )
		{
			if ( op.IsExecutive ) continue;
			var p = op.Psychology;
			if ( p.Conscience > 25 ) World.NarrativeFlags.Remove( $"hollowed_out:{op.Id}" );
			if ( p.Stress < 75 ) World.NarrativeFlags.Remove( $"breaking_point:{op.Id}" );
			if ( p.Loyalty > 30 ) World.NarrativeFlags.Remove( $"defection_risk:{op.Id}" );
			World.NarrativeFlags.Remove( $"refusal:{op.Id}" );
			World.NarrativeFlags.Remove( $"ambition_leak:{op.Id}" );
		}
		if ( World.Corporate.Heat < 70 ) World.NarrativeFlags.Remove( "heat:critical" );
		if ( World.Corporate.Suspicion < 70 ) World.NarrativeFlags.Remove( "suspicion:critical" );
	}

	/// <summary>
	/// Ambition produces behavior: a hungry, disloyal operative leaks to the
	/// other divisions to climb. Visible in the corporate log and as a
	/// narrative flag.
	/// </summary>
	private void ProcessAmbition()
	{
		var rng = Rng.Fork( $"ambition:{World.Corporate.Cycle}" );
		foreach ( var op in World.ActiveRoster )
		{
			var p = op.Psychology;
			if ( p.Ambition >= 75 && p.Loyalty < 40 && rng.Chance( 0.15 ) )
			{
				World.Corporate.Suspicion = Math.Clamp( World.Corporate.Suspicion + 4, 0, 100 );
				World.NarrativeFlags.Add( $"ambition_leak:{op.Id}" );
				World.Corporate.RecentEventLog.Add(
					$"[Roster] Internal audit traced a document leak to {op.Codename}'s terminal." );
			}
		}
	}

	// ---- Fallback world (deterministic placeholder) -------------

	private void ScaffoldFallbackWorld()
	{
		var nameRng = Rng.Fork( "names" );
		var statRng = Rng.Fork( "stats" );

		World.Setting = new WorldSetting
		{
			CorpName = "Panopticon Holdings",
			Location = "Neo-Detroit",
			Year = 2087,
			EraTagline = "after the second collapse",
			ToneTags = new List<string> { "noir", "cyberpunk", "corporate" },
		};
		World.Corporate = new CorporateState
		{
			Heat = 10, Suspicion = 5, Reputation = 50, Budget = 100_000, Cycle = 1,
		};

		// Three placeholder factions so cross-faction mission templates have anchors.
		World.Factions.Add( new Faction { Id = "host", Name = "Panopticon Holdings", Kind = FactionKind.HostCorp, Standing = 0 } );
		World.Factions.Add( new Faction { Id = "rival_kasumi", Name = "Kasumi Dynamics", Kind = FactionKind.RivalCorp, Standing = -25 } );
		World.Factions.Add( new Faction { Id = "razor", Name = "The Razor", Kind = FactionKind.Agency, Standing = -50 } );

		// Four placeholder operatives so the mission loop has bodies to throw at missions.
		string[] firsts = { "Mara", "Cyrus", "Vivienne", "Jin", "Emil", "Renko", "Lila", "Kade" };
		string[] codes = { "GHOST", "STITCH", "ECHO", "RIDER", "ORACLE", "WICK" };
		string[] arches = { "operator", "ghost", "decker", "fixer" };

		for ( int i = 0; i < 4; i++ )
		{
			var op = new Operative
			{
				Id = i + 1,
				Name = nameRng.Pick( firsts ),
				Codename = nameRng.Pick( codes ),
				Archetype = arches[i % arches.Length],
				Gender = nameRng.Chance( 0.45 ) ? "F" : nameRng.Chance( 0.82 ) ? "M" : "NB",
				Tenure = 0,
			};
			op.Skills.Combat = statRng.BellInt( 30, 80 );
			op.Skills.Stealth = statRng.BellInt( 30, 80 );
			op.Skills.Hacking = statRng.BellInt( 30, 80 );
			op.Skills.Deception = statRng.BellInt( 30, 80 );
			op.Skills.Intimidation = statRng.BellInt( 20, 70 );
			op.Skills.Persuasion = statRng.BellInt( 20, 70 );

			// Archetype tilt — coarse fallback only.
			switch ( op.Archetype )
			{
				case "operator": op.Skills.Combat += 15; op.Skills.Intimidation += 10; break;
				case "ghost": op.Skills.Stealth += 15; op.Skills.Deception += 10; break;
				case "decker": op.Skills.Hacking += 20; break;
				case "fixer": op.Skills.Persuasion += 15; op.Skills.Deception += 10; break;
			}

			World.Operatives.Add( op );
		}
	}
}
