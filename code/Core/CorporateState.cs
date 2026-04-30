namespace CWC.Core;

/// <summary>
/// World-level pressure dials. Mutated by ConsequenceProcessor (Sprint 3+).
/// Heat = Razor / external scrutiny. PoliticalPressure = boardroom / faction politics.
/// Budget moves with mission upkeep + payouts (Sprint 6).
/// </summary>
public sealed class CorporateState
{
	public int Heat { get; set; } = 0;            // 0..100 — Razor pressure
	public int Suspicion { get; set; } = 0;       // 0..100 — internal scrutiny
	public int PoliticalPressure { get; set; } = 0;
	public int Reputation { get; set; } = 50;
	public int Budget { get; set; } = 100_000;
	public int Cycle { get; set; } = 1;

	public CorporateState Clone() => new()
	{
		Heat = Heat,
		Suspicion = Suspicion,
		PoliticalPressure = PoliticalPressure,
		Reputation = Reputation,
		Budget = Budget,
		Cycle = Cycle,
	};
}
