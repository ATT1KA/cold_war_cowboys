using System.Collections.Generic;

namespace CWC.Narrative;

public enum ScenePriority
{
	Background = 0,
	Routine = 1,
	Pressing = 2,
	Critical = 3,
}

/// <summary>
/// Authoring-time scene template loaded from Data/Templates/scenes.json.
/// One template can spawn many concrete Scenes — operative tokens get
/// resolved at queue time so the same template fires for different ops.
/// </summary>
public sealed class SceneTemplate
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Speaker { get; set; } = "";
	public string Setting { get; set; } = "";

	/// <summary>
	/// AND-condition list. Flag prefixes:
	///   "flag:foo"          — world flag must contain "foo" exactly
	///   "flag_prefix:foo"   — any world flag starts with "foo"
	///   "any_op:hollowed_out" — any op-scoped flag like "hollowed_out:42" exists
	/// </summary>
	public List<string> RequiredFlags { get; set; } = new();

	/// <summary>OR-condition: any one of these blocks the scene from firing.</summary>
	public List<string> ForbiddenFlags { get; set; } = new();

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
}

/// <summary>
/// Concrete queued scene. Resolved against a specific operative if applicable.
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
	public List<string> FlagsOnFire { get; set; } = new();
}
