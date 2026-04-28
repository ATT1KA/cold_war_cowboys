using System;
using System.Collections.Generic;

namespace ColdWarCowboys.Core;

/// <summary>
/// Lightweight in-process pub/sub used by every system to broadcast facts
/// (mission resolved, faction action taken, board hearing scheduled) without
/// hard-wiring listeners. Sprint 6 systems publish corporate events here so
/// the NarrativeDirector and UI can react without coupling.
/// </summary>
public sealed class EventBus
{
	private readonly Dictionary<Type, List<Delegate>> _handlers = new();

	/// <summary>Subscribe to events of type T. Returns an unsubscribe action.</summary>
	public Action Subscribe<T>( Action<T> handler )
	{
		var key = typeof(T);
		if ( !_handlers.TryGetValue( key, out var list ) )
		{
			list = new List<Delegate>();
			_handlers[key] = list;
		}
		list.Add( handler );
		return () => list.Remove( handler );
	}

	/// <summary>Publish an event. All current subscribers fire synchronously.</summary>
	public void Publish<T>( T evt )
	{
		if ( !_handlers.TryGetValue( typeof(T), out var list ) ) return;
		// Iterate over a copy so handlers can subscribe/unsubscribe during dispatch.
		foreach ( var d in list.ToArray() )
		{
			((Action<T>)d).Invoke( evt );
		}
	}
}
