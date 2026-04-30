using System.Collections.Generic;

namespace CWC.Core;

/// <summary>
/// Setting frozen at NewGame. Drives UI chrome, name generation flavor,
/// and the run's tonal envelope. Sprint 2's WorldGenerator picks these from
/// Data/Templates/world.json.
/// </summary>
public sealed class WorldSetting
{
	public string CorpName { get; set; } = "Panopticon Holdings";
	public string Location { get; set; } = "Neo-Detroit";
	public int Year { get; set; } = 2087;
	public string EraTagline { get; set; } = "after the second collapse";
	public List<string> ToneTags { get; set; } = new();
}
