// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Corporate;

public enum CorporateEventKind
{
	BoardReshuffle,
	FactionMerger,
	FactionSplit,
	BudgetCut,
	BudgetWindfall,
	Whistleblower,
	CorporateEspionage,
	MarketShift,
}

public sealed class CorporateEventTemplate
{
	public string Id { get; init; } = "";
	public CorporateEventKind Kind { get; init; }
	public string NarrativeTemplate { get; init; } = "";
	public double BaseWeight { get; init; } = 1.0;
	public List<string> Conditions { get; init; } = new();
}

public readonly record struct CorporateEventFired( CorporateEventKind Kind, string Narrative );

/// <summary>
/// Sprint 6 random-events layer. One event (or none) per cycle drawn from a
/// weighted pool. Conditions gate templates; mechanical fallout queues
/// through CorporateConsequenceProcessor. Templates ship in JSON so designers
/// iterate without a recompile.
/// </summary>
public sealed class CorporateEventGenerator
{
	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly CorporateConsequenceProcessor _consequences;
	private readonly List<CorporateEventTemplate> _templates = new();

	public double FireProbability { get; set; } = 0.6;

	public CorporateEventGenerator( Rng rng, EventBus bus, CorporateConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
		SeedDefaultTemplates();
	}

	public void Register( CorporateEventTemplate t ) => _templates.Add( t );

	public void LoadTemplates( IEnumerable<CorporateEventTemplate> templates )
	{
		_templates.Clear();
		_templates.AddRange( templates );
	}

	public void Roll( CorporateState corp, WorldState world )
	{
		if ( !_rng.Chance( FireProbability ) ) return;

		var eligible = new List<(CorporateEventTemplate t, double w)>();
		foreach ( var t in _templates )
		{
			if ( !MeetsConditions( t, corp, world ) ) continue;
			eligible.Add( (t, t.BaseWeight) );
		}
		if ( eligible.Count == 0 ) return;

		var picked = _rng.WeightedPick( eligible );
		Fire( picked, corp, world );
	}

	private void Fire( CorporateEventTemplate t, CorporateState corp, WorldState world )
	{
		string narrative = ExpandNarrative( t, corp, world, out var bindings );
		switch ( t.Kind )
		{
			case CorporateEventKind.BoardReshuffle:
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c =>
					{
						c.DirectorName = bindings.TryGetValue( "new_director", out var n ) ? n : "Director Mercer";
						c.DirectorAgenda = _rng.Pick( new[] { "Personal advancement", "Risk reduction", "Empire building", "Quiet sabotage" } );
						c.BoardConfidence = Math.Clamp( c.BoardConfidence - 5, 0, 100 );
					},
				} );
				break;

			case CorporateEventKind.FactionMerger:
			{
				var pair = PickTwoFactions( corp );
				if ( pair == null ) return;
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c =>
					{
						var (a, b) = pair.Value;
						b.Cash += a.Cash;
						b.Standing = Math.Min( 100, (a.Standing + b.Standing) / 2 + 5 );
						b.RelationshipToPlayer = (a.RelationshipToPlayer + b.RelationshipToPlayer) / 2;
						c.Factions.Remove( a.Id );
					},
				} );
				break;
			}

			case CorporateEventKind.FactionSplit:
			{
				var src = PickFaction( corp );
				if ( src == null ) return;
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c =>
					{
						var splinter = new Faction
						{
							Id = src.Id + "_splinter",
							Name = src.Name + " Splinter",
							Kind = src.Kind,
							Leader = "Unknown",
							Agenda = FactionAgenda.Predatory,
							Standing = Math.Max( 10, src.Standing / 2 ),
							Cash = src.Cash / 3,
							RelationshipToPlayer = -10,
						};
						src.Cash -= splinter.Cash;
						src.Standing = Math.Max( 5, src.Standing - 10 );
						c.Factions[splinter.Id] = splinter;
					},
				} );
				break;
			}

			case CorporateEventKind.BudgetCut:
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c => c.Budget = Math.Max( 0, c.Budget - 30_000 ),
				} );
				break;

			case CorporateEventKind.BudgetWindfall:
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c => c.Budget += 40_000,
				} );
				break;

			case CorporateEventKind.Whistleblower:
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c =>
					{
						c.Suspicion = Math.Min( 100, c.Suspicion + 12 );
						c.ExternalReputation = Math.Max( 0, c.ExternalReputation - 6 );
						c.BoardConfidence = Math.Max( 0, c.BoardConfidence - 4 );
					},
				} );
				break;

			case CorporateEventKind.CorporateEspionage:
			{
				var thief = PickFaction( corp );
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative,
					Apply = c =>
					{
						if ( thief != null ) thief.Cash += 8_000;
						c.PoliticalCapital = Math.Max( 0, c.PoliticalCapital - 1 );
					},
				} );
				break;
			}

			case CorporateEventKind.MarketShift:
			{
				int shift = _rng.Next( -20_000, 20_001 );
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Event",
					Description = narrative + (shift >= 0 ? $" (+{shift:N0})" : $" ({shift:N0})"),
					Apply = c =>
					{
						c.Budget = Math.Max( 0, c.Budget + shift );
						foreach ( var f in c.Factions.Values )
							f.Cash = Math.Max( 0, f.Cash + shift / 4 );
					},
				} );
				break;
			}
		}

		_bus.Publish( new CorporateEventFired( t.Kind, narrative ) );
	}

	private static bool MeetsConditions( CorporateEventTemplate t, CorporateState corp, WorldState world )
	{
		foreach ( var cond in t.Conditions )
		{
			var parts = cond.Split( ':', 2 );
			if ( parts.Length != 2 ) continue;
			if ( !int.TryParse( parts[1], out var v ) ) continue;
			switch ( parts[0] )
			{
				case "min_day":              if ( world.Day < v ) return false; break;
				case "min_cycle":            if ( corp.Cycle < v ) return false; break;
				case "min_suspicion":        if ( corp.Suspicion < v ) return false; break;
				case "max_board_confidence": if ( corp.BoardConfidence > v ) return false; break;
				case "min_factions":         if ( corp.Factions.Count < v ) return false; break;
			}
		}
		return true;
	}

	private string ExpandNarrative( CorporateEventTemplate t, CorporateState corp, WorldState world, out Dictionary<string, string> bindings )
	{
		bindings = new Dictionary<string, string>
		{
			["day"] = world.Day.ToString(),
			["cycle"] = corp.Cycle.ToString(),
			["director"] = corp.DirectorName,
			["new_director"] = "Director " + _rng.Pick( new[] { "Mercer", "Halliday", "Drozd", "Ng", "Vasquez" } ),
		};
		var s = t.NarrativeTemplate;
		foreach ( var kv in bindings ) s = s.Replace( "{" + kv.Key + "}", kv.Value );
		return s;
	}

	private Faction? PickFaction( CorporateState corp )
	{
		if ( corp.Factions.Count == 0 ) return null;
		var list = new List<Faction>( corp.Factions.Values );
		return _rng.Pick( list );
	}

	private (Faction a, Faction b)? PickTwoFactions( CorporateState corp )
	{
		if ( corp.Factions.Count < 2 ) return null;
		var list = new List<Faction>( corp.Factions.Values );
		var a = _rng.Pick( list );
		Faction b;
		int safety = 0;
		do { b = _rng.Pick( list ); if ( ++safety > 32 ) return null; }
		while ( b.Id == a.Id );
		return (a, b);
	}

	private void SeedDefaultTemplates()
	{
		Register( new CorporateEventTemplate
		{
			Id = "default_market_shift",
			Kind = CorporateEventKind.MarketShift,
			NarrativeTemplate = "Cycle {cycle}: market repositioning ripples through every division.",
			BaseWeight = 1.0,
		} );
	}
}
