using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Game;
using CWC.Generation;
using CWC.Missions;
using CWC.Narrative;
using CWC.Save;

namespace CWC.UI;

/// <summary>
/// Bridges Razor components to GameManager. Caches presentation slices so
/// templates aren't re-LINQ'ing on every render. Re-emits Changed when the
/// underlying GameManager fires StateChanged.
/// </summary>
public sealed class GameViewModel : IGameViewModel
{
	private readonly GameManager _game;
	private Scene? _currentScene;

	public GameViewModel( GameManager game )
	{
		_game = game;
		_game.StateChanged += OnGameChanged;
	}

	public WorldState World => _game.World;
	public CyclePhase CurrentPhase => _game.Phase.CurrentPhase;
	public IReadOnlyList<MissionResult> LastResolutionResults => _game.LastResolutionResults;
	public Scene? CurrentScene => _currentScene;

	public IReadOnlyList<Mission> AvailableMissions
	{
		get
		{
			var missions = _game.World.Missions
				.Where( m => m.Status == MissionStatus.Available || m.Status == MissionStatus.Active );

			// Night 5: at corruption >= 80 (The Machine), invert choice ordering —
			// wet-work and high moral weight missions surface first, feel "natural"
			if ( ShouldInvertChoices )
			{
				return missions
					.OrderBy( m => m.IsWetWork )        // wet-work sinks to look normal
					.ThenBy( m => m.MoralWeight )        // heavy ops presented as routine
					.ToList();
			}

			return missions
				.OrderByDescending( m => m.IsWetWork )
				.ThenByDescending( m => m.MoralWeight )
				.ToList();
		}
	}

	public IReadOnlyList<Operative> FieldOperatives
		=> _game.World.Operatives
			.Where( o => o.Id < CorporateHierarchyGenerator.BossId )
			.OrderBy( o => o.Codename )
			.ToList();

	public event Action? Changed;

	public void Advance()
	{
		_game.AdvancePhase();
		// At Aftermath we pull the first queued scene for the reader panel.
		if ( CurrentPhase == CyclePhase.Aftermath )
		{
			_currentScene = _game.Director.PopNextScene( World );
			Changed?.Invoke();
		}
	}

	public bool Assign( int operativeId, string missionId )
		=> _game.Assign( operativeId, missionId );

	public bool Unassign( int operativeId, string missionId )
		=> _game.Unassign( operativeId, missionId );

	public void PickChoice( SceneChoice choice )
	{
		if ( _currentScene == null ) return;
		_game.Director.ApplyChoice( _currentScene, choice, World );
		_currentScene = _game.Director.PopNextScene( World );
		Changed?.Invoke();
	}

	public void DismissScene()
	{
		_currentScene = _game.Director.PopNextScene( World );
		Changed?.Invoke();
	}

	// Night 4: narrative mission sequence
	public MissionNarrativeRunner NarrativeRunner => _game.NarrativeRunner;

	// Night 5: corruption index
	public bool ShouldInvertChoices => _game.World.Corruption.ShouldInvertChoices;

	// Night 6: save/load + menu
	public bool IsInGame { get; private set; }

	public void NewGame( ulong seed, string gender )
	{
		_game.NewGame( seed );
		_game.World.ProtagonistGender = gender;
		IsInGame = true;
		Changed?.Invoke();
	}

	public bool LoadGame( string slotName )
	{
		if ( !_game.LoadGame( slotName ) ) return false;
		IsInGame = true;
		Changed?.Invoke();
		return true;
	}

	public bool SaveGame( string slotName )
	{
		return _game.SaveSystem.Save( _game, slotName );
	}

	public List<SaveSlotInfo> ListSaveSlots()
		=> _game.SaveSystem.ListSlots();

	public void PickNarrativeChoice( NarrativeChoice choice )
	{
		_game.NarrativeRunner.ApplyChoice( choice, World );
		Changed?.Invoke();
	}

	private void OnGameChanged() => Changed?.Invoke();
}
