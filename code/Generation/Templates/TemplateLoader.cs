using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CWC.Core;

namespace CWC.Generation.Templates;

/// <summary>
/// Centralised JSON loader for Data/Templates/*. Reads once, caches by path.
/// File access goes through <see cref="CwcFiles"/> — sandbox-safe in-engine,
/// System.IO-backed in tests. Every load failure is recorded in
/// <see cref="Errors"/> and logged; nothing fails silently.
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
				RecordError( $"{filename}: deserialized to null" );
			return result;
		}
		catch ( Exception e )
		{
			RecordError( $"{filename}: {e.Message}" );
			return null;
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
