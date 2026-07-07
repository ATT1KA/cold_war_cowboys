using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CWC.Core;
using CWC.Narrative;

namespace CWC.Generation.Templates;

/// <summary>
/// Centralised JSON loader for Data/Templates/*. Reads once, caches by path.
/// File access goes through <see cref="CwcFiles"/> — sandbox-safe in-engine,
/// System.IO-backed in tests. Every load failure is recorded in
/// <see cref="Errors"/> and logged; nothing fails silently.
///
/// Two-pass parsing: the lenient pass loads the content (case-insensitive,
/// comments and trailing commas allowed), then a strict pass re-parses with
/// unknown properties DISALLOWED. A typo'd field name ("FlagOnFire" for
/// "FlagsOnFire") still loads what it can, but the typo lands in Errors and
/// surfaces as a boot-time content warning instead of a scene silently losing
/// its flag.
/// </summary>
public sealed class TemplateLoader
{
	private static readonly JsonSerializerOptions _opts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		// scenes.json / narrative_missions.json write enums as strings
		// ("Priority": "Pressing", "Phase": "Briefing"). Without this converter
		// System.Text.Json throws — and the whole scene library used to vanish
		// into a catch{return null}.
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly JsonSerializerOptions _strictOpts = new( _opts )
	{
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
	};

	private readonly string _root;
	private readonly Dictionary<string, string> _textCache = new();
	private readonly List<string> _errors = new();

	/// <summary>Human-readable load errors accumulated across all Deserialize calls.</summary>
	public IReadOnlyList<string> Errors => _errors;

	public TemplateLoader( string root = "Data/Templates" )
	{
		_root = root;
	}

	public List<ArchetypeTemplate> LoadArchetypes()
		=> Deserialize<List<ArchetypeTemplate>>( "archetypes.json" ) ?? new();

	public List<TraitTemplate> LoadTraits()
		=> Deserialize<List<TraitTemplate>>( "traits.json" ) ?? new();

	public NameTemplate LoadNames()
		=> Deserialize<NameTemplate>( "names.json" ) ?? new();

	public List<ScenarioTemplate> LoadScenarios()
		=> Deserialize<List<ScenarioTemplate>>( "scenarios.json" ) ?? new();

	public WorldTemplate LoadWorld()
		=> Deserialize<WorldTemplate>( "world.json" ) ?? new();

	/// <summary>
	/// Loads the scene library. Preferred layout: one scene per file (or a small
	/// list per file) anywhere under <c>Data/Templates/scenes/</c> — authors work
	/// on a single scene without touching a monolith, and category subdirectories
	/// (corruption/, psychology/, corporate/…) are just organization, not schema.
	/// A legacy flat <c>scenes.json</c> is still merged in if present. Files are
	/// visited in ordinal path order so loading stays deterministic.
	/// </summary>
	public List<SceneTemplate> LoadScenes()
	{
		var scenes = new List<SceneTemplate>();

		var files = new List<string>( CwcFiles.FindFiles( _root + "/scenes", "*.json", recursive: true ) );
		files.Sort( StringComparer.Ordinal );
		foreach ( var rel in files )
		{
			var fromFile = DeserializeSceneFile( "scenes/" + rel );
			if ( fromFile != null ) scenes.AddRange( fromFile );
		}

		if ( CwcFiles.FileExists( _root + "/scenes.json" ) )
		{
			var legacy = Deserialize<List<SceneTemplate>>( "scenes.json" );
			if ( legacy != null ) scenes.AddRange( legacy );
		}

		if ( scenes.Count == 0 )
			RecordError( $"scenes: no scene files found under {_root}/scenes/ (and no legacy scenes.json)" );
		return scenes;
	}

	/// <summary>
	/// A scene file may hold a single scene object or an array of scenes.
	/// Detected from the first significant character so authors can use either.
	/// Public so tools/ScenePreview can load one file in isolation.
	/// </summary>
	public List<SceneTemplate>? DeserializeSceneFile( string filename )
	{
		var text = ReadText( filename );
		if ( text is null )
		{
			RecordError( $"{filename}: file not found under {_root}" );
			return null;
		}
		if ( FirstSignificantChar( text ) == '[' )
			return Deserialize<List<SceneTemplate>>( filename );
		var one = Deserialize<SceneTemplate>( filename );
		return one == null ? null : new List<SceneTemplate> { one };
	}

	private static char FirstSignificantChar( string text )
	{
		int i = 0;
		while ( i < text.Length )
		{
			char c = text[i];
			if ( char.IsWhiteSpace( c ) ) { i++; continue; }
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				while ( i < text.Length && text[i] != '\n' ) i++;
				continue;
			}
			return c;
		}
		return '\0';
	}

	public T? Deserialize<T>( string filename ) where T : class
	{
		var text = ReadText( filename );
		if ( text is null )
		{
			RecordError( $"{filename}: file not found under {_root}" );
			return null;
		}
		try
		{
			var result = JsonSerializer.Deserialize<T>( text, _opts );
			if ( result == null )
			{
				RecordError( $"{filename}: deserialized to null" );
				return result;
			}
			StrictCheck<T>( filename, text );
			return result;
		}
		catch ( Exception e )
		{
			RecordError( $"{filename}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Re-parse with unknown JSON properties disallowed. Purely diagnostic — the
	/// lenient result is already loaded; this pass exists to catch typo'd field
	/// names, which the lenient parser ignores without a trace.
	/// </summary>
	private void StrictCheck<T>( string filename, string text )
	{
		try
		{
			JsonSerializer.Deserialize<T>( text, _strictOpts );
		}
		catch ( JsonException e )
		{
			RecordError( $"{filename}: unknown field — {e.Message}" );
		}
	}

	private void RecordError( string message )
	{
		_errors.Add( message );
		CwcLog.Warn( "TemplateLoader: " + message );
	}

	private string? ReadText( string filename )
	{
		if ( _textCache.TryGetValue( filename, out var cached ) ) return cached;

		var text = CwcFiles.ReadAllText( _root + "/" + filename );
		if ( text is not null )
		{
			_textCache[filename] = text;
			return text;
		}
		return null;
	}
}
