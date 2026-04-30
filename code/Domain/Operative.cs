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
	public string Gender { get; set; } = "";

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
}
