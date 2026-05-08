using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Corporate;
using CWC.Domain;
using CWC.Generation;
using CWC.Generation.Templates;
using CWC.Missions;
using CWC.Narrative;

namespace CWC.Game;

/// <summary>
/// Top-level orchestrator. Sprint 2 wires NewGame through WorldGenerator;
/// Sprint 3 wires the mission flow (board refresh / resolve / consequences);
/// Sprint 4 hooks the NarrativeDirector at Aftermath; Sprint 6 inserts the
/// Corporate phase between Resolution and Aftermath, where factions act, the
/// board evaluates, contracts refresh, and random events roll.
/// </summary>
public sealed class GameManager
{
	public WorldState World { get; private set; } = new();
	public PhaseManager Phase { get; } = new();
	public Rng Rng { get; private set; } = new( 1 );
	public EventBus Bus { get; } = new();

	public MissionGenerator MissionGen { get; private set; } = new( new List<MissionTemplate>() );
	public MissionBoard Board { get; private set; }
	public MissionResolver Resolver { get; } = new();
	public ConsequenceProcessor Consequences { get; } = new();
	public MissionNarrativeRunner NarrativeRunner { get; } = new();
	public NarrativeDirector Director { get; private set; } = new( new List<SceneTemplate>() );

	// Sprint 6: corporate-sim layer.
	public CorporateConsequenceProcessor CorpConsequences { get; private set; }
	public FactionSystem? Factions { get; private set; }
	public PoliticsSystem? Politics { get; private set; }
	public ContractSystem? Contracts { get; private set; }
	public ReputationSystem? Reputation { get; private set; }
	public CorporateEventGenerator? CorporateEvents { get; private set; }

	public List<MissionResult> LastResolutionResults { get; private set; } = new();

	public event Action? StateChanged;

	public GameManager()
	{
		Board = new MissionBoard( MissionGen );
		CorpConsequences = new CorporateConsequenceProcessor( Bus );
	}

	public void NewGame( ulong seed )
	{
		Rng = new Rng( seed );
		Phase.Reset();

		var loader = new TemplateLoader();
		try
		{
			World = new WorldGenerator().Generate( seed );
		}
		catch
		{
			// Defensive fallback so the game still launches if templates are missing.
			World = new WorldState { Seed = seed };
			ScaffoldFallbackWorld();
		}

		MissionGen = new MissionGenerator( loader );
		Board = new MissionBoard( MissionGen );
		Director = new NarrativeDirector( loader.Deserialize<List<SceneTemplate>>( "scenes.json" ) ?? new() );

		// Seed opening mission and refresh the board for cycle 1.
		Board.SeedFromScenario( World.SeedMissionTemplateId, World, Rng.Fork( "scenario_seed" ) );
		Board.Refresh( World, Rng.Fork( "board:1" ) );

		WireCorporateLayer( loader );

		StateChanged?.Invoke();
	}

	private void WireCorporateLayer( TemplateLoader loader )
	{
		// Reset and wire fresh systems for this run.
		CorpConsequences = new CorporateConsequenceProcessor( Bus );
		Factions       = new FactionSystem( Rng.Fork( "corp_factions" ), Bus, CorpConsequences );
		Politics       = new PoliticsSystem( Rng.Fork( "corp_politics" ), Bus, CorpConsequences );
		Contracts      = new ContractSystem( Rng.Fork( "corp_contracts" ), Bus, CorpConsequences );
		Reputation     = new ReputationSystem( Rng.Fork( "corp_reputation" ), Bus, CorpConsequences );
		CorporateEvents = new CorporateEventGenerator( Rng.Fork( "corp_events" ), Bus, CorpConsequences );

		// Seed the corporate-only views of state from generated world data.
		World.Corporate.Roster.Clear();
		World.Corporate.Factions.Clear();
		foreach ( var op in World.Operatives )
			if ( op.Id < CorporateHierarchyGenerator.BossId ) World.Corporate.Roster.Add( op );
		foreach ( var f in World.Factions )
			World.Corporate.Factions[f.Id] = f;

		// Layer Sprint 6 templates on top of any defaults already provided by world.json.
		CorporateDataLoader.LoadFactions( World.Corporate, loader );
		CorporateDataLoader.LoadDirectives( World.Corporate, World, loader );
		CorporateDataLoader.LoadEvents( CorporateEvents, loader );

		// Make sure any factions added by the corporate loader are also visible
		// at the world level so the rest of the codebase finds them.
		foreach ( var kv in World.Corporate.Factions )
		{
			if ( World.Factions.Find( f => f.Id == kv.Key ) == null )
				World.Factions.Add( kv.Value );
		}

		// Subscribe corporate systems to mission-resolution events.
		Bus.Subscribe<MissionResolved>( r => Factions!.OnMissionResolved( World.Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Politics!.OnMissionResolved( World.Corporate, r ) );
		Bus.Subscribe<MissionResolved>( r => Reputation!.OnMissionResolved( World.Corporate, World, r ) );
	}

	public void AdvancePhase()
	{
		Phase.Advance();
		var current = Phase.CurrentPhase;

		switch ( current )
		{
			case CyclePhase.Briefing:
				// New cycle — bookkeeping the foundation owns.
				World.Corporate.Cycle++;
				World.Day++;
				RecoverInjuredOps();
				DecayStress();
				Board.Refresh( World, Rng.Fork( $"board:{World.Corporate.Cycle}" ) );
				break;

			case CyclePhase.Resolution:
				ResolveActiveMissions();
				// Night 5: recompute corruption index after mission outcomes
				World.Corruption.Compute( World );
				break;

			case CyclePhase.Corporate:
				RunCorporatePhase();
				break;

			case CyclePhase.Aftermath:
				Director.ConsumeFlags( World );
				break;
		}

		StateChanged?.Invoke();
	}

	private void ResolveActiveMissions()
	{
		LastResolutionResults.Clear();
		// Snapshot to allow status mutation while iterating.
		var active = new List<Mission>();
		foreach ( var m in World.Missions )
			if ( m.Status == MissionStatus.Active && m.AssignedOperativeIds.Count > 0 )
				active.Add( m );

		foreach ( var m in active )
		{
			// Night 4: use narrative overrides if this mission had a completed sequence
			NarrativeOverrides? overrides = null;
			if ( NarrativeRunner.IsComplete && NarrativeRunner.ActiveMission?.Id == m.Id )
			{
				overrides = NarrativeRunner.Overrides;
				overrides.SequenceCompleted = true;
			}

			var result = Resolver.Resolve( m, World, Rng.Fork( $"resolve:{m.Id}" ), overrides );
			Consequences.Apply( result, World );
			LastResolutionResults.Add( result );

			// Notify Sprint 6 systems that subscribe via the bus.
			Bus.Publish( new MissionResolved(
				m,
				result.Outcome,
				m.Exposure,
				result.Outcome == MissionOutcome.Success ? m.Reward : 0
			) );
		}

		// Night 4: clear narrative runner after all missions resolve
		NarrativeRunner.Cancel();
	}

	private void RunCorporatePhase()
	{
		if ( Factions == null ) return; // pre-NewGame call — nothing to do.

		Factions.ProcessTurn( World.Corporate );
		Politics!.EvaluateBoard( World.Corporate, World );
		Contracts!.RefreshContracts( World.Corporate );
		CorporateEvents!.Roll( World.Corporate, World );
		Reputation!.Decay( World.Corporate, World );
		CorpConsequences.ApplyAll( World.Corporate );

		// Surface Sprint 6 contracts on the mission board so the player can see them.
		foreach ( var contract in World.Corporate.AvailableContracts )
		{
			if ( !World.Missions.Any( m => m.Id == contract.Id ) )
				World.Missions.Add( contract );
		}

		Director.CheckCorporateTriggers( World );
	}

	/// <summary>
	/// Assignment-phase action. Adds operative to mission roster.
	/// Returns false if assignment is invalid (op unavailable, mission not available, etc.).
	/// </summary>
	public bool Assign( int operativeId, string missionId )
	{
		var op = World.GetOperative( operativeId );
		var mission = World.GetMission( missionId );
		if ( op == null || mission == null ) return false;
		if ( !op.IsAvailable ) return false;
		if ( mission.Status != MissionStatus.Available && mission.Status != MissionStatus.Active )
			return false;
		if ( mission.AssignedOperativeIds.Contains( operativeId ) ) return false;

		mission.AssignedOperativeIds.Add( operativeId );
		op.CurrentMissionId = missionId;
		if ( mission.Status == MissionStatus.Available )
			mission.Status = MissionStatus.Active;

		StateChanged?.Invoke();
		return true;
	}

	public bool Unassign( int operativeId, string missionId )
	{
		var op = World.GetOperative( operativeId );
		var mission = World.GetMission( missionId );
		if ( op == null || mission == null ) return false;
		if ( !mission.AssignedOperativeIds.Remove( operativeId ) ) return false;
		op.CurrentMissionId = null;
		if ( mission.AssignedOperativeIds.Count == 0 && mission.Status == MissionStatus.Active )
			mission.Status = MissionStatus.Available;
		StateChanged?.Invoke();
		return true;
	}

	// ---- end-of-cycle bookkeeping ----------------------------------------

	private void RecoverInjuredOps()
	{
		foreach ( var op in World.Operatives )
		{
			if ( op.Status == OperativeStatus.Injured ) op.Status = OperativeStatus.Active;
			if ( op.Status == OperativeStatus.Active ) op.Tenure++;
		}
	}

	private void DecayStress()
	{
		foreach ( var op in World.Operatives )
		{
			if ( op.Psychology.Stress > 0 )
				op.Psychology.Stress = Math.Max( 0, op.Psychology.Stress - 3 );
			if ( op.Psychology.Morale < 70 )
				op.Psychology.Morale = Math.Min( 70, op.Psychology.Morale + 1 );
		}
	}

	// ---- Sprint 1 fallback world (deterministic placeholder) -------------

	private void ScaffoldFallbackWorld()
	{
		var nameRng = Rng.Fork( "names" );
		var statRng = Rng.Fork( "stats" );

		World.Setting = new WorldSetting
		{
			CorpName = "Panopticon Holdings",
			Location = "Neo-Detroit",
			Year = 2087,
			EraTagline = "after the second collapse",
			ToneTags = new List<string> { "noir", "cyberpunk", "corporate" },
		};
		World.Corporate = new CorporateState
		{
			Heat = 10, Suspicion = 5, Reputation = 50, Budget = 100_000, Cycle = 1,
		};

		// Three placeholder factions so cross-faction mission templates have anchors.
		World.Factions.Add( new Faction { Id = "host", Name = "Panopticon Holdings", Kind = FactionKind.HostCorp, Standing = 0 } );
		World.Factions.Add( new Faction { Id = "rival_kasumi", Name = "Kasumi Dynamics", Kind = FactionKind.RivalCorp, Standing = -25 } );
		World.Factions.Add( new Faction { Id = "razor", Name = "The Razor", Kind = FactionKind.Agency, Standing = -50 } );

		// Four placeholder operatives so Sprint 3 has bodies to throw at missions.
		string[] firsts = { "Mara", "Cyrus", "Vivienne", "Jin", "Emil", "Renko", "Lila", "Kade" };
		string[] codes = { "GHOST", "STITCH", "ECHO", "RIDER", "ORACLE", "WICK" };
		string[] arches = { "operator", "ghost", "decker", "fixer" };

		for ( int i = 0; i < 4; i++ )
		{
			var op = new Operative
			{
				Id = i + 1,
				Name = nameRng.Pick( firsts ),
				Codename = nameRng.Pick( codes ),
				Archetype = arches[i % arches.Length],
				Gender = nameRng.Chance( 0.45 ) ? "F" : nameRng.Chance( 0.82 ) ? "M" : "NB",
				Tenure = 0,
			};
			op.Skills.Combat = statRng.BellInt( 30, 80 );
			op.Skills.Stealth = statRng.BellInt( 30, 80 );
			op.Skills.Hacking = statRng.BellInt( 30, 80 );
			op.Skills.Deception = statRng.BellInt( 30, 80 );
			op.Skills.Intimidation = statRng.BellInt( 20, 70 );
			op.Skills.Persuasion = statRng.BellInt( 20, 70 );

			// Archetype tilt — coarse, will be replaced by Sprint 2 archetype templates.
			switch ( op.Archetype )
			{
				case "operator": op.Skills.Combat += 15; op.Skills.Intimidation += 10; break;
				case "ghost": op.Skills.Stealth += 15; op.Skills.Deception += 10; break;
				case "decker": op.Skills.Hacking += 20; break;
				case "fixer": op.Skills.Persuasion += 15; op.Skills.Deception += 10; break;
			}

			World.Operatives.Add( op );
		}
	}
}
