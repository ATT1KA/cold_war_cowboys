using System;
using System.Collections.Generic;
using ColdWarCowboys.Factions;
using ColdWarCowboys.Missions;
using ColdWarCowboys.Operatives;

namespace ColdWarCowboys.World;

/// <summary>The player's rank in the megacorp hierarchy.</summary>
public enum CorporateRank
{
	Probationary,
	Operative,
	Handler,
	Director,
	BoardLiaison,
}

/// <summary>
/// Persistent state of the corporate layer: roster, faction roster, board
/// confidence, current rank, active directives, available contracts. This is
/// the single struct Sprint 6 systems mutate; everything else reads from it.
/// </summary>
public sealed class CorporateState
{
	public CorporateRank Rank { get; set; } = CorporateRank.Operative;
	public int Budget { get; set; } = 1000;

	/// <summary>Board confidence in the player division (0-100).</summary>
	public int BoardConfidence { get; set; } = 50;

	/// <summary>Suspicion meter — public/external pressure on the division.</summary>
	public int Suspicion { get; set; } = 0;

	/// <summary>Internal reputation within the corp (0-100).</summary>
	public int InternalReputation { get; set; } = 50;

	/// <summary>External reputation (rivals, media, government) (0-100).</summary>
	public int ExternalReputation { get; set; } = 50;

	/// <summary>The player's direct boss — has their own agenda.</summary>
	public string DirectorName { get; set; } = "Director Vale";
	public string DirectorAgenda { get; set; } = "Personal advancement";

	public List<Operative> Roster { get; } = new();
	public Dictionary<string, Faction> Factions { get; } = new();
	public List<Mission> AvailableContracts { get; } = new();
	public List<BoardDirective> ActiveDirectives { get; } = new();
	public List<string> RecentEventLog { get; } = new();

	/// <summary>Political capital — burnable to negotiate contract terms.</summary>
	public int PoliticalCapital { get; set; } = 5;

	public void Clamp()
	{
		BoardConfidence = Clamp01( BoardConfidence );
		Suspicion = Clamp01( Suspicion );
		InternalReputation = Clamp01( InternalReputation );
		ExternalReputation = Clamp01( ExternalReputation );
		if ( PoliticalCapital < 0 ) PoliticalCapital = 0;
		if ( Budget < 0 ) Budget = 0;
		foreach ( var f in Factions.Values ) f.Clamp();
	}

	private static int Clamp01( int v ) => v < 0 ? 0 : v > 100 ? 100 : v;
}

/// <summary>A single binding (or soft) instruction issued by the board.</summary>
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
