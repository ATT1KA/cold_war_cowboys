using System.Collections.Generic;

namespace CWC.Domain;

/// <summary>
/// Trait axes are independent — at most one trait per axis per operative.
/// Personality / Background ~85% present, Vice ~35%, Compulsion ~25%
/// (per Sprint 2 design notes).
/// </summary>
public enum TraitAxis
{
	Personality,
	Background,
	Vice,
	Compulsion,
}

public sealed class Trait
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public TraitAxis Axis { get; set; }
	public string Description { get; set; } = "";

	// Stat / psychology modifiers applied at generation time. Sprint 2 reads these
	// from Data/Templates/traits.json.
	public Dictionary<SkillKind, int> SkillModifiers { get; set; } = new();
	public int LoyaltyModifier { get; set; }
	public int StressModifier { get; set; }
	public int ConscienceModifier { get; set; }
}
