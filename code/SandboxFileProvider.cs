// [FRAMEWORK] Game-agnostic. Copy as-is into another s&box project. Map: docs/FRAMEWORK_MAP.md
using CWC.Core;

/// <summary>
/// In-engine file provider. Reads templates from the mounted filesystem
/// (project content, read-only) and routes writes (saves) to FileSystem.Data,
/// the per-game writable store — the only write surface the s&amp;box sandbox
/// allows. Registered by <see cref="CwcGame"/> before the first NewGame.
///
/// This file lives at code/ root (outside the subdirectories the smoke test
/// compiles) because it references Sandbox APIs directly.
/// </summary>
public sealed class SandboxFileProvider : ICwcFileProvider
{
	public bool FileExists( string path )
		=> FileSystem.Mounted.FileExists( path ) || FileSystem.Data.FileExists( path );

	public string? ReadAllText( string path )
	{
		if ( FileSystem.Mounted.FileExists( path ) )
			return FileSystem.Mounted.ReadAllText( path );
		if ( FileSystem.Data.FileExists( path ) )
			return FileSystem.Data.ReadAllText( path );
		return null;
	}

	public void WriteAllText( string path, string contents )
	{
		int slash = path.Replace( '\\', '/' ).LastIndexOf( '/' );
		if ( slash > 0 )
			FileSystem.Data.CreateDirectory( path.Substring( 0, slash ) );
		FileSystem.Data.WriteAllText( path, contents );
	}

	public bool DeleteFile( string path )
	{
		if ( !FileSystem.Data.FileExists( path ) ) return false;
		FileSystem.Data.DeleteFile( path );
		return true;
	}

	public System.Collections.Generic.IEnumerable<string> FindFiles( string folder, string pattern, bool recursive )
	{
		// BaseFileSystem.FindFile returns paths relative to the searched folder,
		// which matches the ICwcFileProvider contract. Templates are read-only
		// project content, so only the Mounted filesystem is scanned.
		if ( !FileSystem.Mounted.DirectoryExists( folder ) )
			return System.Array.Empty<string>();
		return FileSystem.Mounted.FindFile( folder, pattern, recursive );
	}
}
