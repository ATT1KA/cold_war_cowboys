using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ColdWarCowboys.Factions;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Corporate;

/// <summary>
/// Loads the four Sprint 6 JSON template sets from Assets/data/corporate.
/// Authoring is plain JSON so designers iterate without recompiling.
/// </summary>
public static class CorporateDataLoader
{
	private sealed class FactionTemplate
	{
		public string Id { get; set; } = "";
		public string Name { get; set; } = "";
		public string Leader { get; set; } = "";
		public string Agenda { get; set; } = "Cooperative";
		public int Standing { get; set; } = 50;
		public int Cash { get; set; } = 100;
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

	/// <summary>Loads factions.json and seeds CorporateState.Factions.</summary>
	public static void LoadFactions( CorporateState corp, string path )
	{
		var json = File.ReadAllText( path );
		var list = JsonSerializer.Deserialize<List<FactionTemplate>>( json ) ?? new();
		foreach ( var t in list )
		{
			if ( !Enum.TryParse<FactionAgenda>( t.Agenda, true, out var agenda ) )
				agenda = FactionAgenda.Cooperative;
			var f = new Faction
			{
				Id = t.Id,
				Name = t.Name,
				Leader = t.Leader,
				Agenda = agenda,
				Standing = t.Standing,
				Cash = t.Cash,
				RelationshipToPlayer = t.RelationshipToPlayer,
			};
			f.Personality.AddRange( t.Personality );
			f.Clamp();
			corp.Factions[f.Id] = f;
		}
	}

	/// <summary>Loads directives.json and queues them on the active list.</summary>
	public static void LoadDirectives( CorporateState corp, WorldState world, string path )
	{
		var json = File.ReadAllText( path );
		var list = JsonSerializer.Deserialize<List<DirectiveTemplate>>( json ) ?? new();
		foreach ( var t in list )
		{
			corp.ActiveDirectives.Add( new BoardDirective
			{
				Id = t.Id,
				Title = t.Title,
				Description = t.Description,
				Mandatory = t.Mandatory,
				IgnoreConfidencePenalty = t.IgnoreConfidencePenalty,
				ComplyConfidenceReward = t.ComplyConfidenceReward,
				DeadlineDay = world.Day + t.DeadlineDayOffset,
			} );
		}
	}

	/// <summary>Loads events.json into the CorporateEventGenerator template pool.</summary>
	public static void LoadEvents( CorporateEventGenerator gen, string path )
	{
		var json = File.ReadAllText( path );
		var list = JsonSerializer.Deserialize<List<EventTemplateDto>>( json ) ?? new();
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
			};
			tmpl.Conditions.AddRange( t.Conditions );
			templates.Add( tmpl );
		}
		gen.LoadTemplates( templates );
	}
}
