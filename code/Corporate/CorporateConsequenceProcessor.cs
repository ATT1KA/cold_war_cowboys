using System;
using System.Collections.Generic;
using CWC.Core;

namespace CWC.Corporate;

/// <summary>
/// A queued state mutation produced by a Sprint 6 system. The corporate
/// systems (Faction, Politics, Contract, Reputation, Event) enqueue
/// consequences instead of mutating CorporateState directly so ordering and
/// dedupe are centralised.
///
/// Distinct from CWC.Missions.ConsequenceProcessor — that one applies
/// MissionResults from the resolver. This one applies cycle-by-cycle
/// corporate fallout.
/// </summary>
public sealed class CorporateConsequence
{
	public string Source { get; init; } = "";
	public string Description { get; init; } = "";
	public Action<CorporateState> Apply { get; init; } = _ => { };
}

/// <summary>FIFO queue + drain pass. Publishes each applied consequence on the bus.</summary>
public sealed class CorporateConsequenceProcessor
{
	private readonly Queue<CorporateConsequence> _queue = new();
	private readonly EventBus _bus;

	public CorporateConsequenceProcessor( EventBus bus ) { _bus = bus; }

	public int PendingCount => _queue.Count;

	public void Enqueue( CorporateConsequence c ) => _queue.Enqueue( c );

	public void ApplyAll( CorporateState corp )
	{
		while ( _queue.Count > 0 )
		{
			var c = _queue.Dequeue();
			c.Apply( corp );
			corp.RecentEventLog.Add( $"[{c.Source}] {c.Description}" );
			_bus.Publish( c );
		}
		corp.Clamp();
		// Cap log so it doesn't grow unbounded across long runs.
		const int maxLog = 200;
		if ( corp.RecentEventLog.Count > maxLog )
			corp.RecentEventLog.RemoveRange( 0, corp.RecentEventLog.Count - maxLog );
	}
}
