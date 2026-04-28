using System;
using System.Collections.Generic;

namespace ColdWarCowboys.Factions;

/// <summary>Strategic personality of a rival division. Drives action weighting.</summary>
public enum FactionAgenda
{
	Expansionist,
	Defensive,
	Predatory,
	Cooperative,
}

/// <summary>
/// A rival division within the megacorp. Sprint 6 uses Faction as both a
/// data record (the JSON template) and the live political actor.
/// </summary>
public sealed class Faction
{
	public string Id { get; init; } = "";
	public string Name { get; set; } = "";
	public string Leader { get; set; } = "";
	public FactionAgenda Agenda { get; set; } = FactionAgenda.Cooperative;

	/// <summary>Internal corporate clout (0-100).</summary>
	public int Standing { get; set; } = 50;

	/// <summary>Liquid budget the faction can spend on actions and contracts.</summary>
	public int Cash { get; set; } = 100;

	/// <summary>Where this faction sits relative to the player (-100..+100).</summary>
	public int RelationshipToPlayer { get; set; } = 0;

	/// <summary>Free-form personality tags consumed by the AI weighting layer.</summary>
	public List<string> Personality { get; } = new();

	/// <summary>Clamps standing/relationship into legal ranges after mutation.</summary>
	public void Clamp()
	{
		if ( Standing < 0 ) Standing = 0; else if ( Standing > 100 ) Standing = 100;
		if ( RelationshipToPlayer < -100 ) RelationshipToPlayer = -100;
		else if ( RelationshipToPlayer > 100 ) RelationshipToPlayer = 100;
		if ( Cash < 0 ) Cash = 0;
	}
}
