using System;

namespace CWC.Core;

public enum CyclePhase
{
	Briefing,
	Assignment,
	Resolution,
	Corporate,    // Sprint 6: factions act, board evaluates, contracts refresh, events roll, reputation decays
	Aftermath,
	Review,
}

public sealed class PhaseManager
{
	public CyclePhase CurrentPhase { get; private set; } = CyclePhase.Briefing;
	public event Action<CyclePhase, CyclePhase>? PhaseChanged;

	public void Advance()
	{
		var prev = CurrentPhase;
		CurrentPhase = CurrentPhase switch
		{
			CyclePhase.Briefing   => CyclePhase.Assignment,
			CyclePhase.Assignment => CyclePhase.Resolution,
			CyclePhase.Resolution => CyclePhase.Corporate,
			CyclePhase.Corporate  => CyclePhase.Aftermath,
			CyclePhase.Aftermath  => CyclePhase.Review,
			CyclePhase.Review     => CyclePhase.Briefing,
			_                     => CyclePhase.Briefing,
		};
		PhaseChanged?.Invoke( prev, CurrentPhase );
	}

	public void Reset() { CurrentPhase = CyclePhase.Briefing; }
}
