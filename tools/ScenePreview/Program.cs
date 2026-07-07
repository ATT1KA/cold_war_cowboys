using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CWC.Core;
using CWC.Game;
using CWC.Generation.Templates;
using CWC.Narrative;

namespace CWC.ScenePreview;

/// <summary>
/// Authoring-loop preview harness. Renders scenes through the REAL
/// NarrativeDirector against a real generated roster, without booting s&box —
/// the edit-prose/see-prose loop drops from minutes to seconds.
///
/// Usage:
///   dotnet run --project tools/ScenePreview -- Data/Templates/scenes/corruption/third_kill_intro.json
///   dotnet run --project tools/ScenePreview -- third_kill_intro
///   dotnet run --project tools/ScenePreview -- --all
///   dotnet run --project tools/ScenePreview -- --validate
///   dotnet run --project tools/ScenePreview -- --list-eligible --flag any_op:third_kill --cycle 8
///   options: --seed N (roster seed, default 42)
/// </summary>
internal static class Program
{
	public static int Main( string[] rawArgs )
	{
		CwcFiles.Provider = new CWC.SmokeTest.DiskFileProvider();
		CwcLog.Sink = null; // template loading is chatty; problems are reported explicitly below

		var args = new Queue<string>( rawArgs );
		string? target = null;
		bool validate = false, listEligible = false, all = false;
		ulong seed = 42;
		int? cycle = null;
		var flags = new List<string>();

		while ( args.Count > 0 )
		{
			var a = args.Dequeue();
			switch ( a )
			{
				case "--validate": validate = true; break;
				case "--list-eligible": listEligible = true; break;
				case "--all": all = true; break;
				case "--seed": seed = ulong.Parse( Require( args, a ) ); break;
				case "--cycle": cycle = int.Parse( Require( args, a ) ); break;
				case "--flag": flags.Add( Require( args, a ) ); break;
				case "--help" or "-h": PrintUsage(); return 0;
				default:
					if ( a.StartsWith( "--" ) ) { Console.WriteLine( $"unknown option '{a}'" ); PrintUsage(); return 2; }
					target = a; break;
			}
		}

		if ( validate ) return Validate( seed );

		// A real world with a real roster — same generators as the game.
		var gm = new GameManager();
		gm.NewGame( seed );
		if ( cycle.HasValue ) gm.World.Corporate.Cycle = cycle.Value;
		foreach ( var f in flags ) gm.World.NarrativeFlags.Add( f );

		if ( listEligible ) return ListEligible( gm );

		var loader = new TemplateLoader();
		var library = loader.LoadScenes();

		if ( all )
		{
			foreach ( var t in library ) Render( gm, t, library );
			return 0;
		}

		if ( target == null ) { PrintUsage(); return 2; }

		// A path renders every scene in that file; anything else is a scene id.
		List<SceneTemplate> chosen;
		if ( target.EndsWith( ".json", StringComparison.OrdinalIgnoreCase ) || File.Exists( target ) )
		{
			var dir = Path.GetDirectoryName( target );
			var fileLoader = new TemplateLoader( string.IsNullOrEmpty( dir ) ? "." : dir );
			chosen = fileLoader.DeserializeSceneFile( Path.GetFileName( target ) ) ?? new();
			foreach ( var e in fileLoader.Errors )
				Console.WriteLine( $"  !! {e}" );
			if ( chosen.Count == 0 )
			{
				Console.WriteLine( $"no scenes loaded from '{target}'" );
				return 1;
			}
			// Cross-reference against the shipped library plus this file's own
			// scenes, so a new not-yet-committed scene validates in context.
			library = library.Where( s => chosen.All( c => c.Id != s.Id ) ).Concat( chosen ).ToList();
		}
		else
		{
			var byId = library.FirstOrDefault( s => s.Id == target );
			if ( byId == null )
			{
				Console.WriteLine( $"no scene with id '{target}' — known ids:" );
				foreach ( var s in library.OrderBy( s => s.Id, StringComparer.Ordinal ) )
					Console.WriteLine( $"  {s.Id}" );
				return 1;
			}
			chosen = new List<SceneTemplate> { byId };
		}

		int problems = 0;
		foreach ( var t in chosen ) problems += Render( gm, t, library );
		return problems == 0 ? 0 : 1;
	}

	private static string Require( Queue<string> args, string opt )
	{
		if ( args.Count == 0 ) throw new ArgumentException( $"{opt} needs a value" );
		return args.Dequeue();
	}

	private static void PrintUsage()
	{
		Console.WriteLine( "ScenePreview — render CWC scenes without booting s&box" );
		Console.WriteLine();
		Console.WriteLine( "  dotnet run --project tools/ScenePreview -- <scene-file.json | scene_id>" );
		Console.WriteLine( "  dotnet run --project tools/ScenePreview -- --all" );
		Console.WriteLine( "  dotnet run --project tools/ScenePreview -- --validate" );
		Console.WriteLine( "  dotnet run --project tools/ScenePreview -- --list-eligible [--flag F]... [--cycle N]" );
		Console.WriteLine( "  common: --seed N   roster/world seed (default 42)" );
	}

	// ---- --validate: full load + every validator, exit code for CI ----------

	private static int Validate( ulong seed )
	{
		var gm = new GameManager();
		gm.NewGame( seed );
		Console.WriteLine( $"scenes: {gm.Director.TemplateCount}   missions: {gm.MissionGen.Templates.Count}" );
		if ( gm.ContentWarnings.Count == 0 )
		{
			Console.WriteLine( "VALIDATE: PASS — all templates load clean" );
			return 0;
		}
		Console.WriteLine( $"VALIDATE: FAIL — {gm.ContentWarnings.Count} problem(s):" );
		foreach ( var w in gm.ContentWarnings )
			Console.WriteLine( $"  !! {w}" );
		return 1;
	}

	// ---- --list-eligible: which scenes could fire given this state ----------

	private static int ListEligible( GameManager gm )
	{
		var fired = gm.Director.ConsumeFlags( gm.World );
		Console.WriteLine( $"state: cycle {gm.World.Corporate.Cycle}, flags [{string.Join( ", ", gm.World.NarrativeFlags )}]" );
		if ( fired.Count == 0 )
		{
			Console.WriteLine( "no scenes eligible" );
			return 0;
		}
		Console.WriteLine( $"{fired.Count} scene(s) would fire, in priority order:" );
		foreach ( var s in fired )
			Console.WriteLine( $"  [{s.Priority,-10}] {s.TemplateId}" );
		return 0;
	}

	// ---- render one scene ----------------------------------------------------

	private static readonly Regex RoleToken = new( @"\{operative\.role:([^}]+)\}", RegexOptions.Compiled );

	private static int Render( GameManager gm, SceneTemplate t, List<SceneTemplate> library )
	{
		var world = gm.World;
		var roster = world.ActiveRoster.ToList();
		var trigOp = roster.First();

		// Give the roster every KNOWN narrative role this scene references, the
		// way a drifted mid-run roster would satisfy them. Unknown roles (typos)
		// are deliberately left unassigned so the preview shows the same
		// "someone" fallback the player would see.
		var needed = new HashSet<string>();
		foreach ( var slot in t.Cast )
			if ( slot.Resolver.StartsWith( "role:" ) ) needed.Add( slot.Resolver.Substring( 5 ) );
		foreach ( var line in AllText( t ) )
			foreach ( Match m in RoleToken.Matches( line ) )
				needed.Add( m.Groups[1].Value );
		needed.IntersectWith( TemplateValidator.NarrativeRoles );
		foreach ( var op in roster ) op.NarrativeRole = "";
		int slotIdx = 0;
		foreach ( var role in needed.OrderBy( r => r, StringComparer.Ordinal ) )
			roster[slotIdx++ % roster.Count].NarrativeRole = role;

		var scene = gm.Director.RenderPreview( t, world, trigOp.Id );

		var line60 = new string( '─', 60 );
		Console.WriteLine();
		Console.WriteLine( $"── {t.Id} {line60.Substring( Math.Min( t.Id.Length + 4, 60 ) )}" );
		Console.WriteLine( $"   Title:    {scene.Title}" );
		Console.WriteLine( $"   Priority: {t.Priority}   OneShot: {( t.OneShot ? "yes" : "no" )}" );
		if ( t.RequiredFlags.Count > 0 )
			Console.WriteLine( $"   Requires: {string.Join( " AND ", t.RequiredFlags )}" );
		foreach ( var trig in t.Triggers )
			Console.WriteLine( $"   Trigger:  {trig.Type}" +
				( string.IsNullOrEmpty( trig.Key ) ? "" : $" key={trig.Key}" ) +
				( trig.Threshold != 0 ? $" threshold={trig.Threshold}" : "" ) );
		if ( !string.IsNullOrEmpty( scene.Setting ) )
			Console.WriteLine( $"   Setting:  {scene.Setting}" );
		if ( !string.IsNullOrEmpty( scene.Speaker ) )
			Console.WriteLine( $"   Speaker:  {scene.Speaker}" );

		if ( scene.ResolvedCast.Count > 0 )
		{
			Console.WriteLine( "   Cast:" );
			foreach ( var kv in scene.ResolvedCast )
			{
				var op = world.GetOperative( kv.Value );
				Console.WriteLine( $"     {kv.Key} → {op?.Codename} ({op?.Name}, {op?.Archetype}" +
					$"{( string.IsNullOrEmpty( op?.NarrativeRole ) ? "" : ", " + op!.NarrativeRole )})" );
			}
		}

		Console.WriteLine();
		foreach ( var text in scene.TextLines )
			Console.WriteLine( $"   │ {text}" );
		Console.WriteLine();
		for ( int i = 0; i < scene.Choices.Count; i++ )
		{
			var c = scene.Choices[i];
			Console.WriteLine( $"   {i + 1}. {c.Label}" );
			var deltas = DescribeDeltas( c );
			if ( deltas.Length > 0 || c.FlagsOnPick.Count > 0 )
				Console.WriteLine( $"      {deltas}{( c.FlagsOnPick.Count > 0 ? "  flags: " + string.Join( ", ", c.FlagsOnPick ) : "" )}" );
		}
		if ( t.FlagsOnFire.Count > 0 )
			Console.WriteLine( $"   Sets on fire: {string.Join( ", ", t.FlagsOnFire )}" );

		// Problems: validator findings for this scene + render-time fallbacks.
		var problems = TemplateValidator.ValidateScenes( library )
			.Where( p => p.Contains( $"'{t.Id}'" ) )
			.ToList();
		string renderedAll = string.Join( "\n", scene.TextLines.Concat( scene.Choices.Select( c => c.Label ) )
			.Append( scene.Title ).Append( scene.Setting ).Append( scene.Speaker ) );
		string templateAll = string.Join( "\n", AllText( t ) );
		if ( renderedAll.Contains( '{' ) )
			problems.Add( "render left raw {tokens} in the text" );
		if ( CountOf( renderedAll, "someone" ) > CountOf( templateAll, "someone" )
			|| CountOf( renderedAll, "the operative" ) > CountOf( templateAll, "the operative" ) )
			problems.Add( "a cast slot or role token failed to resolve (rendered a fallback name)" );

		foreach ( var p in problems )
			Console.WriteLine( $"   !! {p}" );
		return problems.Count;
	}

	private static IEnumerable<string> AllText( SceneTemplate t )
		=> t.TextLines.Concat( t.Choices.Select( c => c.Label ) )
			.Append( t.Title ).Append( t.Setting ).Append( t.Speaker );

	private static string DescribeDeltas( SceneChoice c )
	{
		var parts = new List<string>();
		void Add( string name, int v ) { if ( v != 0 ) parts.Add( $"{name} {( v > 0 ? "+" : "" )}{v}" ); }
		Add( "Loyalty", c.LoyaltyDelta );
		Add( "Stress", c.StressDelta );
		Add( "Conscience", c.ConscienceDelta );
		Add( "Heat", c.HeatDelta );
		Add( "Morale", c.MoraleDelta );
		return string.Join( "  ", parts );
	}

	private static int CountOf( string haystack, string needle )
	{
		int count = 0, idx = 0;
		while ( ( idx = haystack.IndexOf( needle, idx, StringComparison.Ordinal ) ) >= 0 )
		{
			count++;
			idx += needle.Length;
		}
		return count;
	}
}
