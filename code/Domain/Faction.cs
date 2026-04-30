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

/// <summary>
/// Other actors in the world. Standing is the player corp's relationship to them
/// (-100..100). Sprint 6 (Corporate Sim) extends this with cash/internal pressure
/// to drive contract surfacing per faction.
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
}
