using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CWC.Generation.Templates;

/// <summary>
/// Centralised JSON loader for Data/Templates/*. Reads once, caches by path.
/// Path resolution: tries Sandbox FileSystem.Mounted first (S&box runtime),
/// falls back to plain disk (smoke tests, editor tools).
/// </summary>
public sealed class TemplateLoader
{
	private static readonly JsonSerializerOptions _opts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	private readonly string _root;
	private readonly Dictionary<string, string> _textCache = new();

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
		if ( text is null ) return null;
		try { return JsonSerializer.Deserialize<T>( text, _opts ); }
		catch ( Exception ) { return null; }
	}

	private string? ReadText( string filename )
	{
		if ( _textCache.TryGetValue( filename, out var cached ) ) return cached;

		string? text = TryReadDisk( filename );
		if ( text is not null )
		{
			_textCache[filename] = text;
			return text;
		}
		return null;
	}

	private string? TryReadDisk( string filename )
	{
		// Walk up from the working dir looking for Data/Templates/<file>.
		// This lets the smoke test (runs from bin/Debug/netX) find the same
		// templates the in-engine runtime sees at the project root.
		var dir = Directory.GetCurrentDirectory();
		for ( int i = 0; i < 8; i++ )
		{
			var candidate = Path.Combine( dir, _root, filename );
			try
			{
				if ( File.Exists( candidate ) ) return File.ReadAllText( candidate );
			}
			catch { /* keep walking */ }
			var parent = Directory.GetParent( dir );
			if ( parent is null ) break;
			dir = parent.FullName;
		}
		return null;
	}
}
