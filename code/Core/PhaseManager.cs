using System;
using System.Collections.Generic;

namespace ColdWarCowboys.Core;

/// <summary>
/// Ordered set of phases that play out within a single in-game day. Sprint 6
/// inserts a CorporatePhase between MissionPhase and CharacterPhase so the
/// political fallout from missions is processed before character beats.
/// </summary>
public enum GamePhase
{
	WorldPhase,
	BriefingPhase,
	MissionPhase,
	CorporatePhase,
	CharacterPhase,
	EndOfDay,
}

/// <summary>Drives the day's phase progression and exposes the current phase.</summary>
public sealed class PhaseManager
{
	private static readonly GamePhase[] Order =
	{
		GamePhase.WorldPhase,
		GamePhase.BriefingPhase,
		GamePhase.MissionPhase,
		GamePhase.CorporatePhase,
		GamePhase.CharacterPhase,
		GamePhase.EndOfDay,
	};

	private int _index;

	public GamePhase Current => Order[_index];

	public event Action<GamePhase>? PhaseEntered;

	/// <summary>Resets to the first phase of a new day.</summary>
	public void BeginDay()
	{
		_index = 0;
		PhaseEntered?.Invoke( Current );
	}

	/// <summary>Advances to the next phase. Returns false when the day is over.</summary>
	public bool Advance()
	{
		if ( _index >= Order.Length - 1 ) return false;
		_index++;
		PhaseEntered?.Invoke( Current );
		return true;
	}
}
