using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;
using CWC.Missions;
using CWC.Narrative;

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
}
