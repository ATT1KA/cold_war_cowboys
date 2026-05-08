using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Save;

/// <summary>
/// Night 6: Serializable snapshot of a complete game state.
/// JSON-friendly — no circular refs, no engine types.
/// </summary>
public sealed class SaveData
{
	public int Version { get; set; } = 1;
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
	public List<string> ActiveCrises { get; set; } = new();

	// ---- Corruption state ----
	public double CorruptionIndex { get; set; }
	public string CurrentMilestone { get; set; } = "None";
	public List<string> CrossedMilestones { get; set; } = new();

	// ---- Phase ----
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
}

public sealed class RelationshipSave
{
	public int FromId { get; set; }
	public int ToId { get; set; }
	public string Kind { get; set; } = "";
	public int Score { get; set; }
}
