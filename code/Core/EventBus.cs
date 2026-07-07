// [FRAMEWORK] Game-agnostic. Copy as-is into another s&box project. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;

namespace CWC.Core;

/// <summary>
/// Lightweight in-process pub/sub. Sprint 6 systems publish events
/// (MissionResolved, FactionActionTaken, CorporateEventFired, RankChanged,
/// DirectiveResolved, ReputationThresholdCrossed) so Narrative + UI can react
/// without coupling to the producers.
/// </summary>
public sealed class EventBus
{
	private readonly Dictionary<Type, List<Delegate>> _handlers = new();

	public Action Subscribe<T>( Action<T> handler )
	{
		var key = typeof( T );
		if ( !_handlers.TryGetValue( key, out var list ) )
		{
			list = new List<Delegate>();
			_handlers[key] = list;
		}
		list.Add( handler );
		return () => list.Remove( handler );
	}

	public void Publish<T>( T evt )
	{
		if ( !_handlers.TryGetValue( typeof( T ), out var list ) ) return;
		// Iterate over a copy so handlers can subscribe/unsubscribe during dispatch.
		foreach ( var d in list.ToArray() )
			((Action<T>)d).Invoke( evt );
	}

	/// <summary>
	/// Drop all handlers. Called at the start of NewGame so old subscriptions
	/// from a prior run don't fire against fresh systems and double-mutate state.
	/// </summary>
	public void Clear() => _handlers.Clear();
}
