using System;
using System.Collections.Generic;

namespace CWC.Save;

/// <summary>
/// Serializable snapshot of a complete game state.
/// JSON-friendly — no circular refs, no engine types.
///
/// Version 2: adds directives (active + pending pool), fired one-shot scenes,
/// corruption crossed-milestones + choice weight, full mission fields
/// (weights, flags, contract metadata), choice log, executive flags, and
/// persistent RNG stream states — the whole save/load defect cluster.
/// </summary>
public sealed class SaveData
{
	public int Version { get; set; } = 2;
	public string SaveId { get; set; } = "";
	public string SlotName { get; set; } = "";
	public DateTime Timestamp { get; set; }
	public bool IsAutoSave { get; set; }

	// ---- World snapshot ----
	public ulong Seed { get; set; }
	public WorldSettingSave Setting { get; set; } = new();
	public CorporateStateSave Corporate { get; set; } = new();
	public List<OperativeSave> Operatives { get; set; } = new();
	public List<MissionSave> Missions { get; set; } = new();
	public List<FactionSave> Factions { get; set; } = new();
	public List<RelationshipSave> Relationships { get; set; } = new();
	public List<string> NarrativeFlags { get; set; } = new();

	public string ProtagonistGender { get; set; } = "m";
	public int Day { get; set; }
	public int HeatLevel { get; set; }
	public int PublicTrust { get; set; }
	public int ConsecutiveSuccesses { get; set; }
	public List<string> ActiveCrises { get; set; } = new();

	// ---- Choice history ----
	public List<ChoiceRecordSave> ChoiceLog { get; set; } = new();

	// ---- Narrative director state ----
	public List<string> FiredOneShotScenes { get; set; } = new();

	// ---- Corruption state ----
	public double CorruptionIndex { get; set; }
	public double CorruptionChoiceWeight { get; set; }
	public string CurrentMilestone { get; set; } = "None";
	public List<string> CrossedMilestones { get; set; } = new();

	// ---- RNG stream positions (long-lived corporate streams) ----
	public Dictionary<string, ulong[]> RngStreams { get; set; } = new();

	// ---- Phase ----
	// Recorded for diagnostics; loads deliberately resume at the Briefing
	// boundary of the saved cycle (auto-saves are taken there).
	public string CurrentPhase { get; set; } = "Briefing";
	public string SeedMissionTemplateId { get; set; } = "";

	/// <summary>Display summary for save slot UI.</summary>
	public string DisplaySummary =>
		$"{Setting.CorpName} · Cycle {Corporate.Cycle} · Day {Day}";
}

// ---- Flat DTOs for serialization ----

public sealed class WorldSettingSave
{
	public string CorpName { get; set; } = "";
	public string Location { get; set; } = "";
	public int Year { get; set; }
	public string EraTagline { get; set; } = "";
	public List<string> ToneTags { get; set; } = new();
}

public sealed class CorporateStateSave
{
	public int Heat { get; set; }
	public int Suspicion { get; set; }
	public int PoliticalPressure { get; set; }
	public int Reputation { get; set; }
	public int Budget { get; set; }
	public int Cycle { get; set; }
	public string Rank { get; set; } = "Operative";
	public int BoardConfidence { get; set; }
	public int InternalReputation { get; set; }
	public int ExternalReputation { get; set; }
	public int PoliticalCapital { get; set; }
	public string DirectorName { get; set; } = "";
	public string DirectorAgenda { get; set; } = "";
	public List<DirectiveSave> ActiveDirectives { get; set; } = new();
	public List<DirectiveSave> PendingDirectivePool { get; set; } = new();
	public List<string> AvailableContractIds { get; set; } = new();
	public List<string> RecentEventLog { get; set; } = new();
}

public sealed class DirectiveSave
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public bool Mandatory { get; set; }
	public int IgnoreConfidencePenalty { get; set; }
	public int ComplyConfidenceReward { get; set; }
	public int DeadlineDay { get; set; }
	public int DeadlineDayOffset { get; set; }
	public bool Resolved { get; set; }
	public bool Complied { get; set; }
}

public sealed class ChoiceRecordSave
{
	public int Cycle { get; set; }
	public string Source { get; set; } = "";
	public string SourceId { get; set; } = "";
	public string Label { get; set; } = "";
	public List<string> Flags { get; set; } = new();
	public int? OperativeId { get; set; }
}

public sealed class OperativeSave
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Codename { get; set; } = "";
	public string Archetype { get; set; } = "";
	public string Background { get; set; } = "";
	public string Gender { get; set; } = "";
	public string NarrativeRole { get; set; } = "";
	public string Status { get; set; } = "Active";
	public int Tenure { get; set; }
	public bool IsExecutive { get; set; }
	public string? CurrentMissionId { get; set; }
	public string? FactionLoyalty { get; set; }
	public List<string> Tags { get; set; } = new();

	// Skills
	public int Combat { get; set; }
	public int Stealth { get; set; }
	public int Hacking { get; set; }
	public int Deception { get; set; }
	public int Intimidation { get; set; }
	public int Persuasion { get; set; }

	// Psychology
	public int Loyalty { get; set; }
	public int Stress { get; set; }
	public int Morale { get; set; }
	public int Conscience { get; set; }
	public int Ambition { get; set; }
	public int WetWorkCount { get; set; }

	// Traits
	public List<TraitSave> Traits { get; set; } = new();
}

public sealed class TraitSave
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Axis { get; set; } = "";
	public string Description { get; set; } = "";
}

public sealed class MissionSave
{
	public string Id { get; set; } = "";
	public string TemplateId { get; set; } = "";
	public string Type { get; set; } = "";
	public string Status { get; set; } = "";
	public string Title { get; set; } = "";
	public string Briefing { get; set; } = "";
	public int Difficulty { get; set; }
	public int MoralWeight { get; set; }
	public bool IsWetWork { get; set; }
	public int CycleAvailable { get; set; }
	public int CycleDeadline { get; set; }
	public List<int> AssignedOperativeIds { get; set; } = new();
	public string? ClientFactionId { get; set; }
	public string? TargetFactionId { get; set; }
	public int Reward { get; set; }
	public int Risk { get; set; }
	public int Exposure { get; set; }

	// Full round-trip fields (v2): weights, flags, contract metadata.
	public Dictionary<string, int> StatWeights { get; set; } = new();
	public List<string> NarrativeFlagsOnSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnPartialSuccess { get; set; } = new();
	public List<string> NarrativeFlagsOnFailure { get; set; } = new();
	public List<string> NarrativeFlagsOnCatastrophe { get; set; } = new();
	public string SuccessText { get; set; } = "";
	public string PartialText { get; set; } = "";
	public string FailureText { get; set; } = "";
	public string CatastropheText { get; set; } = "";
	public string? IssuingFactionId { get; set; }
	public List<string> OpposesFactionIds { get; set; } = new();
	public List<string> AlliedFactionIds { get; set; } = new();
	public bool IsBoardDirective { get; set; }
	public bool IsMandatory { get; set; }
	public List<string> Tags { get; set; } = new();
	/// <summary>Sequence bodies live in templates; on load the sequence is reattached by TemplateId.</summary>
	public bool HasNarrativeSequence { get; set; }
}

public sealed class FactionSave
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Kind { get; set; } = "";
	public int Standing { get; set; }
	public int Reputation { get; set; }
	public int Cash { get; set; }
	public string Agenda { get; set; } = "";
	public string Leader { get; set; } = "";
	public int RelationshipToPlayer { get; set; }
	public List<string> Personality { get; set; } = new();
}

public sealed class RelationshipSave
{
	public int FromId { get; set; }
	public int ToId { get; set; }
	public string Kind { get; set; } = "";
	public int Score { get; set; }
}
