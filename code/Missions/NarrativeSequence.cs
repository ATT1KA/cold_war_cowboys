using System.Collections.Generic;

namespace CWC.Missions;

/// <summary>
/// A narrative sequence attached to high-stakes missions (difficulty >= 7 on a 1-10 scale,
/// or >= 70 on the 0-100 internal scale). Plays 3-5 interactive nodes between assignment
/// and resolution, modifying resolver parameters based on player choices.
/// </summary>
public sealed class NarrativeSequence
{
	public List<NarrativeNode> Nodes { get; set; } = new();
}

public sealed class NarrativeNode
{
	/// <summary>Phase determines when this node fires in the sequence.</summary>
	public NarrativePhase Phase { get; set; }

	/// <summary>Narrative text with token placeholders: {leader}, {target}, {client}, {location}, {corp}.</summary>
	public string Text { get; set; } = "";

	/// <summary>Player choices presented at this node.</summary>
	public List<NarrativeChoice> Choices { get; set; } = new();

	/// <summary>Optional condition: node only fires if a random roll < this value (0.0-1.0). Null = always.</summary>
	public double? RollThreshold { get; set; }

	/// <summary>Optional condition: node only fires if a previous choice had this approach tag.</summary>
	public string? RequiresApproach { get; set; }

	/// <summary>Optional condition: node only fires if operative stats meet threshold. Format: "stat:threshold" e.g. "Stealth:60".</summary>
	public string? RequiresStat { get; set; }
}

public enum NarrativePhase
{
	Briefing,
	Complication,
	ResolutionModifier,
}

public sealed class NarrativeChoice
{
	/// <summary>Display text for the choice.</summary>
	public string Text { get; set; } = "";

	/// <summary>Approach tag: stealth, social, force, cyber, compromise, abort.</summary>
	public string Approach { get; set; } = "";

	/// <summary>Overrides skill weights for resolution. Keys are SkillKind names, values are weights 0-100.</summary>
	public Dictionary<string, int> SkillWeightOverride { get; set; } = new();

	/// <summary>Added to mission difficulty. Positive = harder, negative = easier.</summary>
	public int DifficultyModifier { get; set; }

	/// <summary>Added to all assigned operatives' stress. Applied immediately on choice.</summary>
	public int StressModifier { get; set; }

	/// <summary>If true, marks the mission path as wet-work (conscience erosion applies).</summary>
	public bool WetWork { get; set; }

	/// <summary>Optional narrative text shown after choice is made.</summary>
	public string? Aftermath { get; set; }
}
