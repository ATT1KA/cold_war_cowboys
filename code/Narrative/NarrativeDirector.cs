using System;
using System.Collections.Generic;
using ColdWarCowboys.Core;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Narrative;

/// <summary>A scene the director queues for the next character phase.</summary>
public sealed class Scene
{
	public string Id { get; init; } = Guid.NewGuid().ToString( "N" );
	public string Title { get; set; } = "";
	public string TemplateKey { get; set; } = "";
	public Dictionary<string, string> Bindings { get; } = new();
	public int Priority { get; set; }
}

/// <summary>
/// Sprint 5 picks the next dramatic beat. Sprint 6 adds corporate triggers:
/// promotions, demotions, faction confrontations, board hearings.
/// </summary>
public sealed class NarrativeDirector
{
	private readonly EventBus _bus;
	private readonly List<Scene> _queued = new();

	public IReadOnlyList<Scene> Queued => _queued;

	public NarrativeDirector( EventBus bus )
	{
		_bus = bus;
	}

	/// <summary>Queues a scene; higher priority scenes play first.</summary>
	public void Queue( Scene s )
	{
		_queued.Add( s );
		_queued.Sort( (a, b) => b.Priority.CompareTo( a.Priority ) );
		_bus.Publish( s );
	}

	/// <summary>
	/// Inspects state and queues any corporate-triggered scenes. Called from the
	/// CorporatePhase after consequences have been applied.
	/// </summary>
	public void CheckCorporateTriggers( CorporateState corp )
	{
		if ( corp.BoardConfidence >= 85 && corp.Rank < CorporateRank.BoardLiaison )
		{
			Queue( new Scene { Title = "Promotion summons", TemplateKey = "scene/promotion", Priority = 100 } );
		}
		else if ( corp.BoardConfidence <= 15 && corp.Rank > CorporateRank.Probationary )
		{
			Queue( new Scene { Title = "Demotion hearing", TemplateKey = "scene/demotion", Priority = 95 } );
		}
		if ( corp.Suspicion >= 75 )
		{
			Queue( new Scene { Title = "Internal audit", TemplateKey = "scene/audit", Priority = 80 } );
		}
		foreach ( var f in corp.Factions.Values )
		{
			if ( f.RelationshipToPlayer <= -75 )
			{
				Queue( new Scene
				{
					Title = $"Confrontation: {f.Name}",
					TemplateKey = "scene/faction_confrontation",
					Priority = 70,
					Bindings = { ["faction"] = f.Id },
				} );
			}
		}
	}

	/// <summary>Pops the next scene to play, or null if none queued.</summary>
	public Scene? Pop()
	{
		if ( _queued.Count == 0 ) return null;
		var s = _queued[0];
		_queued.RemoveAt( 0 );
		return s;
	}
}
