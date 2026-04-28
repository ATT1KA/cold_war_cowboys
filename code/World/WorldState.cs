using System;
using System.Collections.Generic;

namespace ColdWarCowboys.World;

/// <summary>
/// Snapshot of the global state outside the corp — geopolitics, public mood,
/// active scandals. Sprint 6 reads HeatLevel and PublicTrust to modulate
/// reputation decay and event probabilities.
/// </summary>
public sealed class WorldState
{
	public int Day { get; set; } = 1;
	public int HeatLevel { get; set; } = 0;
	public int PublicTrust { get; set; } = 50;
	public List<string> ActiveCrises { get; } = new();
	public List<string> Headlines { get; } = new();
}
