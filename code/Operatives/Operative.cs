using System;
using System.Collections.Generic;

namespace ColdWarCowboys.Operatives;

/// <summary>
/// A field agent on the player's roster. Sprint 6 only reads stable fields
/// (Id, Name, FactionLoyalty) so this stub is intentionally minimal.
/// </summary>
public sealed class Operative
{
	public string Id { get; init; } = Guid.NewGuid().ToString( "N" );
	public string Name { get; set; } = "Unknown";
	public string Codename { get; set; } = "";
	public int Skill { get; set; } = 50;
	public int Loyalty { get; set; } = 50;
	public int Stress { get; set; } = 0;
	public bool Active { get; set; } = true;

	/// <summary>Optional id of a non-player faction this operative privately favors.</summary>
	public string? FactionLoyalty { get; set; }

	public List<string> Tags { get; } = new();
}
