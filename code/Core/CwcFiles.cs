using System;
using System.Collections.Generic;

namespace CWC.Core;

/// <summary>
/// File access seam. Game code (compiled inside the s&amp;box sandbox, where
/// System.IO and reflection are off the access list) never touches a filesystem
/// API directly — it goes through the registered provider.
///
/// Providers:
///   • In-engine: <c>CwcGame</c> registers a provider backed by
///     Sandbox.FileSystem.Mounted (reads) and Sandbox.FileSystem.Data (writes).
///   • Smoke test / editor tools: a System.IO-backed provider registered by the
///     host process (see tests/SmokeTest/DiskFileProvider.cs).
///
/// With no provider registered, reads return null and writes fail loudly via
/// CwcLog — generators fall back to their built-in content, and the failure is
/// visible instead of silent.
/// </summary>
public interface ICwcFileProvider
{
	bool FileExists( string path );
	string? ReadAllText( string path );
	void WriteAllText( string path, string contents );
	bool DeleteFile( string path );
}

public static class CwcFiles
{
	public static ICwcFileProvider? Provider { get; set; }

	public static bool FileExists( string path )
		=> Provider?.FileExists( path ) ?? false;

	public static string? ReadAllText( string path )
	{
		if ( Provider == null )
		{
			CwcLog.Warn( $"CwcFiles: no file provider registered; cannot read '{path}'." );
			return null;
		}
		try { return Provider.ReadAllText( path ); }
		catch ( Exception e )
		{
			CwcLog.Warn( $"CwcFiles: read failed for '{path}': {e.Message}" );
			return null;
		}
	}

	public static bool WriteAllText( string path, string contents )
	{
		if ( Provider == null )
		{
			CwcLog.Warn( $"CwcFiles: no file provider registered; cannot write '{path}'." );
			return false;
		}
		try { Provider.WriteAllText( path, contents ); return true; }
		catch ( Exception e )
		{
			CwcLog.Warn( $"CwcFiles: write failed for '{path}': {e.Message}" );
			return false;
		}
	}

	public static bool DeleteFile( string path )
	{
		if ( Provider == null ) return false;
		try { return Provider.DeleteFile( path ); }
		catch ( Exception e )
		{
			CwcLog.Warn( $"CwcFiles: delete failed for '{path}': {e.Message}" );
			return false;
		}
	}
}

/// <summary>
/// Logging seam. Sandbox code routes to Sandbox's Log; the smoke test routes to
/// Console. Warnings are also kept in a ring so GameManager can surface load
/// errors in the UI instead of swallowing them.
/// </summary>
public static class CwcLog
{
	public static Action<string>? Sink { get; set; }

	private static readonly List<string> _recent = new();
	private const int MaxRecent = 100;

	public static IReadOnlyList<string> Recent => _recent;

	public static void Warn( string message ) => Emit( "[warn] " + message );
	public static void Info( string message ) => Emit( "[info] " + message );

	private static void Emit( string line )
	{
		_recent.Add( line );
		if ( _recent.Count > MaxRecent ) _recent.RemoveAt( 0 );
		Sink?.Invoke( line );
	}

	public static void ClearRecent() => _recent.Clear();
}
