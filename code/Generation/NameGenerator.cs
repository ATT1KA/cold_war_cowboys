// [FRAMEWORK-PATTERN] The shape is reusable; the vocabulary is CWC's. Port by rename. Map: docs/FRAMEWORK_MAP.md
using System.Collections.Generic;
using CWC.Core;
using CWC.Generation.Templates;

namespace CWC.Generation;

/// <summary>
/// Procedural name + codename generation. Pulls from Data/Templates/names.json
/// (with built-in fallback pools so smoke tests work without disk I/O).
/// </summary>
public sealed class NameGenerator
{
	private readonly NameTemplate _names;
	private readonly HashSet<string> _usedFullNames = new();
	private readonly HashSet<string> _usedCodenames = new();

	private static readonly List<string> _fallbackFirst = new()
	{
		"Mara", "Cyrus", "Vivienne", "Jin", "Emil", "Renko", "Lila", "Kade",
		"Ines", "Tobias", "Saoirse", "Anand", "Noor", "Petra", "Soren", "Wren",
	};
	private static readonly List<string> _fallbackLast = new()
	{
		"Vance", "Okoye", "Chen", "Reyes", "Bartov", "Marlowe", "Sato", "Halloran",
	};
	private static readonly List<string> _fallbackCodes = new()
	{
		"GHOST", "STITCH", "ECHO", "RIDER", "ORACLE", "WICK", "ANVIL", "MIRROR",
	};

	public NameGenerator( NameTemplate? names )
	{
		_names = names ?? new NameTemplate();
		if ( _names.First.Count == 0 ) _names.First = _fallbackFirst;
		if ( _names.Last.Count == 0 ) _names.Last = _fallbackLast;
		if ( _names.Codenames.Count == 0 ) _names.Codenames = _fallbackCodes;
	}

	public string FullName( Rng r )
	{
		// Try a handful of times to avoid duplicates; settle if we exhaust.
		for ( int attempt = 0; attempt < 12; attempt++ )
		{
			var name = $"{r.Pick( _names.First )} {r.Pick( _names.Last )}";
			if ( _usedFullNames.Add( name ) ) return name;
		}
		var fallback = $"{r.Pick( _names.First )} {r.Pick( _names.Last )}-{r.Next( 100 )}";
		_usedFullNames.Add( fallback );
		return fallback;
	}

	public string Codename( Rng r, IList<string>? archetypePool = null )
	{
		var pool = (archetypePool != null && archetypePool.Count > 0) ? archetypePool : _names.Codenames;
		for ( int attempt = 0; attempt < 12; attempt++ )
		{
			var c = r.Pick( pool );
			if ( _usedCodenames.Add( c ) ) return c;
		}
		var fallback = $"{r.Pick( pool )}-{r.Next( 99 )}";
		_usedCodenames.Add( fallback );
		return fallback;
	}

	public string Gender( Rng r )
	{
		double roll = r.NextDouble();
		if ( roll < 0.45 ) return "F";
		if ( roll < 0.92 ) return "M";
		return "NB";
	}
}
