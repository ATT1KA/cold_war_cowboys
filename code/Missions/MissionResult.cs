using System.Collections.Generic;

namespace CWC.Missions;

public enum MissionOutcome
{
	Success,
	PartialSuccess,
	Failure,
	Catastrophe,
}

/// <summary>
/// Pure data emitted by MissionResolver. ConsequenceProcessor reads this and
/// mutates WorldState. Resolver itself never touches WorldState.
/// </summary>
public sealed class MissionResult
{
	public string MissionId { get; set; } = "";
	public MissionOutcome Outcome { get; set; }

	/// <summary>Final score against the difficulty target. >0 = success margin.</summary>
	public int Score { get; set; }
	public int Target { get; set; }
	public int Margin => Score - Target;

	public string NarrativeText { get; set; } = "";

	public List<int> AssignedOperativeIds { get; set; } = new();
	public Dictionary<int, OperativeImpact> PerOperative { get; set; } = new();
	public List<MissionConsequence> Consequences { get; set; } = new();
	public List<string> NarrativeFlags { get; set; } = new();

	// Decomposed score components — exposed so the UI/log can show *why* a mission landed where it did.
	public int SkillContribution { get; set; }
	public int PsychologyPenalty { get; set; }
	public int RelationshipSwing { get; set; }
	public int RngSwing { get; set; }
}

public sealed class OperativeImpact
{
	public int StressDelta { get; set; }
	public int LoyaltyDelta { get; set; }
	public int MoraleDelta { get; set; }
	public int ConscienceDelta { get; set; }
	public bool Injured { get; set; }
	public bool Killed { get; set; }
	public bool Compromised { get; set; }
	public int WetWorkDelta { get; set; }
}

public enum ConsequenceKind
{
	HeatChange,
	SuspicionChange,
	ReputationChange,
	BudgetChange,
	FactionStandingChange,
	NarrativeFlag,
	OperativeStatus,
}

public sealed class MissionConsequence
{
	public ConsequenceKind Kind { get; set; }
	public int IntValue { get; set; }
	public string StringValue { get; set; } = "";
	public int OperativeId { get; set; }
}
