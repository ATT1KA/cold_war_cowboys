using System;
using System.Collections.Generic;

namespace ColdWarCowboys.Missions;

/// <summary>Coarse mission outcome bucket consumed by every downstream system.</summary>
public enum MissionOutcome
{
	Pending,
	Success,
	PartialSuccess,
	Failure,
	Catastrophe,
}

/// <summary>Sprint 6 only needs faction-affecting tags and outcome.</summary>
public sealed class Mission
{
	public string Id { get; init; } = Guid.NewGuid().ToString( "N" );
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";

	/// <summary>Faction id that issued the contract (null = direct board op).</summary>
	public string? IssuingFactionId { get; set; }

	/// <summary>Faction ids whose interests this mission opposes.</summary>
	public List<string> OpposesFactionIds { get; } = new();

	/// <summary>Faction ids whose interests this mission advances jointly.</summary>
	public List<string> AlliedFactionIds { get; } = new();

	public int Reward { get; set; }
	public int Risk { get; set; }
	public int Exposure { get; set; }
	public bool IsBoardDirective { get; set; }
	public bool IsMandatory { get; set; }

	public MissionOutcome Outcome { get; set; } = MissionOutcome.Pending;
	public List<string> AssignedOperativeIds { get; } = new();
	public List<string> Tags { get; } = new();
}
