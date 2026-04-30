using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Generation.Templates;

namespace CWC.Missions;

/// <summary>
/// Builds concrete Missions from MissionTemplates. Difficulty scales with
/// cycle (cycle 1 ~baseline; +2 per cycle, capped at +30). Templates loaded
/// once via TemplateLoader; in-memory fallbacks ensure tests run dry.
/// </summary>
public sealed class MissionGenerator
{
	private readonly List<MissionTemplate> _templates;

	public MissionGenerator( TemplateLoader loader )
		: this( loader.Deserialize<List<MissionTemplate>>( "missions.json" ) ?? FallbackTemplates() )
	{ }

	public MissionGenerator( List<MissionTemplate> templates )
	{
		_templates = templates.Count > 0 ? templates : FallbackTemplates();
	}

	public IReadOnlyList<MissionTemplate> Templates => _templates;

	public MissionTemplate? FindTemplate( string id )
		=> _templates.FirstOrDefault( t => t.Id == id );

	public Mission Generate( MissionTemplate t, WorldState world, Rng rng )
	{
		int cycle = world.Corporate.Cycle;
		int diffBase = rng.BellInt( t.MinDifficulty, t.MaxDifficulty );
		int cycleBoost = Math.Min( 30, (cycle - 1) * 2 );
		int difficulty = Math.Clamp( diffBase + cycleBoost, 5, 95 );

		var weights = new Dictionary<SkillKind, int>();
		foreach ( var (k, v) in t.StatWeights )
			if ( Enum.TryParse<SkillKind>( k, true, out var sk ) ) weights[sk] = v;

		string client = t.ClientCandidates.Count > 0 ? rng.Pick( t.ClientCandidates ) : "";
		string target = t.TargetCandidates.Count > 0 ? rng.Pick( t.TargetCandidates ) : "";

		var mission = new Mission
		{
			Id = $"m_{cycle}_{t.Id}_{rng.Next( 1000, 9999 )}",
			TemplateId = t.Id,
			Type = ParseType( t.Type ),
			Status = MissionStatus.Available,
			Title = InterpolateTokens( t.TitleTemplate, world, client, target ),
			Briefing = InterpolateTokens( t.BriefingTemplate, world, client, target ),
			ClientFactionId = client,
			TargetFactionId = target,
			Difficulty = difficulty,
			MoralWeight = t.MoralWeight,
			IsWetWork = t.IsWetWork,
			StatWeights = weights,
			CycleAvailable = cycle,
			CycleDeadline = cycle + Math.Max( 1, t.CycleWindow ),
			NarrativeFlagsOnSuccess = new List<string>( t.NarrativeFlagsOnSuccess ),
			NarrativeFlagsOnPartialSuccess = new List<string>( t.NarrativeFlagsOnPartialSuccess ),
			NarrativeFlagsOnFailure = new List<string>( t.NarrativeFlagsOnFailure ),
			NarrativeFlagsOnCatastrophe = new List<string>( t.NarrativeFlagsOnCatastrophe ),
		};

		return mission;
	}

	private static MissionType ParseType( string s )
		=> Enum.TryParse<MissionType>( s, true, out var t ) ? t : MissionType.Surveillance;

	private static string InterpolateTokens( string template, WorldState world, string clientId, string targetId )
	{
		if ( string.IsNullOrEmpty( template ) ) return template ?? "";
		string s = template;
		s = s.Replace( "{corp}", world.Setting.CorpName );
		s = s.Replace( "{location}", world.Setting.Location );
		s = s.Replace( "{client}", world.GetFaction( clientId )?.Name ?? clientId );
		s = s.Replace( "{target}", world.GetFaction( targetId )?.Name ?? targetId );
		s = s.Replace( "{year}", world.Setting.Year.ToString() );
		return s;
	}

	private static List<MissionTemplate> FallbackTemplates() => new()
	{
		new MissionTemplate
		{
			Id = "extraction_defector", Type = "Extraction",
			TitleTemplate = "Extract: defector from {target}",
			BriefingTemplate = "{target} has a body to bury. Pull them out clean.",
			MinDifficulty = 35, MaxDifficulty = 60,
			MoralWeight = 10, IsWetWork = false,
			StatWeights = { { "Stealth", 50 }, { "Combat", 30 }, { "Persuasion", 20 } },
			TargetCandidates = { "rival_kasumi", "rival_aether" },
			ClientCandidates = { "host" },
		},
		new MissionTemplate
		{
			Id = "surveillance_auditor", Type = "Surveillance",
			TitleTemplate = "Surveil: auditor inbound to {location}",
			BriefingTemplate = "Razor auditor making rounds. Document the route, learn the schedule.",
			MinDifficulty = 30, MaxDifficulty = 55,
			MoralWeight = 5,
			StatWeights = { { "Stealth", 50 }, { "Hacking", 30 }, { "Deception", 20 } },
			TargetCandidates = { "razor" },
			ClientCandidates = { "host" },
		},
		new MissionTemplate
		{
			Id = "counterintel_internal", Type = "CounterIntel",
			TitleTemplate = "Counter-intel: silence the deputy",
			BriefingTemplate = "An internal voice is getting too loud. Shut it down without leaving prints.",
			MinDifficulty = 45, MaxDifficulty = 70,
			MoralWeight = 30, IsWetWork = false,
			StatWeights = { { "Deception", 40 }, { "Hacking", 30 }, { "Persuasion", 30 } },
			ClientCandidates = { "div_ops" }, TargetCandidates = { "div_finance" },
		},
	};
}
