using System;
using ColdWarCowboys.Core;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Missions;

/// <summary>
/// Result bundle published on the EventBus after every resolution. Sprint 6
/// systems (faction standing, board confidence, reputation) consume this.
/// </summary>
public readonly record struct MissionResolved(
	Mission Mission,
	MissionOutcome Outcome,
	int ExposureDelta,
	int CashEarned );

/// <summary>
/// Stub resolver. Real implementation lives in Sprint 4; this provides the
/// fields Sprint 6 reads (Outcome, IssuingFactionId, Opposes/AlliedFactionIds).
/// </summary>
public sealed class MissionResolver
{
	private readonly Rng _rng;
	private readonly EventBus _bus;

	public MissionResolver( Rng rng, EventBus bus )
	{
		_rng = rng;
		_bus = bus;
	}

	/// <summary>Resolves a mission, sets its outcome, and publishes the result.</summary>
	public MissionResolved Resolve( Mission m, CorporateState corp )
	{
		// Simple stub: skill-vs-risk roll. Sprint 4 has the real model.
		double roll = _rng.NextDouble() + (corp.Rank >= CorporateRank.Director ? 0.1 : 0);
		m.Outcome = roll switch
		{
			>= 0.85 => MissionOutcome.Success,
			>= 0.55 => MissionOutcome.PartialSuccess,
			>= 0.20 => MissionOutcome.Failure,
			_ => MissionOutcome.Catastrophe,
		};
		var result = new MissionResolved( m, m.Outcome, m.Exposure, m.Reward );
		_bus.Publish( result );
		return result;
	}
}
