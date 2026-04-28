using System;
using System.Collections.Generic;
using ColdWarCowboys.Core;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Missions;

/// <summary>
/// A queued state mutation. Sprint 6 systems queue consequences here instead
/// of mutating CorporateState directly so ordering and dedupe are centralized.
/// </summary>
public sealed class Consequence
{
	public string Source { get; init; } = "";
	public string Description { get; init; } = "";
	public Action<CorporateState> Apply { get; init; } = _ => { };
}

/// <summary>Drains the queue at the end of each phase.</summary>
public sealed class ConsequenceProcessor
{
	private readonly Queue<Consequence> _queue = new();
	private readonly EventBus _bus;

	public ConsequenceProcessor( EventBus bus )
	{
		_bus = bus;
	}

	/// <summary>Enqueues a consequence to be applied on the next ApplyAll().</summary>
	public void Enqueue( Consequence c ) => _queue.Enqueue( c );

	/// <summary>Drains the queue, applying every consequence in FIFO order.</summary>
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
	}
}
