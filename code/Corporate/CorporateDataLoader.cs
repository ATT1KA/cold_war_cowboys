using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;
using CWC.Generation.Templates;

namespace CWC.Corporate;

/// <summary>
/// Loads the four Sprint 6 corporate template sets via the shared TemplateLoader
/// (which handles disk-walk + JSON-comment tolerance). Templates land in
/// Data/Templates/corporate/*.json so the rest of the project's templates can
/// stay flat.
/// </summary>
public static class CorporateDataLoader
{
	private sealed class CorporateFactionTemplate
	{
		public string Id { get; set; } = "";
		public string Name { get; set; } = "";
		public string Kind { get; set; } = "RivalCorp";
		public string Leader { get; set; } = "";
		public string Agenda { get; set; } = "Cooperative";
		public int Standing { get; set; } = 50;
		public int Cash { get; set; } = 50_000;
		public int RelationshipToPlayer { get; set; } = 0;
		public List<string> Personality { get; set; } = new();
	}

	private sealed class DirectiveTemplate
	{
		public string Id { get; set; } = "";
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public bool Mandatory { get; set; }
		public int IgnoreConfidencePenalty { get; set; } = 5;
		public int ComplyConfidenceReward { get; set; } = 5;
		public int DeadlineDayOffset { get; set; } = 7;
	}

	private sealed class EventTemplateDto
	{
		public string Id { get; set; } = "";
		public string Kind { get; set; } = "MarketShift";
		public string NarrativeTemplate { get; set; } = "";
		public double BaseWeight { get; set; } = 1.0;
		public List<string> Conditions { get; set; } = new();
	}

	/// <summary>
	/// Loads factions.json and MERGES the corporate-layer fields into the
	/// world's faction objects. Corp and world views must reference the SAME
	/// instances — the old behavior replaced shared references with divergent
	/// duplicates, which made faction AI mutations invisible to the UI and
	/// narrative triggers.
	/// </summary>
	public static int LoadFactions( WorldState world, TemplateLoader loader )
	{
		var corp = world.Corporate;
		var list = loader.Deserialize<List<CorporateFactionTemplate>>( "corporate/factions.json" )
			?? new List<CorporateFactionTemplate>();
		int count = 0;
		foreach ( var t in list )
		{
			if ( !Enum.TryParse<FactionAgenda>( t.Agenda, true, out var agenda ) )
				agenda = FactionAgenda.Cooperative;
			if ( !Enum.TryParse<FactionKind>( t.Kind, true, out var kind ) )
				kind = FactionKind.RivalCorp;

			var existing = world.Factions.Find( f => f.Id == t.Id );
			if ( existing != null )
			{
				// Layer corporate-AI fields onto the world-generated faction.
				existing.Agenda = agenda;
				existing.Leader = t.Leader;
				existing.RelationshipToPlayer = t.RelationshipToPlayer;
				if ( t.Cash > 0 ) existing.Cash = t.Cash;
				existing.Personality.Clear();
				existing.Personality.AddRange( t.Personality );
				existing.Clamp();
				corp.Factions[existing.Id] = existing;
			}
			else
			{
				var f = new Faction
				{
					Id = t.Id,
					Name = t.Name,
					Kind = kind,
					Leader = t.Leader,
					Agenda = agenda,
					Standing = t.Standing,
					Cash = t.Cash,
					RelationshipToPlayer = t.RelationshipToPlayer,
				};
				f.Personality.AddRange( t.Personality );
				f.Clamp();
				world.Factions.Add( f );
				corp.Factions[f.Id] = f;
			}
			count++;
		}
		return count;
	}

	/// <summary>
	/// Loads directives.json into the board's pending pool. The board issues
	/// them over the course of the run (PoliticsSystem) instead of dumping the
	/// whole stack on cycle 1.
	/// </summary>
	public static int LoadDirectives( CorporateState corp, TemplateLoader loader )
	{
		var list = loader.Deserialize<List<DirectiveTemplate>>( "corporate/directives.json" )
			?? new List<DirectiveTemplate>();
		int count = 0;
		foreach ( var t in list )
		{
			corp.PendingDirectivePool.Add( new BoardDirective
			{
				Id = t.Id,
				Title = t.Title,
				Description = t.Description,
				Mandatory = t.Mandatory,
				IgnoreConfidencePenalty = t.IgnoreConfidencePenalty,
				ComplyConfidenceReward = t.ComplyConfidenceReward,
				DeadlineDayOffset = t.DeadlineDayOffset,
			} );
			count++;
		}
		return count;
	}

	/// <summary>Loads corporate-events.json into the CorporateEventGenerator pool.</summary>
	public static int LoadEvents( CorporateEventGenerator gen, TemplateLoader loader )
	{
		var list = loader.Deserialize<List<EventTemplateDto>>( "corporate/events.json" )
			?? new List<EventTemplateDto>();
		var templates = new List<CorporateEventTemplate>();
		foreach ( var t in list )
		{
			if ( !Enum.TryParse<CorporateEventKind>( t.Kind, true, out var kind ) ) continue;
			var tmpl = new CorporateEventTemplate
			{
				Id = t.Id,
				Kind = kind,
				NarrativeTemplate = t.NarrativeTemplate,
				BaseWeight = t.BaseWeight,
				Conditions = new List<string>( t.Conditions ),
			};
			templates.Add( tmpl );
		}
		if ( templates.Count > 0 ) gen.LoadTemplates( templates );
		return templates.Count;
	}
}
