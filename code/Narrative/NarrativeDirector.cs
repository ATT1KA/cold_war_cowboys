// [MIXED] ~45% framework (flag eligibility, priority queue, role casting, tokens, choice log)
// and ~55% CWC (stat names, resolvers, corruption hooks, tripwires). Do NOT genericize —
// re-derive the pattern for a new game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CWC.Core;
using CWC.Domain;

namespace CWC.Narrative;

/// <summary>
/// Role-based casting, token resolution, gender-conditional text.
///
/// Reads narrative flags + stat thresholds off WorldState and queues scenes for
/// the UI. Selection is OneShot-aware, repeatable scenes carry a cooldown so a
/// persistent flag doesn't turn a crisis into wallpaper, and the queue never
/// holds two copies of the same template.
///
/// Token resolution runs at Materialize time:
///   {operative.name}           → resolved operative's display name
///   {operative.role:mirror}    → name of op filling the "mirror" cast slot
///   {faction.name}             → most hostile rival faction (never your own corp)
///   {gender:m|A|B}             → conditional on the CAST OPERATIVE's gender
///                                (protagonist gender only when no operative is cast)
///   {corp}, {location}, {year} → world-setting tokens
/// </summary>
public sealed class NarrativeDirector
{
	/// <summary>Cycles a repeatable (non-OneShot) template must wait before re-firing.</summary>
	private const int RepeatCooldownCycles = 6;

	private readonly Dictionary<string, SceneTemplate> _templates;
	private readonly HashSet<string> _firedOneShots = new();
	private readonly Dictionary<string, int> _lastFiredCycle = new();
	private readonly Queue<Scene> _queue = new();

	public NarrativeDirector( IEnumerable<SceneTemplate> templates )
	{
		_templates = templates.ToDictionary( t => t.Id, t => t );
	}

	public int QueueDepth => _queue.Count;
	public IReadOnlyCollection<Scene> Queued => _queue;
	public int TemplateCount => _templates.Count;

	// ---- Save/load support ----

	/// <summary>One-shot templates that have fired, for serialization.</summary>
	public IReadOnlyCollection<string> FiredOneShots => _firedOneShots;

	/// <summary>Restore fired one-shot state from a save so scenes don't re-fire on load.</summary>
	public void RestoreFiredState( IEnumerable<string> firedOneShots )
	{
		_firedOneShots.Clear();
		foreach ( var id in firedOneShots ) _firedOneShots.Add( id );
	}

	// ========================================================================
	// SCENE SELECTION — scan triggers, enqueue eligible scenes
	// ========================================================================

	/// <summary>
	/// Scan world state, resolve all eligible templates, and enqueue scenes
	/// in priority order. Call after ConsequenceProcessor finishes a cycle's
	/// resolution (i.e. at Aftermath phase).
	/// </summary>
	public IReadOnlyList<Scene> ConsumeFlags( WorldState world )
	{
		var fresh = new List<Scene>();
		int cycle = world.Corporate.Cycle;
		var alreadyQueued = new HashSet<string>( _queue.Select( s => s.TemplateId ) );

		foreach ( var template in _templates.Values )
		{
			if ( template.OneShot && _firedOneShots.Contains( template.Id ) ) continue;
			// Repeatable scenes: cooldown so persistent flags don't re-fire every cycle.
			if ( !template.OneShot
				&& _lastFiredCycle.TryGetValue( template.Id, out var last )
				&& cycle - last < RepeatCooldownCycles ) continue;
			// Never queue a duplicate of something already waiting.
			if ( alreadyQueued.Contains( template.Id ) ) continue;
			if ( IsForbidden( template, world ) ) continue;
			if ( !IsEligible( template, world, out int? opId ) ) continue;

			var scene = Materialize( template, world, opId );
			fresh.Add( scene );
			alreadyQueued.Add( template.Id );
		}

		fresh.Sort( ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		foreach ( var s in fresh ) _queue.Enqueue( s );
		return fresh;
	}

	/// <summary>
	/// Pop the next scene for presentation. Marks OneShot templates fired
	/// and applies template-level FlagsOnFire to WorldState.
	/// </summary>
	public Scene? PopNextScene( WorldState world )
	{
		if ( _queue.Count == 0 ) return null;
		var scene = _queue.Dequeue();
		var template = _templates.TryGetValue( scene.TemplateId, out var t ) ? t : null;
		if ( template != null )
		{
			if ( template.OneShot ) _firedOneShots.Add( template.Id );
			_lastFiredCycle[template.Id] = world.Corporate.Cycle;
		}
		foreach ( var f in scene.FlagsOnFire )
			world.NarrativeFlags.Add( f );
		return scene;
	}

	/// <summary>
	/// Sprint 6 hook: scan CorporateState for boardroom-level triggers.
	/// </summary>
	public void CheckCorporateTriggers( WorldState world )
	{
		var corp = world.Corporate;
		if ( corp.BoardConfidence >= 85 && corp.Rank < CorporateRank.BoardLiaison )
			world.NarrativeFlags.Add( "corp:promotion_imminent" );
		else if ( corp.BoardConfidence <= 15 && corp.Rank > CorporateRank.Probationary )
			world.NarrativeFlags.Add( "corp:demotion_imminent" );

		if ( corp.Suspicion >= 75 )
			world.NarrativeFlags.Add( "corp:audit_triggered" );

		foreach ( var f in world.Factions )
		{
			if ( f.RelationshipToPlayer <= -75 )
				world.NarrativeFlags.Add( $"corp:confrontation:{f.Id}" );
		}
	}

	/// <summary>
	/// Apply player choice from a scene. Mutates the targeted operative + corp,
	/// records the decision in the choice log, and feeds the corruption tracker.
	/// </summary>
	public IReadOnlyList<string> ApplyChoice( Scene scene, SceneChoice choice, WorldState world )
	{
		if ( scene.OperativeId.HasValue )
		{
			var op = world.GetOperative( scene.OperativeId.Value );
			if ( op != null )
			{
				op.Psychology.Loyalty    = Math.Clamp( op.Psychology.Loyalty + choice.LoyaltyDelta, 0, 100 );
				op.Psychology.Stress     = Math.Clamp( op.Psychology.Stress + choice.StressDelta, 0, 100 );
				op.Psychology.Conscience = Math.Clamp( op.Psychology.Conscience + choice.ConscienceDelta, 0, 100 );
				op.Psychology.Morale     = Math.Clamp( op.Psychology.Morale + choice.MoraleDelta, 0, 100 );
			}
		}
		world.Corporate.Heat = Math.Clamp( world.Corporate.Heat + choice.HeatDelta, 0, 100 );
		foreach ( var f in choice.FlagsOnPick ) world.NarrativeFlags.Add( f );

		// The corruption arc is authored by decisions, not just drift.
		bool cold = choice.ConscienceDelta < 0
			|| choice.FlagsOnPick.Any( f => f.Contains( "cold" ) || f.Contains( "transactional" ) || f.Contains( "machine" ) );
		bool humane = choice.ConscienceDelta > 0
			|| choice.FlagsOnPick.Any( f => f.Contains( "human" ) || f.Contains( "mercy" ) );
		if ( cold ) world.Corruption.RegisterChoice( +4.0 );
		else if ( humane ) world.Corruption.RegisterChoice( -2.0 );

		world.ChoiceLog.Add( new ChoiceRecord
		{
			Cycle = world.Corporate.Cycle,
			Source = "scene",
			SourceId = scene.TemplateId,
			Label = choice.Label,
			Flags = new List<string>( choice.FlagsOnPick ),
			OperativeId = scene.OperativeId,
		} );

		return choice.FlagsOnPick;
	}

	// ========================================================================
	// ROLE DRIFT — narrative roles follow psychology across the run
	// ========================================================================

	/// <summary>
	/// Re-evaluate each operative's narrative role against current psychology.
	/// Called once per cycle at Briefing. An operative whose stats no longer
	/// match the role thresholds loses the tag; one whose stats have drifted
	/// into a new band may gain one. Roles are seeded by archetype at
	/// generation and drift from there.
	/// </summary>
	public void ReEvaluateRoles( IEnumerable<Operative> operatives )
	{
		foreach ( var op in operatives )
		{
			if ( !op.Active || op.IsExecutive ) continue;

			var role = op.NarrativeRole?.ToLowerInvariant() ?? "";
			var p = op.Psychology;

			switch ( role )
			{
				case "conscience":
					if ( p.Conscience < 30 ) op.NarrativeRole = "";
					break;
				case "weapon":
					if ( p.Conscience > 70 ) op.NarrativeRole = "";
					break;
				case "climber":
					if ( p.Ambition < 35 ) op.NarrativeRole = "";
					break;
				case "anchor":
					if ( p.Loyalty < 35 ) op.NarrativeRole = "";
					break;
				case "innocent":
					if ( p.Conscience < 40 || p.Stress > 70 ) op.NarrativeRole = "";
					break;
			}

			// If role was stripped, check for natural re-assignment
			if ( string.IsNullOrEmpty( op.NarrativeRole ) )
			{
				if ( p.Conscience > 65 ) op.NarrativeRole = "conscience";
				else if ( p.Conscience < 30 && p.Ambition < 40 ) op.NarrativeRole = "weapon";
				else if ( p.Ambition > 70 ) op.NarrativeRole = "climber";
				else if ( p.Loyalty > 70 ) op.NarrativeRole = "anchor";
				// Otherwise stays blank — narratively adrift
			}
		}
	}

	// ========================================================================
	// ELIGIBILITY — flag predicates + stat-based triggers
	// ========================================================================

	private static bool IsForbidden( SceneTemplate t, WorldState world )
	{
		foreach ( var f in t.ForbiddenFlags )
			if ( MatchesFlag( f, world, out _ ) ) return true;
		return false;
	}

	/// <summary>
	/// Check both legacy RequiredFlags AND structured Triggers. All must pass.
	/// </summary>
	private static bool IsEligible( SceneTemplate t, WorldState world, out int? opId )
	{
		opId = null;

		// Legacy flag requirements
		foreach ( var f in t.RequiredFlags )
		{
			if ( !MatchesFlag( f, world, out var matchedOp ) ) return false;
			if ( opId == null && matchedOp.HasValue ) opId = matchedOp;
		}

		// Structured triggers
		foreach ( var trigger in t.Triggers )
		{
			if ( !EvaluateTrigger( trigger, world, ref opId ) ) return false;
		}

		return true;
	}

	private static bool EvaluateTrigger( SceneTrigger trigger, WorldState world, ref int? opId )
	{
		// Executives never participate in team math or scene casting.
		var active = world.ActiveRoster.ToList();
		if ( active.Count == 0 ) return false;

		switch ( trigger.Type )
		{
			case "avg_stat_below":
				return GetAvgStat( active, trigger.Key ) < trigger.Threshold;

			case "any_stat_below":
				var low = active.FirstOrDefault( o => GetStat( o, trigger.Key ) < trigger.Threshold );
				if ( low == null ) return false;
				opId ??= low.Id;
				return true;

			case "any_relationship_below":
				var badRel = world.Relationships.FirstOrDefault( r => r.Score < trigger.Threshold );
				if ( badRel == null ) return false;
				opId ??= badRel.FromId;
				return true;

			case "any_relationship_above":
				var goodRel = world.Relationships.FirstOrDefault( r => r.Score > trigger.Threshold );
				if ( goodRel == null ) return false;
				opId ??= goodRel.FromId;
				return true;

			case "no_active_missions":
				return !world.Missions.Any( m => m.Status == MissionStatus.Active );

			case "flag":
				return MatchesFlag( trigger.Key, world, out _ );

			case "flag_prefix":
				return world.NarrativeFlags.Any( f => f.StartsWith( trigger.Key ) );

			case "any_op":
				string prefix = trigger.Key + ":";
				var hit = world.NarrativeFlags.FirstOrDefault( f => f.StartsWith( prefix ) );
				if ( hit == null ) return false;
				if ( int.TryParse( hit.Substring( prefix.Length ), out var id ) ) opId ??= id;
				return true;

			case "consecutive_successes":
				return world.ConsecutiveSuccesses >= trigger.Threshold;

			case "board_confidence_below":
				return world.Corporate.BoardConfidence < trigger.Threshold;

			case "any_faction_relationship_below":
				return world.Factions.Any( f => f.RelationshipToPlayer < trigger.Threshold );

			case "cycle_reached":
				return world.Corporate.Cycle >= trigger.Threshold;

			case "active_operatives_below":
				return active.Count < trigger.Threshold;

			case "last_mission_catastrophe":
				return world.NarrativeFlags.Contains( "last_mission:catastrophe" );

			case "stress_below":
				return GetAvgStat( active, "stress" ) < trigger.Threshold;

			default:
				return false;
		}
	}

	private static double GetAvgStat( List<Operative> ops, string stat )
	{
		return ops.Average( o => (double)GetStat( o, stat ) );
	}

	private static int GetStat( Operative op, string stat ) => stat switch
	{
		"conscience" => op.Psychology.Conscience,
		"loyalty" => op.Psychology.Loyalty,
		"stress" => op.Psychology.Stress,
		"morale" => op.Psychology.Morale,
		"ambition" => op.Psychology.Ambition,
		_ => 0,
	};

	private static bool MatchesFlag( string spec, WorldState world, out int? opId )
	{
		opId = null;
		if ( spec.StartsWith( "flag_prefix:" ) )
		{
			string p = spec.Substring( "flag_prefix:".Length );
			return world.NarrativeFlags.Any( f => f.StartsWith( p ) );
		}
		if ( spec.StartsWith( "any_op:" ) )
		{
			string p = spec.Substring( "any_op:".Length ) + ":";
			var hit = world.NarrativeFlags.FirstOrDefault( f => f.StartsWith( p ) );
			if ( hit == null ) return false;
			if ( int.TryParse( hit.Substring( p.Length ), out var id ) ) opId = id;
			return true;
		}
		string raw = spec.StartsWith( "flag:" ) ? spec.Substring( "flag:".Length ) : spec;
		return world.NarrativeFlags.Contains( raw );
	}

	// ========================================================================
	// CAST RESOLUTION — resolve role slots to operatives
	// ========================================================================

	private static Dictionary<string, int> ResolveCast( SceneTemplate t, WorldState world, int? triggeringOpId )
	{
		var cast = new Dictionary<string, int>();
		var active = world.ActiveRoster.ToList();

		foreach ( var slot in t.Cast )
		{
			int? resolved = ResolveSlot( slot, world, active, triggeringOpId );
			if ( resolved.HasValue )
				cast[slot.Name] = resolved.Value;
		}

		// Always include "triggering" if we have a triggering operative
		if ( triggeringOpId.HasValue && !cast.ContainsKey( "triggering" ) )
			cast["triggering"] = triggeringOpId.Value;

		return cast;
	}

	private static int? ResolveSlot( CastSlot slot, WorldState world,
		List<Operative> active, int? triggeringOpId )
	{
		if ( slot.Kind == CastSlotKind.Identity )
		{
			if ( int.TryParse( slot.Resolver, out var id ) ) return id;
			return null;
		}

		// Role-based resolution
		return slot.Resolver switch
		{
			"triggering_operative" => triggeringOpId,
			"highest_conscience" => active.OrderByDescending( o => o.Psychology.Conscience ).FirstOrDefault()?.Id,
			"lowest_conscience" => active.OrderBy( o => o.Psychology.Conscience ).FirstOrDefault()?.Id,
			"highest_social" => active.OrderByDescending( o => o.Skills.Persuasion + o.Skills.Deception ).FirstOrDefault()?.Id,
			"lowest_loyalty" => active.OrderBy( o => o.Psychology.Loyalty ).FirstOrDefault()?.Id,
			"highest_stress" => active.OrderByDescending( o => o.Psychology.Stress ).FirstOrDefault()?.Id,
			"highest_morale" => active.OrderByDescending( o => o.Psychology.Morale ).FirstOrDefault()?.Id,
			"lowest_morale" => active.OrderBy( o => o.Psychology.Morale ).FirstOrDefault()?.Id,
			"first_mission_op" => triggeringOpId ?? active.FirstOrDefault()?.Id,
			"last_catastrophe_op" => triggeringOpId ?? active.OrderByDescending( o => o.Psychology.Stress ).FirstOrDefault()?.Id,
			_ when slot.Resolver.StartsWith( "role:" ) =>
				active.FirstOrDefault( o => o.NarrativeRole == slot.Resolver.Substring( "role:".Length ) )?.Id,
			_ => null,
		};
	}

	// ========================================================================
	// TOKEN RESOLUTION — render scene text with dynamic substitutions
	// ========================================================================

	private static readonly Regex GenderToken = new(
		@"\{gender:([mfn])\|([^|]*)\|([^}]*)\}", RegexOptions.Compiled );

	private static readonly Regex OperativeRoleToken = new(
		@"\{operative\.role:([^}]+)\}", RegexOptions.Compiled );

	private static readonly Regex OperativeNameToken = new(
		@"\{operative\.name\}", RegexOptions.Compiled );

	private static readonly Regex FactionToken = new(
		@"\{faction\.name\}", RegexOptions.Compiled );

	private static string ResolveTokens( string text, WorldState world,
		Dictionary<string, int> cast, int? primaryOpId )
	{
		// Legacy tokens
		string result = text
			.Replace( "{corp}", world.Setting.CorpName )
			.Replace( "{location}", world.Setting.Location )
			.Replace( "{year}", world.Setting.Year.ToString() );

		// {op} → legacy primary operative (backward compat)
		if ( primaryOpId.HasValue )
		{
			var op = world.GetOperative( primaryOpId.Value );
			string opName = op != null
				? ( string.IsNullOrEmpty( op.Codename ) ? op.Name : op.Codename )
				: "the field";
			result = result.Replace( "{op}", opName );
		}
		else
		{
			result = result.Replace( "{op}", "the field" );
		}

		// {operative.name} → primary operative's display name
		result = OperativeNameToken.Replace( result, m =>
		{
			if ( !primaryOpId.HasValue ) return "the operative";
			var op = world.GetOperative( primaryOpId.Value );
			return op != null
				? ( string.IsNullOrEmpty( op.Codename ) ? op.Name : op.Codename )
				: "the operative";
		} );

		// {operative.role:X} → name of operative filling role X in cast
		result = OperativeRoleToken.Replace( result, m =>
		{
			string role = m.Groups[1].Value;
			if ( cast.TryGetValue( role, out int opId ) )
			{
				var op = world.GetOperative( opId );
				return op != null
					? ( string.IsNullOrEmpty( op.Codename ) ? op.Name : op.Codename )
					: "someone";
			}
			// Also check by narrative role directly
			var byRole = world.Operatives.FirstOrDefault( o => o.NarrativeRole == role && o.Active && !o.IsExecutive );
			return byRole != null
				? ( string.IsNullOrEmpty( byRole.Codename ) ? byRole.Name : byRole.Codename )
				: "someone";
		} );

		// {faction.name} → the most hostile non-host faction. A scene about a
		// rival poaching your operative must never name your own corporation.
		result = FactionToken.Replace( result, m =>
		{
			var faction = world.Factions
				.Where( f => f.Kind != FactionKind.HostCorp )
				.OrderBy( f => f.RelationshipToPlayer )
				.FirstOrDefault();
			return faction?.Name ?? "the opposition";
		} );

		// {gender:m|A|B} → conditional on the CAST OPERATIVE's gender. The
		// content overwhelmingly uses this token about other people ("Promote
		// {gender:m|him|her}"), so it must read the operative the scene is
		// about — the protagonist's gender is only the fallback when a scene
		// has no cast (e.g. the Director addressing you).
		result = GenderToken.Replace( result, m =>
		{
			string condition = m.Groups[1].Value;
			string optionA = m.Groups[2].Value;
			string optionB = m.Groups[3].Value;

			string gender = "m";
			var castOp = primaryOpId.HasValue ? world.GetOperative( primaryOpId.Value ) : null;
			if ( castOp != null && !string.IsNullOrEmpty( castOp.Gender ) )
				gender = castOp.Gender.ToLowerInvariant();
			else
				gender = world.ProtagonistGender?.ToLowerInvariant() ?? "m";

			return condition switch
			{
				"m" => gender == "m" ? optionA : optionB,
				"f" => gender == "f" ? optionA : optionB,
				"n" => gender == "nb" ? optionA : optionB,
				_ => optionA,
			};
		} );

		return result;
	}

	// ========================================================================
	// PREVIEW — render any template without eligibility gating
	// ========================================================================

	/// <summary>
	/// Materialize a template against the given world regardless of triggers,
	/// cooldowns, or one-shot state. Powers tools/ScenePreview and the smoke
	/// test's render-every-scene pass; fires no flags and mutates nothing.
	/// </summary>
	public Scene RenderPreview( SceneTemplate template, WorldState world, int? triggeringOperativeId = null )
		=> Materialize( template, world, triggeringOperativeId );

	// ========================================================================
	// MATERIALIZE — build concrete Scene from template + world state
	// ========================================================================

	private static Scene Materialize( SceneTemplate t, WorldState world, int? opId )
	{
		var cast = ResolveCast( t, world, opId );

		// Primary operative: explicit triggering op, or first cast slot
		int? primaryOp = opId ?? ( cast.Count > 0 ? cast.Values.First() : null );

		string Resolve( string s ) => ResolveTokens( s, world, cast, primaryOp );

		// Tone modifier bands follow the milestone thresholds.
		var tone = world.Corruption.CorruptionIndex switch
		{
			>= 85 => ToneModifier.Hollow,
			>= 70 => ToneModifier.Transactional,
			>= 55 => ToneModifier.Guarded,
			_     => ToneModifier.Normal,
		};

		return new Scene
		{
			TemplateId = t.Id,
			Title = Resolve( t.Title ),
			Speaker = Resolve( t.Speaker ),
			Setting = Resolve( t.Setting ),
			Priority = t.Priority,
			TextLines = t.TextLines.Select( Resolve ).ToList(),
			Choices = t.Choices.Select( c => new SceneChoice
			{
				Label = Resolve( c.Label ),
				FlagsOnPick = new List<string>( c.FlagsOnPick ),
				LoyaltyDelta = c.LoyaltyDelta,
				StressDelta = c.StressDelta,
				ConscienceDelta = c.ConscienceDelta,
				HeatDelta = c.HeatDelta,
				MoraleDelta = c.MoraleDelta,
			} ).ToList(),
			OperativeId = primaryOp,
			ResolvedCast = cast,
			FlagsOnFire = new List<string>( t.FlagsOnFire ),
			Tone = tone,
		};
	}
}
