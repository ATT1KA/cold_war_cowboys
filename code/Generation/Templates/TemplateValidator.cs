using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Missions;
using CWC.Narrative;

namespace CWC.Generation.Templates;

/// <summary>
/// Startup content validation. Runs once at NewGame after templates load and
/// returns human-readable problems: duplicate ids, unknown trigger types,
/// unknown cast resolvers, unknown skill names, and flag cross-reference gaps.
/// The point is to make authoring mistakes loud at boot instead of silently
/// producing a game with missing content.
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

	private static readonly HashSet<string> KnownResolvers = new()
	{
		"triggering_operative", "highest_conscience", "lowest_conscience",
		"highest_social", "lowest_loyalty", "highest_stress",
		"highest_morale", "lowest_morale", "first_mission_op", "last_catastrophe_op",
	};

	private static readonly HashSet<string> KnownStats = new()
	{
		"conscience", "loyalty", "stress", "morale", "ambition",
	};

	public static List<string> ValidateScenes( IReadOnlyList<SceneTemplate> scenes )
	{
		var problems = new List<string>();
		if ( scenes.Count == 0 )
		{
			problems.Add( "scenes.json: zero scenes loaded" );
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
				if ( !KnownTriggerTypes.Contains( t.Type ) )
					problems.Add( $"scene '{s.Id}': unknown trigger type '{t.Type}'" );

			foreach ( var t in s.Triggers )
				if ( t.Type is "avg_stat_below" or "any_stat_below" && !KnownStats.Contains( t.Key ) )
					problems.Add( $"scene '{s.Id}': unknown stat '{t.Key}' in trigger" );

			foreach ( var slot in s.Cast )
			{
				if ( slot.Kind == CastSlotKind.Role
					&& !KnownResolvers.Contains( slot.Resolver )
					&& !slot.Resolver.StartsWith( "role:" ) )
					problems.Add( $"scene '{s.Id}': unknown cast resolver '{slot.Resolver}'" );
			}

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

	public static List<string> ValidateArchetypes( IReadOnlyList<ArchetypeTemplate> archetypes )
	{
		var problems = new List<string>();
		if ( archetypes.Count == 0 )
		{
			problems.Add( "archetypes: zero templates loaded" );
			return problems;
		}
		foreach ( var a in archetypes )
		{
			foreach ( var kv in a.SkillBands )
				if ( kv.Value.Max <= 0 )
					problems.Add( $"archetype '{a.Id}': skill band '{kv.Key}' is zero — deserialization or data bug" );
			if ( a.Conscience.Max <= 0 || a.Loyalty.Max <= 0 )
				problems.Add( $"archetype '{a.Id}': psychology bands are zero — deserialization or data bug" );
		}
		return problems;
	}

	public static List<string> ValidateMissions( IReadOnlyList<MissionTemplate> templates )
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

			if ( t.NarrativeSequence != null )
			{
				if ( t.NarrativeSequence.Nodes.Count == 0 )
					problems.Add( $"mission '{t.Id}': narrative sequence has zero nodes" );
				foreach ( var node in t.NarrativeSequence.Nodes )
				{
					if ( node.Choices.Count == 0 )
						problems.Add( $"mission '{t.Id}': narrative node with no choices" );
					foreach ( var c in node.Choices )
						foreach ( var k in c.SkillWeightOverride.Keys )
							if ( !Enum.TryParse<CWC.Domain.SkillKind>( k, true, out _ ) )
								problems.Add( $"mission '{t.Id}': unknown skill '{k}' in choice override" );
				}
			}
		}

		return problems;
	}
}
