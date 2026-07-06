using System.Collections.Generic;

namespace CWC.Core;

/// <summary>
/// One recorded player decision. The single cheapest high-value structure in
/// the design: scenes can reference what you chose ("you did that"), the
/// epilogue can replay your run, and save files carry your history.
/// </summary>
public sealed class ChoiceRecord
{
	/// <summary>Cycle the choice was made on.</summary>
	public int Cycle { get; set; }

	/// <summary>"scene" or "sequence".</summary>
	public string Source { get; set; } = "";

	/// <summary>Scene template id, or mission template id for sequences.</summary>
	public string SourceId { get; set; } = "";

	/// <summary>The choice label / text the player picked.</summary>
	public string Label { get; set; } = "";

	/// <summary>Flags emitted by the choice.</summary>
	public List<string> Flags { get; set; } = new();

	/// <summary>Operative the choice was about, when the scene had one.</summary>
	public int? OperativeId { get; set; }
}
