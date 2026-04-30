using System.Collections.Generic;

namespace CWC.Domain;

/// <summary>
/// A single contract on the board. Sprint 2 generates these from templates;
/// Sprint 3's MissionResolver computes a four-state outcome from assigned ops.
///
/// Resolver does NOT mutate Mission state — ConsequenceProcessor moves a mission
/// from Active → Completed/Failed/Expired and applies all deltas.
/// </summary>
public sealed class Mission
{
	public string Id { get; set; } = "";
	public string TemplateId { get; set; } = "";
	public MissionType Type { get; set; }
	public MissionStatus Status { get; set; } = MissionStatus.Available;

	public string Title { get; set; } = "";
	public string Briefing { get; set; } = "";

	public string? ClientFactionId { get; set; }
	public string? TargetFactionId { get; set; }

	/// <summary>0..100. Higher = harder. Drives target-number trim in Resolver.</summary>
	public int Difficulty { get; set; } = 50;

	/// <summary>0..100. >0 means morally non-neutral; >=50 is wet-work territory.</summary>
	public int MoralWeight { get; set; } = 0;

	public bool IsWetWork { get; set; } = false;

	/// <summary>Per-skill weights for resolution. Sum doesn't have to equal 100.</summary>
	public Dictionary<SkillKind, int> StatWeights { get; set; } = new();

	public List<int> AssignedOperativeIds { get; set; } = new();

	public int CycleAvailable { get; set; }
	public int CycleDeadline { get; set; }

	public List<string> NarrativeFlagsOnSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnPartialSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnFailure { get; set; } = new();
	public List<string> NarrativeFlagsOnCatastrophe { get; set; } = new();
}
