using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;
using CWC.Generation.Templates;

namespace CWC.Generation;

/// <summary>
/// Picks the opening scenario, applies its starting CorporateState overrides,
/// stamps NarrativeFlags, and emits the scenario's seed mission. Sprint 3's
/// MissionGenerator builds the mission body from the scenario's MissionTemplateId.
/// </summary>
public sealed class ScenarioGenerator
{
	private readonly List<ScenarioTemplate> _scenarios;

	public ScenarioGenerator( List<ScenarioTemplate> scenarios )
	{
		_scenarios = scenarios.Count > 0 ? scenarios : Fallback();
	}

	public Result Generate( WorldState world, Rng r )
	{
		var template = r.Pick( _scenarios );

		foreach ( var kv in template.StartingCorporate )
		{
			switch ( kv.Key )
			{
				case "Heat": world.Corporate.Heat = kv.Value; break;
				case "Suspicion": world.Corporate.Suspicion = kv.Value; break;
				case "PoliticalPressure": world.Corporate.PoliticalPressure = kv.Value; break;
				case "Reputation": world.Corporate.Reputation = kv.Value; break;
				case "Budget": world.Corporate.Budget = kv.Value; break;
			}
		}

		foreach ( var flag in template.NarrativeFlags )
			world.NarrativeFlags.Add( flag );

		return new Result
		{
			ScenarioId = template.Id,
			Title = template.Title,
			Briefing = template.Briefing,
			SeedMissionTemplateId = template.MissionTemplateId,
		};
	}

	private static List<ScenarioTemplate> Fallback() => new()
	{
		new ScenarioTemplate
		{
			Id = "the_recall", Title = "The Recall",
			Briefing = "A junior analyst defected with internal memos. Plug the leak.",
			MissionTemplateId = "extraction_defector",
			StartingCorporate = new() { { "Heat", 15 }, { "Suspicion", 20 }, { "Budget", 250000 } },
			NarrativeFlags = new() { "scenario:recall", "first_cycle" },
		},
	};

	public sealed class Result
	{
		public string ScenarioId { get; set; } = "";
		public string Title { get; set; } = "";
		public string Briefing { get; set; } = "";
		public string SeedMissionTemplateId { get; set; } = "";
	}
}
