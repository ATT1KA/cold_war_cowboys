using System;
using ColdWarCowboys.Corporate;
using ColdWarCowboys.Missions;
using ColdWarCowboys.Narrative;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Core;

/// <summary>
/// Top-level orchestrator. Owns the singletons every system reads from and
/// drives the day cycle through PhaseManager. Sprint 6 wires the four
/// corporate systems into AdvanceDay's CorporatePhase.
/// </summary>
public sealed class GameManager
{
	public Rng Rng { get; }
	public EventBus Bus { get; }
	public PhaseManager Phases { get; }
	public WorldState World { get; }
	public CorporateState Corporate { get; }

	public MissionResolver MissionResolver { get; }
	public ConsequenceProcessor Consequences { get; }
	public NarrativeDirector Narrative { get; }

	public FactionSystem Factions { get; }
	public PoliticsSystem Politics { get; }
	public ContractSystem Contracts { get; }
	public ReputationSystem Reputation { get; }
	public CorporateEventGenerator Events { get; }

	public GameManager( int seed )
	{
		Rng = new Rng( seed );
		Bus = new EventBus();
		Phases = new PhaseManager();
		World = new WorldState();
		Corporate = new CorporateState();

		MissionResolver = new MissionResolver( Rng, Bus );
		Consequences = new ConsequenceProcessor( Bus );
		Narrative = new NarrativeDirector( Bus );

		Factions = new FactionSystem( Rng, Bus, Consequences );
		Politics = new PoliticsSystem( Rng, Bus, Consequences );
		Contracts = new ContractSystem( Rng, Bus, Consequences );
		Reputation = new ReputationSystem( Rng, Bus, Consequences );
		Events = new CorporateEventGenerator( Rng, Bus, Consequences );

		// Wire mission outcomes into corporate fallout.
		Bus.Subscribe<MissionResolved>( r => Factions.OnMissionResolved( Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Politics.OnMissionResolved( Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Reputation.OnMissionResolved( Corporate, World, r ) );
	}

	/// <summary>
	/// Runs a complete in-game day. Sprint 6 inserts the CorporatePhase between
	/// MissionPhase and CharacterPhase: factions act, the board evaluates,
	/// contracts refresh, random events roll, then consequences flush.
	/// </summary>
	public void AdvanceDay()
	{
		Phases.BeginDay();
		while ( true )
		{
			switch ( Phases.Current )
			{
				case GamePhase.WorldPhase:
					// Sprint 1 world tick — stub.
					World.Day++;
					break;

				case GamePhase.BriefingPhase:
					// Sprint 2 briefing UI — stub.
					break;

				case GamePhase.MissionPhase:
					// Sprint 4 mission execution — stub. Real loop iterates
					// AvailableContracts the player accepted and resolves them.
					break;

				case GamePhase.CorporatePhase:
					Factions.ProcessTurn( Corporate );
					Politics.EvaluateBoard( Corporate, World );
					Contracts.RefreshContracts( Corporate );
					Events.Roll( Corporate, World );
					Reputation.Decay( Corporate, World );
					Consequences.ApplyAll( Corporate );
					Narrative.CheckCorporateTriggers( Corporate );
					break;

				case GamePhase.CharacterPhase:
					// Sprint 5 plays queued scenes — stub.
					break;

				case GamePhase.EndOfDay:
					return;
			}
			if ( !Phases.Advance() ) return;
		}
	}
}
