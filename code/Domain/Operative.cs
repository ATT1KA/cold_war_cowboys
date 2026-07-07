// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System.Collections.Generic;
using System.Linq;

namespace CWC.Domain;

/// <summary>
/// Top-level character object. Stats + traits decided at generation; psychology
/// dials drift over the run. Status gates assignment.
/// </summary>
public sealed class Operative
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Codename { get; set; } = "";
	public string Archetype { get; set; } = "";
	public string Background { get; set; } = "";
	public string Gender { get; set; } = "";

	/// <summary>
	/// Narrative role tag assigned at generation, re-evaluated each cycle.
	/// Values: conscience, mirror, weapon, innocent, survivor, climber, anchor, wildcard.
	/// </summary>
	public string NarrativeRole { get; set; } = "";

	public Skills Skills { get; set; } = new();
	public Psychology Psychology { get; set; } = new();

	public List<Trait> Traits { get; set; } = new();

	public OperativeStatus Status { get; set; } = OperativeStatus.Active;

	/// <summary>Cycles the op has been with the corp. Drives loyalty drift + retirement scenes.</summary>
	public int Tenure { get; set; } = 0;

	/// <summary>Currently assigned mission id (if any). Cleared at end-of-cycle.</summary>
	public string? CurrentMissionId { get; set; }

	public bool IsAvailable => Status == OperativeStatus.Active && CurrentMissionId == null;

	public Trait? TraitOnAxis( TraitAxis axis )
		=> Traits.FirstOrDefault( t => t.Axis == axis );

	// ---- Sprint 6 corporate-AI surface ----

	/// <summary>Optional id of a non-player faction this operative privately favors.</summary>
	public string? FactionLoyalty { get; set; }

	/// <summary>Free-form tags consumed by Sprint 6 systems (e.g. "former_kasumi").</summary>
	public List<string> Tags { get; set; } = new();

	/// <summary>True when the operative is on the active roster (not Defected/Dead).</summary>
	public bool Active => Status == OperativeStatus.Active || Status == OperativeStatus.Injured;

	/// <summary>
	/// True for corporate-hierarchy NPCs (Director, division heads, board).
	/// They are not assignable and must never count toward team averages,
	/// psychology decay, or corruption computation.
	/// </summary>
	public bool IsExecutive { get; set; }
}
