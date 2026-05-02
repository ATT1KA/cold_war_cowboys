using System;
using System.Collections.Generic;
using CWC.Domain;

namespace CWC.Core;

/// <summary>The player's rank in the megacorp hierarchy. Sprint 6.</summary>
public enum CorporateRank
{
	Probationary,
	Operative,
	Handler,
	Director,
	BoardLiaison,
}

/// <summary>A single binding (or soft) instruction issued by the board. Sprint 6.</summary>
public sealed class BoardDirective
{
	public string Id { get; init; } = Guid.NewGuid().ToString( "N" );
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public bool Mandatory { get; set; }
	public int IgnoreConfidencePenalty { get; set; } = 5;
	public int ComplyConfidenceReward { get; set; } = 5;
	public int DeadlineDay { get; set; }
	public bool Resolved { get; set; }
}

/// <summary>
/// World-level pressure dials. Mutated by ConsequenceProcessor (Sprint 3) and
/// CorporateConsequenceProcessor (Sprint 6).
/// Heat = Razor / external scrutiny. PoliticalPressure = boardroom / faction politics.
/// Sprint 6 layers BoardConfidence, dual reputation, PoliticalCapital, Rank,
/// and the active board directives.
/// </summary>
public sealed class CorporateState
{
	public int Heat { get; set; } = 0;            // 0..100 — Razor pressure
	public int Suspicion { get; set; } = 0;       // 0..100 — internal scrutiny
	public int PoliticalPressure { get; set; } = 0;
	public int Reputation { get; set; } = 50;
	public int Budget { get; set; } = 100_000;
	public int Cycle { get; set; } = 1;

	// ---- Sprint 6 boardroom layer ----
	public CorporateRank Rank { get; set; } = CorporateRank.Operative;
	public int BoardConfidence { get; set; } = 50;
	public int InternalReputation { get; set; } = 50;
	public int ExternalReputation { get; set; } = 50;
	public int PoliticalCapital { get; set; } = 5;

	public string DirectorName { get; set; } = "Director Vale";
	public string DirectorAgenda { get; set; } = "Personal advancement";

	/// <summary>Field roster surfaced to Sprint 6 systems (poaching, loyalty audits).</summary>
	public List<Operative> Roster { get; } = new();

	/// <summary>Faction lookup by id. Sprint 6 maintains this in parallel with WorldState.Factions.</summary>
	public Dictionary<string, Faction> Factions { get; } = new();

	/// <summary>Contracts surfaced this cycle but not yet accepted.</summary>
	public List<Mission> AvailableContracts { get; } = new();

	/// <summary>Directives the board has issued and is tracking.</summary>
	public List<BoardDirective> ActiveDirectives { get; } = new();

	/// <summary>Recent corporate-event log entries; oldest-first.</summary>
	public List<string> RecentEventLog { get; } = new();

	public void Clamp()
	{
		Heat = Clamp01( Heat );
		Suspicion = Clamp01( Suspicion );
		Reputation = Clamp01( Reputation );
		BoardConfidence = Clamp01( BoardConfidence );
		InternalReputation = Clamp01( InternalReputation );
		ExternalReputation = Clamp01( ExternalReputation );
		if ( PoliticalCapital < 0 ) PoliticalCapital = 0;
		if ( Budget < 0 ) Budget = 0;
		foreach ( var f in Factions.Values ) f.Clamp();
	}

	private static int Clamp01( int v ) => v < 0 ? 0 : v > 100 ? 100 : v;

	public CorporateState Clone()
	{
		var clone = new CorporateState
		{
			Heat = Heat,
			Suspicion = Suspicion,
			PoliticalPressure = PoliticalPressure,
			Reputation = Reputation,
			Budget = Budget,
			Cycle = Cycle,
			Rank = Rank,
			BoardConfidence = BoardConfidence,
			InternalReputation = InternalReputation,
			ExternalReputation = ExternalReputation,
			PoliticalCapital = PoliticalCapital,
			DirectorName = DirectorName,
			DirectorAgenda = DirectorAgenda,
		};

		foreach ( var op in Roster ) clone.Roster.Add( op );
		foreach ( var kv in Factions ) clone.Factions[kv.Key] = kv.Value;
		foreach ( var c in AvailableContracts ) clone.AvailableContracts.Add( c );
		foreach ( var d in ActiveDirectives ) clone.ActiveDirectives.Add( d );
		foreach ( var e in RecentEventLog ) clone.RecentEventLog.Add( e );

		return clone;
	}
}
