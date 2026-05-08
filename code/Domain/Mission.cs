using System.Collections.Generic;

namespace CWC.Domain;

/// <summary>
/// A single contract on the board. Sprint 2 generates these from templates;
/// Sprint 3's MissionResolver computes a four-state outcome; Sprint 6 layers
/// faction-relations metadata so the Corporate systems can score political
/// fallout.
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

	// ---- Sprint 6: corporate / contract layer ----

	/// <summary>Faction id that issued the contract (null = direct board op).</summary>
	public string? IssuingFactionId { get; set; }

	/// <summary>Faction ids whose interests this mission opposes.</summary>
	public List<string> OpposesFactionIds { get; set; } = new();

	/// <summary>Faction ids whose interests this mission advances jointly.</summary>
	public List<string> AlliedFactionIds { get; set; } = new();

	/// <summary>Cash payout on success; consumed by Sprint 6 budget bookkeeping.</summary>
	public int Reward { get; set; } = 0;

	/// <summary>Surfaced risk shown in the brief. Negotiation can move this.</summary>
	public int Risk { get; set; } = 0;

	/// <summary>How visible the op is — feeds Sprint 6 reputation/suspicion.</summary>
	public int Exposure { get; set; } = 0;

	/// <summary>True if the contract was issued by a board directive.</summary>
	public bool IsBoardDirective { get; set; } = false;

	/// <summary>True if the contract cannot be skipped (board-mandated).</summary>
	public bool IsMandatory { get; set; } = false;

	/// <summary>Free-form tags; Sprint 6 uses "hidden_risk:N" / "hidden_exposure:N".</summary>
	public List<string> Tags { get; set; } = new();

	// ---- Night 4: narrative sequence layer ----

	/// <summary>
	/// Interactive narrative sequence for high-stakes missions (Difficulty >= 70).
	/// Null for routine missions — they resolve immediately through MissionResolver.
	/// </summary>
	public CWC.Missions.NarrativeSequence? NarrativeSequence { get; set; }
}
