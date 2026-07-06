using System;
using CWC.Core;
using CWC.Missions;

namespace CWC.Corporate;

public enum ReputationKind
{
	Internal,
	External,
	Suspicion,
}

public readonly record struct ReputationThresholdCrossed( ReputationKind Kind, int Value, string Trigger );

/// <summary>
/// Sprint 6 reputation. Internal reputation tracks how the corp sees the
/// player division; external tracks rivals/media/government view; the
/// suspicion meter accumulates from messy/exposed operations and drives
/// audit/scrutiny events. Decay is asymmetric: positive fades faster than
/// negative — forgetting competence is easier than forgetting scandal.
/// </summary>
public sealed class ReputationSystem
{
	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly CorporateConsequenceProcessor _consequences;

	public ReputationSystem( Rng rng, EventBus bus, CorporateConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	public void OnMissionResolved( CorporateState corp, WorldState world, MissionResolved result )
	{
		var m = result.Mission;
		int internalDelta = 0;
		int externalDelta = 0;
		int suspicionDelta = 0;

		switch ( result.Outcome )
		{
			case MissionOutcome.Success:
				internalDelta = +4;
				externalDelta = m.Exposure > 0 ? +2 : 0;
				break;
			case MissionOutcome.PartialSuccess:
				internalDelta = +1;
				suspicionDelta = m.Exposure / 5;
				break;
			case MissionOutcome.Failure:
				internalDelta = -3;
				externalDelta = -m.Exposure / 4;
				suspicionDelta = m.Exposure / 3;
				break;
			case MissionOutcome.Catastrophe:
				internalDelta = -8;
				externalDelta = -10;
				suspicionDelta = 15 + m.Exposure / 2;
				break;
		}

		if ( internalDelta == 0 && externalDelta == 0 && suspicionDelta == 0 ) return;

		_consequences.Enqueue( new CorporateConsequence
		{
			Source = "Reputation",
			Description = $"Mission '{m.Title}': int{Sign( internalDelta )} ext{Sign( externalDelta )} sus{Sign( suspicionDelta )}",
			Apply = c =>
			{
				int beforeSus = c.Suspicion;
				c.InternalReputation = Math.Clamp( c.InternalReputation + internalDelta, 0, 100 );
				c.ExternalReputation = Math.Clamp( c.ExternalReputation + externalDelta, 0, 100 );
				c.Suspicion          = Math.Clamp( c.Suspicion + suspicionDelta, 0, 100 );
				CheckThresholds( beforeSus, c );
			},
		} );
	}

	public void Decay( CorporateState corp, WorldState world )
	{
		_consequences.Enqueue( new CorporateConsequence
		{
			Source = "Reputation",
			Description = "Daily decay tick.",
			Apply = c =>
			{
				c.InternalReputation = Drift( c.InternalReputation, fastUp: 1, fastDown: 2 );
				c.ExternalReputation = Drift( c.ExternalReputation, fastUp: 1, fastDown: 2 );
				int decay = world.PublicTrust >= 60 ? 4 : 3;
				c.Suspicion = Math.Max( 0, c.Suspicion - decay );
				// The street forgets too — slower than the auditors. Without a
				// heat sink every run pins at 100 by mid-game and the dial
				// stops meaning anything.
				c.Heat = Math.Max( 0, c.Heat - 3 );
			},
		} );
	}

	private void CheckThresholds( int beforeSus, CorporateState c )
	{
		if ( beforeSus < 50 && c.Suspicion >= 50 )
			_bus.Publish( new ReputationThresholdCrossed( ReputationKind.Suspicion, c.Suspicion, "media scrutiny imminent" ) );
		if ( beforeSus < 75 && c.Suspicion >= 75 )
			_bus.Publish( new ReputationThresholdCrossed( ReputationKind.Suspicion, c.Suspicion, "internal audit triggered" ) );
	}

	private static int Drift( int current, int fastUp, int fastDown )
	{
		if ( current > 50 ) return Math.Max( 50, current - fastDown );
		if ( current < 50 ) return Math.Min( 50, current + fastUp );
		return current;
	}

	private static string Sign( int v ) => v >= 0 ? $"+{v}" : v.ToString();
}
