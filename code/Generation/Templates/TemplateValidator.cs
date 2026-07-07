// [FRAMEWORK-PATTERN] The shape is reusable; the vocabulary is CWC's. Port by rename. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CWC.Missions;
using CWC.Narrative;

namespace CWC.Generation.Templates;

/// <summary>
/// Startup content validation. Runs once at NewGame after templates load and
/// returns human-readable problems: duplicate ids, unknown trigger types,
/// unknown cast resolvers, unknown skill names, dangling trait/faction
/// references, malformed tokens, and flag cross-reference gaps.
/// The point is to make authoring mistakes loud at boot instead of silently
/// producing a game with missing content — a cast slot that can't resolve
/// renders as the word "someone", and nobody wants to debug that from prose.
/// (Typo'd FIELD names are caught one layer down, by TemplateLoader's strict
/// re-parse.)
/// </summary>
public static class TemplateValidator
{
	private static readonly HashSet<string> KnownTriggerTypes = new()
	{
		"flag", "flag_prefix", "any_op",
		"avg_stat_below", "any_stat_below",
		"any_relationship_below", "any_relationship_above",
		"no_active_missions",
		"consecutive_successes", "board_confidence_below",
		"any_faction_relationship_below", "cycle_reached",
		"active_operatives_below", "last_mission_catastrophe", "stress_below",
	};

	/// <summary>Trigger types whose Key must name a flag (or flag prefix).</summary>
	private static readonly HashSet<string> KeyedTriggerTypes = new()
	{
		"flag", "flag_prefix", "any_op",
	};

	/// <summary>Trigger types where a missing/zero Threshold is always an authoring mistake.</summary>
	private static readonly HashSet<string> ThresholdTriggerTypes = new()
	{
		"avg_stat_below", "any_stat_below", "consecutive_successes",
		"board_confidence_below", "cycle_reached", "active_operatives_below",
		"stress_below",
	};

	/// <summary>Trigger types that identify a specific operative, enabling triggering_operative casts.</summary>
	private static readonly HashSet<string> OperativeProducingTriggers = new()
	{
		"any_op", "any_stat_below", "any_relationship_below", "any_relationship_above",
	};

	private static readonly HashSet<string> KnownResolvers = new()
	{
		"triggering_operative", "highest_conscience", "lowest_conscience",
		"highest_social", "lowest_loyalty", "highest_stress",
		"highest_morale", "lowest_morale", "first_mission_op", "last_catastrophe_op",
	};

	/// <summary>Narrative role tags — the vocabulary of archetype seeding and role drift.</summary>
	private static readonly HashSet<string> KnownNarrativeRoles = new()
	{
		"conscience", "mirror", "weapon", "innocent",
		"survivor", "climber", "anchor", "wildcard",
	};

	/// <summary>The valid narrative-role vocabulary, for tooling (ScenePreview, harnesses).</summary>
	public static IReadOnlyCollection<string> NarrativeRoles => KnownNarrativeRoles;

	private static readonly HashSet<string> KnownStats = new()
	{
		"conscience", "loyalty", "stress", "morale", "ambition",
	};

	private static readonly HashSet<string> KnownApproaches = new()
	{
		"stealth", "social", "force", "cyber", "compromise", "abort",
	};

	// Every {token} the NarrativeDirector can resolve. Anything else in braces
	// reaches the player as raw text.
	private static readonly Regex TokenPattern = new( @"\{([^{}]*)\}", RegexOptions.Compiled );
	private static readonly Regex GenderTokenShape = new( @"^gender:[mfn]\|[^|]*\|[^}]*$", RegexOptions.Compiled );

	public static List<string> ValidateScenes( IReadOnlyList<SceneTemplate> scenes )
	{
		var problems = new List<string>();
		if ( scenes.Count == 0 )
		{
			problems.Add( "scenes: zero scenes loaded" );
			return problems;
		}

		var ids = new HashSet<string>();
		var flagsSet = new HashSet<string>();
		foreach ( var s in scenes )
		{
			foreach ( var f in s.FlagsOnFire ) flagsSet.Add( f );
			foreach ( var c in s.Choices )
				foreach ( var f in c.FlagsOnPick ) flagsSet.Add( f );
		}

		foreach ( var s in scenes )
		{
			if ( string.IsNullOrEmpty( s.Id ) )
				problems.Add( "scene with empty Id" );
			else if ( !ids.Add( s.Id ) )
				problems.Add( $"duplicate scene id '{s.Id}'" );

			if ( s.TextLines.Count == 0 )
				problems.Add( $"scene '{s.Id}': no text lines" );

			foreach ( var t in s.Triggers )
			{
				if ( !KnownTriggerTypes.Contains( t.Type ) )
				{
					problems.Add( $"scene '{s.Id}': unknown trigger type '{t.Type}'" );
					continue;
				}
				if ( t.Type is "avg_stat_below" or "any_stat_below" && !KnownStats.Contains( t.Key ) )
					problems.Add( $"scene '{s.Id}': unknown stat '{t.Key}' in trigger" );
				if ( KeyedTriggerTypes.Contains( t.Type ) && string.IsNullOrEmpty( t.Key ) )
					problems.Add( $"scene '{s.Id}': trigger '{t.Type}' has no Key — can never match" );
				if ( ThresholdTriggerTypes.Contains( t.Type ) && t.Threshold <= 0 )
					problems.Add( $"scene '{s.Id}': trigger '{t.Type}' has no Threshold (or 0) — always false or missing field" );
			}

			// ---- Cast slots ----
			var slotNames = new HashSet<string>();
			bool hasOperativeSource =
				s.RequiredFlags.Any( f => f.StartsWith( "any_op:" ) )
				|| s.Triggers.Any( t => OperativeProducingTriggers.Contains( t.Type ) );

			foreach ( var slot in s.Cast )
			{
				if ( string.IsNullOrEmpty( slot.Name ) )
					problems.Add( $"scene '{s.Id}': cast slot with empty Name" );
				else if ( !slotNames.Add( slot.Name ) )
					problems.Add( $"scene '{s.Id}': duplicate cast slot '{slot.Name}'" );

				if ( slot.Kind != CastSlotKind.Role ) continue;

				if ( slot.Resolver.StartsWith( "role:" ) )
				{
					var role = slot.Resolver.Substring( "role:".Length );
					if ( !KnownNarrativeRoles.Contains( role ) )
						problems.Add( $"scene '{s.Id}': cast slot '{slot.Name}' targets unknown narrative role '{role}'" );
				}
				else if ( !KnownResolvers.Contains( slot.Resolver ) )
				{
					problems.Add( $"scene '{s.Id}': unknown cast resolver '{slot.Resolver}'" );
				}
				else if ( slot.Resolver == "triggering_operative" && !hasOperativeSource )
				{
					problems.Add( $"scene '{s.Id}': cast slot '{slot.Name}' wants triggering_operative but no trigger identifies one (needs any_op:/any_stat_below/any_relationship_*) — renders as \"the operative\"" );
				}
			}

			// ---- Tokens ----
			foreach ( var (text, where) in EnumerateSceneText( s ) )
			{
				foreach ( Match m in TokenPattern.Matches( text ) )
				{
					var body = m.Groups[1].Value;
					switch ( body )
					{
						case "op": case "corp": case "location": case "year":
						case "faction.name": case "operative.name":
							continue;
					}
					if ( body.StartsWith( "operative.role:" ) )
					{
						var role = body.Substring( "operative.role:".Length );
						// Resolvable if a cast slot has that name, or any operative
						// holds the narrative role at render time.
						if ( !slotNames.Contains( role ) && !KnownNarrativeRoles.Contains( role ) )
							problems.Add( $"scene '{s.Id}': {where} references '{{operative.role:{role}}}' but no cast slot or narrative role matches — renders as \"someone\"" );
						continue;
					}
					if ( body.StartsWith( "gender:" ) )
					{
						if ( !GenderTokenShape.IsMatch( body ) )
							problems.Add( $"scene '{s.Id}': {where} has malformed gender token '{{{body}}}' (expected {{gender:m|his|her}})" );
						continue;
					}
					problems.Add( $"scene '{s.Id}': {where} contains unknown token '{{{body}}}' — will reach the player as raw text" );
				}
			}

			foreach ( var c in s.Choices )
				if ( string.IsNullOrWhiteSpace( c.Label ) )
					problems.Add( $"scene '{s.Id}': choice with empty Label" );

			// Flag cross-reference: a scene gated on a "scene:*"/"choice:*" flag
			// that no scene ever sets can never fire.
			foreach ( var req in s.RequiredFlags )
			{
				string raw = req.StartsWith( "flag:" ) ? req.Substring( 5 ) : req;
				if ( ( raw.StartsWith( "scene:" ) || raw.StartsWith( "choice:" ) ) && !flagsSet.Contains( raw ) )
					problems.Add( $"scene '{s.Id}': required flag '{raw}' is never set by any scene" );
			}
		}

		return problems;
	}

	private static IEnumerable<(string text, string where)> EnumerateSceneText( SceneTemplate s )
	{
		yield return (s.Title, "Title");
		yield return (s.Speaker, "Speaker");
		yield return (s.Setting, "Setting");
		for ( int i = 0; i < s.TextLines.Count; i++ )
			yield return (s.TextLines[i], $"TextLines[{i}]");
		for ( int i = 0; i < s.Choices.Count; i++ )
			yield return (s.Choices[i].Label, $"Choices[{i}].Label");
	}

	public static List<string> ValidateArchetypes(
		IReadOnlyList<ArchetypeTemplate> archetypes,
		IReadOnlyList<TraitTemplate> traits )
	{
		var problems = new List<string>();
		if ( archetypes.Count == 0 )
		{
			problems.Add( "archetypes: zero templates loaded" );
			return problems;
		}

		var traitIds = new HashSet<string>( traits.Select( t => t.Id ) );
		foreach ( var a in archetypes )
		{
			foreach ( var kv in a.SkillBands )
				if ( kv.Value.Max <= 0 )
					problems.Add( $"archetype '{a.Id}': skill band '{kv.Key}' is zero — deserialization or data bug" );
			if ( a.Conscience.Max <= 0 || a.Loyalty.Max <= 0 )
				problems.Add( $"archetype '{a.Id}': psychology bands are zero — deserialization or data bug" );

			if ( !string.IsNullOrEmpty( a.NarrativeRole ) && !KnownNarrativeRoles.Contains( a.NarrativeRole ) )
				problems.Add( $"archetype '{a.Id}': unknown narrative role '{a.NarrativeRole}'" );

			// A typo'd trait id is silently skipped by OperativeGenerator — the
			// archetype just quietly loses part of its personality.
			CheckTraitPool( problems, a.Id, "PersonalityPool", a.PersonalityPool, traitIds );
			CheckTraitPool( problems, a.Id, "BackgroundPool", a.BackgroundPool, traitIds );
			CheckTraitPool( problems, a.Id, "VicePool", a.VicePool, traitIds );
			CheckTraitPool( problems, a.Id, "CompulsionPool", a.CompulsionPool, traitIds );
			CheckTraitPool( problems, a.Id, "TraitPool", a.TraitPool, traitIds );
		}
		return problems;
	}

	private static void CheckTraitPool( List<string> problems, string archetypeId,
		string poolName, IEnumerable<string> pool, HashSet<string> traitIds )
	{
		foreach ( var id in pool )
			if ( !traitIds.Contains( id ) )
				problems.Add( $"archetype '{archetypeId}': {poolName} references unknown trait '{id}'" );
	}

	public static List<string> ValidateMissions(
		IReadOnlyList<MissionTemplate> templates,
		IReadOnlyCollection<string>? knownFactionIds = null )
	{
		var problems = new List<string>();
		if ( templates.Count == 0 )
		{
			problems.Add( "missions: zero templates loaded" );
			return problems;
		}

		var ids = new HashSet<string>();
		foreach ( var t in templates )
		{
			if ( string.IsNullOrEmpty( t.Id ) )
				problems.Add( "mission template with empty Id" );
			else if ( !ids.Add( t.Id ) )
				problems.Add( $"duplicate mission template id '{t.Id}'" );

			foreach ( var k in t.StatWeights.Keys )
				if ( !Enum.TryParse<CWC.Domain.SkillKind>( k, true, out _ ) )
					problems.Add( $"mission '{t.Id}': unknown skill '{k}' in StatWeights" );

			if ( knownFactionIds != null )
			{
				foreach ( var f in t.ClientCandidates )
					if ( !knownFactionIds.Contains( f ) )
						problems.Add( $"mission '{t.Id}': ClientCandidates references unknown faction '{f}'" );
				foreach ( var f in t.TargetCandidates )
					if ( !knownFactionIds.Contains( f ) )
						problems.Add( $"mission '{t.Id}': TargetCandidates references unknown faction '{f}'" );
			}

			if ( t.NarrativeSequence != null )
			{
				if ( t.NarrativeSequence.Nodes.Count == 0 )
					problems.Add( $"mission '{t.Id}': narrative sequence has zero nodes" );
				foreach ( var node in t.NarrativeSequence.Nodes )
				{
					if ( node.Choices.Count == 0 )
						problems.Add( $"mission '{t.Id}': narrative node with no choices" );
					CheckStatGate( problems, t.Id, "node", node.RequiresStat );
					foreach ( var c in node.Choices )
					{
						CheckStatGate( problems, t.Id, $"choice '{Truncate( c.Text )}'", c.RequiresStat );
						if ( !string.IsNullOrEmpty( c.Approach ) && !KnownApproaches.Contains( c.Approach ) )
							problems.Add( $"mission '{t.Id}': unknown approach '{c.Approach}' on choice '{Truncate( c.Text )}'" );
						foreach ( var k in c.SkillWeightOverride.Keys )
							if ( !Enum.TryParse<CWC.Domain.SkillKind>( k, true, out _ ) )
								problems.Add( $"mission '{t.Id}': unknown skill '{k}' in choice override" );
					}
				}
			}
		}

		return problems;
	}

	/// <summary>RequiresStat grammar: "Skill:threshold" with a real SkillKind.</summary>
	private static void CheckStatGate( List<string> problems, string missionId, string where, string? spec )
	{
		if ( string.IsNullOrEmpty( spec ) ) return;
		var parts = spec.Split( ':' );
		if ( parts.Length != 2 || !int.TryParse( parts[1], out _ ) )
		{
			problems.Add( $"mission '{missionId}': {where} has malformed RequiresStat '{spec}' (expected \"Skill:60\")" );
			return;
		}
		if ( !Enum.TryParse<CWC.Domain.SkillKind>( parts[0], true, out _ ) )
			problems.Add( $"mission '{missionId}': {where} RequiresStat names unknown skill '{parts[0]}'" );
	}

	private static string Truncate( string s )
		=> s.Length <= 30 ? s : s.Substring( 0, 27 ) + "...";
}
