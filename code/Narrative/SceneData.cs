using System.Collections.Generic;

namespace CWC.Narrative;

public enum ScenePriority
{
	Background = 0,
	Routine = 1,
	Pressing = 2,
	Critical = 3,
}

// ---- Role-based casting ----

/// <summary>
/// How a cast slot is resolved at render time.
/// Identity: a specific operative by flag match (legacy behavior).
/// Role: resolved dynamically from current WorldState (e.g. highest_conscience).
/// </summary>
public enum CastSlotKind
{
	Identity,
	Role,
}

/// <summary>
/// A named slot in a scene's cast. At authoring time, the slot declares
/// how it should be filled. At render time, the NarrativeDirector resolves
/// the slot to an actual operative based on current WorldState.
/// </summary>
public sealed class CastSlot
{
	/// <summary>Slot name used in scene text, e.g. "mirror", "weapon", "triggering".</summary>
	public string Name { get; set; } = "";

	public CastSlotKind Kind { get; set; } = CastSlotKind.Role;

	/// <summary>
	/// Resolution key. For Kind=Role, one of:
	///   triggering_operative, highest_conscience, lowest_conscience,
	///   highest_social, lowest_loyalty, highest_stress, highest_morale,
	///   role:mirror, role:weapon, role:conscience, role:anchor, role:wildcard,
	///   role:climber, role:survivor, role:innocent.
	/// For Kind=Identity, the operative id or flag match spec.
	/// </summary>
	public string Resolver { get; set; } = "";
}

// ---- Trigger predicates (Night 3: stat-based, not just flags) ----

/// <summary>
/// A condition that must be true for a scene to fire. Extends beyond simple
/// flag checks to support stat thresholds, relationships, and team aggregates.
/// </summary>
public sealed class SceneTrigger
{
	/// <summary>
	/// Trigger type. Supported:
	///   flag, flag_prefix, any_op — legacy flag predicates
	///   avg_stat_below — average team stat below threshold
	///   any_stat_below — any operative's stat below threshold
	///   any_relationship_below — any relationship score below threshold
	///   no_active_missions — true when mission board is empty
	///   any_relationship_above — any relationship score above threshold
	/// </summary>
	public string Type { get; set; } = "flag";

	/// <summary>The flag name, stat name, or relationship key.</summary>
	public string Key { get; set; } = "";

	/// <summary>Threshold value for numeric triggers.</summary>
	public int Threshold { get; set; } = 0;
}

// ---- Scene template ----

/// <summary>
/// Authoring-time scene template loaded from Data/Templates/scenes.json.
/// Night 3 upgrade: supports role-based Cast slots, stat-based Triggers,
/// and token resolution with gender-conditional text.
/// </summary>
public sealed class SceneTemplate
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Speaker { get; set; } = "";
	public string Setting { get; set; } = "";

	/// <summary>
	/// AND-condition list (legacy). Flag prefixes:
	///   "flag:foo"          — world flag must contain "foo" exactly
	///   "flag_prefix:foo"   — any world flag starts with "foo"
	///   "any_op:hollowed_out" — any op-scoped flag like "hollowed_out:42" exists
	/// </summary>
	public List<string> RequiredFlags { get; set; } = new();

	/// <summary>OR-condition: any one of these blocks the scene from firing.</summary>
	public List<string> ForbiddenFlags { get; set; } = new();

	/// <summary>Night 3: structured trigger predicates (stat thresholds, relationships).</summary>
	public List<SceneTrigger> Triggers { get; set; } = new();

	/// <summary>Night 3: role-based cast slots resolved at render time.</summary>
	public List<CastSlot> Cast { get; set; } = new();

	public ScenePriority Priority { get; set; } = ScenePriority.Routine;

	/// <summary>If set, scene fires at most once per run.</summary>
	public bool OneShot { get; set; } = true;

	public List<string> TextLines { get; set; } = new();
	public List<SceneChoice> Choices { get; set; } = new();

	/// <summary>Flags to add to WorldState when the scene first fires.</summary>
	public List<string> FlagsOnFire { get; set; } = new();
}

public sealed class SceneChoice
{
	public string Label { get; set; } = "";
	public List<string> FlagsOnPick { get; set; } = new();
	public int LoyaltyDelta { get; set; }
	public int StressDelta { get; set; }
	public int ConscienceDelta { get; set; }
	public int HeatDelta { get; set; }
	public int MoraleDelta { get; set; }
}

/// <summary>
/// Concrete queued scene. Resolved against specific operatives via cast slots.
/// </summary>
public sealed class Scene
{
	public string TemplateId { get; set; } = "";
	public string Title { get; set; } = "";
	public string Speaker { get; set; } = "";
	public string Setting { get; set; } = "";
	public ScenePriority Priority { get; set; }
	public List<string> TextLines { get; set; } = new();
	public List<SceneChoice> Choices { get; set; } = new();
	public int? OperativeId { get; set; }
	/// <summary>Night 3: all resolved cast mappings (slot name → operative id).</summary>
	public Dictionary<string, int> ResolvedCast { get; set; } = new();
	public List<string> FlagsOnFire { get; set; } = new();
}
