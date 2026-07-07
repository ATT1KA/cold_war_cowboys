using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CWC.Core;

namespace CWC.SmokeTest;

/// <summary>
/// System.IO-backed file provider for the smoke test. Resolves relative paths
/// against the repo root (found by walking up from the working directory until
/// Data/Templates exists), so the test sees the same content the engine mounts.
/// </summary>
public sealed class DiskFileProvider : ICwcFileProvider
{
	private readonly string _root;

	public DiskFileProvider()
	{
		var dir = Directory.GetCurrentDirectory();
		for ( int i = 0; i < 8; i++ )
		{
			if ( Directory.Exists( Path.Combine( dir, "Data", "Templates" ) ) )
			{
				_root = dir;
				return;
			}
			var parent = Directory.GetParent( dir );
			if ( parent is null ) break;
			dir = parent.FullName;
		}
		_root = Directory.GetCurrentDirectory();
	}

	private string Resolve( string path )
		=> Path.IsPathRooted( path ) ? path : Path.Combine( _root, path );

	public bool FileExists( string path ) => File.Exists( Resolve( path ) );

	public string? ReadAllText( string path )
	{
		var full = Resolve( path );
		return File.Exists( full ) ? File.ReadAllText( full ) : null;
	}

	public void WriteAllText( string path, string contents )
	{
		var full = Resolve( path );
		Directory.CreateDirectory( Path.GetDirectoryName( full )! );
		File.WriteAllText( full, contents );
	}

	public bool DeleteFile( string path )
	{
		var full = Resolve( path );
		if ( !File.Exists( full ) ) return false;
		File.Delete( full );
		return true;
	}

	public IEnumerable<string> FindFiles( string folder, string pattern, bool recursive )
	{
		var full = Resolve( folder );
		if ( !Directory.Exists( full ) ) return Array.Empty<string>();
		return Directory
			.GetFiles( full, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly )
			.Select( f => Path.GetRelativePath( full, f ).Replace( '\\', '/' ) )
			.OrderBy( f => f, StringComparer.Ordinal )
			.ToList();
	}
}
