using System.Collections.Generic;

namespace CWC.Missions;

/// <summary>
/// Authoring-time mission template loaded from Data/Templates/missions.json.
/// MissionGenerator rolls within these constraints to produce concrete Missions.
/// </summary>
public sealed class MissionTemplate
{
	public string Id { get; set; } = "";
	public string Type { get; set; } = "";          // MissionType name
	public string TitleTemplate { get; set; } = "";
	public string BriefingTemplate { get; set; } = "";

	public int MinDifficulty { get; set; } = 30;
	public int MaxDifficulty { get; set; } = 70;
	public int MoralWeight { get; set; } = 0;
	public bool IsWetWork { get; set; } = false;

	/// <summary>Per-skill weight 0..100. Sum doesn't have to equal 100.</summary>
	public Dictionary<string, int> StatWeights { get; set; } = new();

	public List<string> ClientCandidates { get; set; } = new();
	public List<string> TargetCandidates { get; set; } = new();

	public int CycleWindow { get; set; } = 3;       // cycles before expiry

	public List<string> NarrativeFlagsOnSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnPartialSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnFailure { get; set; } = new();
	public List<string> NarrativeFlagsOnCatastrophe { get; set; } = new();

	// Resolver-narrative templates. {ops}, {target}, {client}, {leader} are interpolated.
	public string SuccessText { get; set; } = "";
	public string PartialText { get; set; } = "";
	public string FailureText { get; set; } = "";
	public string CatastropheText { get; set; } = "";

	/// <summary>
	/// Interactive narrative sequence for high-stakes missions (difficulty >= 70).
	/// Null for routine missions — they resolve immediately.
	/// </summary>
	public NarrativeSequence? NarrativeSequence { get; set; }
}
