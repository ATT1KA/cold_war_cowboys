using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;

namespace CWC.Narrative;

/// <summary>
/// Reads narrative flags off WorldState (set by ConsequenceProcessor) and
/// queues scenes for the UI to consume. Selection is OneShot-aware: a fired
/// template won't re-fire unless its OneShot flag is false.
///
/// The Director never mutates WorldState mid-selection — it only reads flags
/// to choose, and adds FlagsOnFire when a scene actually fires (PopNextScene).
/// </summary>
public sealed class NarrativeDirector
{
	private readonly Dictionary<string, SceneTemplate> _templates;
	private readonly HashSet<string> _firedOneShots = new();
	private readonly Queue<Scene> _queue = new();

	public NarrativeDirector( IEnumerable<SceneTemplate> templates )
	{
		_templates = templates.ToDictionary( t => t.Id, t => t );
	}

	public int QueueDepth => _queue.Count;
	public IReadOnlyCollection<Scene> Queued => _queue;

	/// <summary>
	/// Scan world state, resolve all eligible templates, and enqueue scenes
	/// in priority order. Call after ConsequenceProcessor finishes a cycle's
	/// resolution (i.e. at Aftermath phase).
	/// </summary>
	public IReadOnlyList<Scene> ConsumeFlags( WorldState world )
	{
		var fresh = new List<Scene>();

		foreach ( var template in _templates.Values )
		{
			if ( template.OneShot && _firedOneShots.Contains( template.Id ) ) continue;
			if ( IsForbidden( template, world ) ) continue;
			if ( !IsRequiredMet( template, world, out int? opId ) ) continue;

			var scene = Materialize( template, world, opId );
			fresh.Add( scene );
		}

		// Highest priority first; stable order on tie.
		fresh.Sort( ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		foreach ( var s in fresh ) _queue.Enqueue( s );
		return fresh;
	}

	/// <summary>
	/// Pop the next scene for presentation. Marks OneShot templates fired
	/// and applies template-level FlagsOnFire to WorldState.
	/// </summary>
	public Scene? PopNextScene( WorldState world )
	{
		if ( _queue.Count == 0 ) return null;
		var scene = _queue.Dequeue();
		var template = _templates.TryGetValue( scene.TemplateId, out var t ) ? t : null;
		if ( template != null && template.OneShot )
			_firedOneShots.Add( template.Id );
		foreach ( var f in scene.FlagsOnFire )
			world.NarrativeFlags.Add( f );
		return scene;
	}

	/// <summary>
	/// Apply player choice from a scene. Mutates the targeted operative + corp.
	/// Returns the choice's flags so the caller can record them.
	/// </summary>
	public IReadOnlyList<string> ApplyChoice( Scene scene, SceneChoice choice, WorldState world )
	{
		if ( scene.OperativeId.HasValue )
		{
			var op = world.GetOperative( scene.OperativeId.Value );
			if ( op != null )
			{
				op.Psychology.Loyalty    = Math.Clamp( op.Psychology.Loyalty + choice.LoyaltyDelta, 0, 100 );
				op.Psychology.Stress     = Math.Clamp( op.Psychology.Stress + choice.StressDelta, 0, 100 );
				op.Psychology.Conscience = Math.Clamp( op.Psychology.Conscience + choice.ConscienceDelta, 0, 100 );
			}
		}
		world.Corporate.Heat = Math.Clamp( world.Corporate.Heat + choice.HeatDelta, 0, 100 );
		foreach ( var f in choice.FlagsOnPick ) world.NarrativeFlags.Add( f );
		return choice.FlagsOnPick;
	}

	// ---- predicate helpers --------------------------------------------------

	private static bool IsForbidden( SceneTemplate t, WorldState world )
	{
		foreach ( var f in t.ForbiddenFlags )
			if ( MatchesFlag( f, world, out _ ) ) return true;
		return false;
	}

	private static bool IsRequiredMet( SceneTemplate t, WorldState world, out int? opId )
	{
		opId = null;
		foreach ( var f in t.RequiredFlags )
		{
			if ( !MatchesFlag( f, world, out var matchedOp ) ) return false;
			// First op-scoped match wins as the focal operative.
			if ( opId == null && matchedOp.HasValue ) opId = matchedOp;
		}
		return true;
	}

	// Supports plain flag, "flag:foo", "flag_prefix:foo", "any_op:bar"
	private static bool MatchesFlag( string spec, WorldState world, out int? opId )
	{
		opId = null;
		if ( spec.StartsWith( "flag_prefix:" ) )
		{
			string p = spec.Substring( "flag_prefix:".Length );
			return world.NarrativeFlags.Any( f => f.StartsWith( p ) );
		}
		if ( spec.StartsWith( "any_op:" ) )
		{
			string p = spec.Substring( "any_op:".Length ) + ":";
			var hit = world.NarrativeFlags.FirstOrDefault( f => f.StartsWith( p ) );
			if ( hit == null ) return false;
			if ( int.TryParse( hit.Substring( p.Length ), out var id ) ) opId = id;
			return true;
		}
		string raw = spec.StartsWith( "flag:" ) ? spec.Substring( "flag:".Length ) : spec;
		return world.NarrativeFlags.Contains( raw );
	}

	private static Scene Materialize( SceneTemplate t, WorldState world, int? opId )
	{
		var op = opId.HasValue ? world.GetOperative( opId.Value ) : null;
		string opName = op != null
			? (string.IsNullOrEmpty( op.Codename ) ? op.Name : op.Codename)
			: "the field";

		string Resolve( string s ) => s
			.Replace( "{op}", opName )
			.Replace( "{corp}", world.Setting.CorpName )
			.Replace( "{location}", world.Setting.Location )
			.Replace( "{year}", world.Setting.Year.ToString() );

		return new Scene
		{
			TemplateId = t.Id,
			Title = Resolve( t.Title ),
			Speaker = Resolve( t.Speaker ),
			Setting = Resolve( t.Setting ),
			Priority = t.Priority,
			TextLines = t.TextLines.Select( Resolve ).ToList(),
			Choices = t.Choices.Select( c => new SceneChoice
			{
				Label = Resolve( c.Label ),
				FlagsOnPick = new List<string>( c.FlagsOnPick ),
				LoyaltyDelta = c.LoyaltyDelta,
				StressDelta = c.StressDelta,
				ConscienceDelta = c.ConscienceDelta,
				HeatDelta = c.HeatDelta,
			} ).ToList(),
			OperativeId = opId,
			FlagsOnFire = new List<string>( t.FlagsOnFire ),
		};
	}
}
