using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Bus-published event after a mission resolves. Sprint 6 systems (Faction,
/// Politics, Reputation) subscribe to drive corporate fallout. Built from a
/// MissionResult so subscribers don't need to re-walk the resolver internals.
/// </summary>
public readonly record struct MissionResolved(
	Mission Mission,
	MissionOutcome Outcome,
	int ExposureDelta,
	int CashEarned );
