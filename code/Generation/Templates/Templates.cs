using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Generation.Templates;

/// <summary>
/// POCOs deserialized from Data/Templates/*.json. Live separately from Domain
/// so generation-time concerns (rolls, weights, ranges) don't pollute runtime
/// types.
/// </summary>

public sealed class ArchetypeTemplate
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";

	/// <summary>Background category: ExMilitary, Hacker, CorpoClimber, Street, Spook, Academic.</summary>
	public string Background { get; set; } = "";

	/// <summary>Narrative role tag: conscience, mirror, weapon, innocent, survivor, climber, anchor, wildcard.</summary>
	public string NarrativeRole { get; set; } = "";

	// Per-skill (min, max) bands. BellInt rolled inside.
	public Dictionary<string, IntRange> SkillBands { get; set; } = new();

	// Psychology starting bands.
	public IntRange Loyalty { get; set; } = new( 60, 80 );
	public IntRange Stress { get; set; } = new( 10, 30 );
	public IntRange Morale { get; set; } = new( 60, 80 );
	public IntRange Conscience { get; set; } = new( 65, 90 );
	public IntRange Ambition { get; set; } = new( 40, 60 );

	// Trait-pool weighting per axis. Empty list means "any from global pool".
	public List<string> PersonalityPool { get; set; } = new();
	public List<string> BackgroundPool { get; set; } = new();
	public List<string> VicePool { get; set; } = new();
	public List<string> CompulsionPool { get; set; } = new();

	/// <summary>Flat trait pool (Night 2). 4-6 trait ids drawn from during generation.</summary>
	public List<string> TraitPool { get; set; } = new();

	/// <summary>[min, max] traits to assign from TraitPool.</summary>
	public List<int> TraitCount { get; set; } = new();

	/// <summary>3 cyberpunk-flavored backstory fragments for flavor text generation.</summary>
	public List<string> FlavorFragments { get; set; } = new();

	public List<string> CodenamePool { get; set; } = new();
}

public sealed class TraitTemplate
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Axis { get; set; } = ""; // Personality | Background | Vice | Compulsion
	public string Description { get; set; } = "";
	public Dictionary<string, int> SkillModifiers { get; set; } = new();
	public int LoyaltyModifier { get; set; }
	public int StressModifier { get; set; }
	public int ConscienceModifier { get; set; }
}

public sealed class NameTemplate
{
	public List<string> First { get; set; } = new();
	public List<string> Last { get; set; } = new();
	public List<string> Codenames { get; set; } = new();
}

public sealed class ScenarioTemplate
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Briefing { get; set; } = "";
	public string MissionTemplateId { get; set; } = "";
	public Dictionary<string, int> StartingCorporate { get; set; } = new();
	public List<string> NarrativeFlags { get; set; } = new();
}

public sealed class WorldTemplate
{
	public List<string> Corps { get; set; } = new();
	public List<string> Locations { get; set; } = new();
	public List<string> EraTaglines { get; set; } = new();
	public List<int> Years { get; set; } = new();
	public List<string> ToneTags { get; set; } = new();
	public List<FactionTemplate> Factions { get; set; } = new();
}

public sealed class FactionTemplate
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Kind { get; set; } = ""; // FactionKind name
	public int Standing { get; set; }
	public int Reputation { get; set; } = 50;
	public int Cash { get; set; } = 50_000;
}

public readonly struct IntRange
{
	public int Min { get; }
	public int Max { get; }
	public IntRange( int min, int max ) { Min = min; Max = max; }
	public int Roll( Rng r ) => r.BellInt( Min, Max );
}
