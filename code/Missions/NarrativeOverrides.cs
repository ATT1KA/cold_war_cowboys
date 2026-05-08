using System.Collections.Generic;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Night 4: Accumulated modifications from narrative sequence choices.
/// Fed into MissionResolver.Resolve() to shift skill weights and difficulty
/// before the final roll. Built by MissionNarrativeRunner as the player
/// walks through nodes.
/// </summary>
public sealed class NarrativeOverrides
{
	/// <summary>Additive deltas to mission skill weights from choices.</summary>
	public Dictionary<SkillKind, int> SkillWeightDeltas { get; set; } = new();

	/// <summary>Cumulative difficulty modifier. Positive = harder.</summary>
	public int DifficultyDelta { get; set; }

	/// <summary>If any choice escalated to wet-work, this forces IsWetWork on the mission.</summary>
	public bool ForcedWetWork { get; set; }

	/// <summary>True if the sequence was fully played (all nodes resolved).</summary>
	public bool SequenceCompleted { get; set; }
}
