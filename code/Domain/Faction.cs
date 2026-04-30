using System.Collections.Generic;

namespace CWC.Domain;

public enum FactionKind
{
	HostCorp,         // the player's own
	RivalCorp,
	InternalDivision, // a board faction within the host
	Agency,           // government / regulator
	Syndicate,        // criminal
	NGO,              // activists / press / reform
}

/// <summary>Strategic personality of a rival division. Drives Sprint 6 action weighting.</summary>
public enum FactionAgenda
{
	Expansionist,
	Defensive,
	Predatory,
	Cooperative,
}

/// <summary>
/// Other actors in the world. Standing is the player corp's relationship to them
/// (-100..100). Sprint 6 fields (Agenda, Leader, RelationshipToPlayer, Personality)
/// drive the corporate AI loop.
/// </summary>
public sealed class Faction
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public FactionKind Kind { get; set; }
	public int Standing { get; set; } = 0;       // -100..100
	public int Reputation { get; set; } = 50;    // 0..100, public reputation
	public int Cash { get; set; } = 50_000;
	public int InternalPressure { get; set; } = 0;

	// ---- Sprint 6 corporate-AI fields ----
	public FactionAgenda Agenda { get; set; } = FactionAgenda.Cooperative;
	public string Leader { get; set; } = "";
	public int RelationshipToPlayer { get; set; } = 0;   // -100..+100
	public List<string> Personality { get; set; } = new();

	public void Clamp()
	{
		if ( Standing < -100 ) Standing = -100; else if ( Standing > 100 ) Standing = 100;
		if ( Reputation < 0 ) Reputation = 0; else if ( Reputation > 100 ) Reputation = 100;
		if ( RelationshipToPlayer < -100 ) RelationshipToPlayer = -100;
		else if ( RelationshipToPlayer > 100 ) RelationshipToPlayer = 100;
		if ( Cash < 0 ) Cash = 0;
	}
}
