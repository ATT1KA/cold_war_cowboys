using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Corporate;
using CWC.Domain;
using CWC.Missions;
using CWC.Narrative;
using CWC.Save;

namespace CWC.UI;

/// <summary>
/// Seam between the Razor layer and the game core. Razor components depend
/// only on this interface — never on GameManager directly. Lets us swap in
/// a fake VM for design / testing without spinning a full game.
/// </summary>
public interface IGameViewModel
{
	WorldState World { get; }
	CyclePhase CurrentPhase { get; }
	IReadOnlyList<MissionResult> LastResolutionResults { get; }
	Scene? CurrentScene { get; }

	IReadOnlyList<Mission> AvailableMissions { get; }
	IReadOnlyList<Operative> FieldOperatives { get; }

	event Action? Changed;

	void Advance();
	bool Assign( int operativeId, string missionId );
	bool Unassign( int operativeId, string missionId );
	void PickChoice( SceneChoice choice );
	void DismissScene();

	// Narrative mission sequence
	MissionNarrativeRunner NarrativeRunner { get; }
	string? PickNarrativeChoice( NarrativeChoice choice );

	// Contract negotiation (political-capital sink) + honest fit hints
	NegotiationResult NegotiateContract( string missionId, NegotiationLever lever );
	FitTier FitTierFor( Operative op, Mission m );

	// Night 5: corruption index
	bool ShouldInvertChoices { get; }

	// Night 6: save/load + menu
	bool IsInGame { get; }
	void NewGame( ulong seed, string gender );
	bool LoadGame( string slotName );
	bool SaveGame( string slotName );
	List<SaveSlotInfo> ListSaveSlots();
}
