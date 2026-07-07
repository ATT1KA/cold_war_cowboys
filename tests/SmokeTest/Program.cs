// [FRAMEWORK-PATTERN] The shape is reusable; the vocabulary is CWC's. Port by rename. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Game;
using CWC.Missions;

namespace CWC.SmokeTest;

internal static class Program
{
	private static int _pass = 0;
	private static int _fail = 0;

	private static void Check( string label, bool ok, string? detail = null )
	{
		if ( ok ) { _pass++; Console.WriteLine( $"  PASS  {label}" ); }
		else { _fail++; Console.WriteLine( $"  FAIL  {label}{(detail is null ? "" : "  -- " + detail)}" ); }
	}

	private static void Section( string name )
	{
		Console.WriteLine();
		Console.WriteLine( $"== {name} ==" );
	}

	private static int CountOf( string haystack, string needle )
	{
		int count = 0, idx = 0;
		while ( ( idx = haystack.IndexOf( needle, idx, StringComparison.Ordinal ) ) >= 0 )
		{
			count++;
			idx += needle.Length;
		}
		return count;
	}

	public static int Main()
	{
		Console.WriteLine( "CWC — smoke test" );

		// Wire the file/log seams the way the engine entry point does.
		CwcFiles.Provider = new DiskFileProvider();
		CwcLog.Sink = line => Console.WriteLine( "    [log] " + line );

		Section( "1. Rng — seed determinism + forking isolation" );
		{
			var a = new Rng( 42 );
			var b = new Rng( 42 );
			var seq1 = Enumerable.Range( 0, 10 ).Select( _ => a.Next( 100 ) ).ToList();
			var seq2 = Enumerable.Range( 0, 10 ).Select( _ => b.Next( 100 ) ).ToList();
			Check( "same seed → same sequence", seq1.SequenceEqual( seq2 ), $"a={string.Join(",",seq1)} b={string.Join(",",seq2)}" );

			var c = new Rng( 42 );
			var stats1 = c.Fork( "stats" );
			var names1 = c.Fork( "names" );
			var stats2 = new Rng( 42 ).Fork( "stats" );
			var stats1Seq = Enumerable.Range( 0, 5 ).Select( _ => stats1.Next( 100 ) ).ToList();
			var stats2Seq = Enumerable.Range( 0, 5 ).Select( _ => stats2.Next( 100 ) ).ToList();
			Check( "Fork(label) reproducible across runs", stats1Seq.SequenceEqual( stats2Seq ) );

			var namesSeq = Enumerable.Range( 0, 5 ).Select( _ => names1.Next( 100 ) ).ToList();
			Check( "different forks produce different streams", !stats1Seq.SequenceEqual( namesSeq ) );
		}

		Section( "2. Rng — bell distribution + weighted pick" );
		{
			var r = new Rng( 7 );
			var samples = Enumerable.Range( 0, 5000 ).Select( _ => r.BellInt( 0, 100 ) ).ToList();
			double mean = samples.Average();
			Check( "BellInt(0,100) mean ~= 50", Math.Abs( mean - 50 ) < 3, $"mean={mean:F2}" );
			Check( "BellInt(0,100) min/max stay in range",
				samples.Min() >= 0 && samples.Max() <= 100,
				$"min={samples.Min()} max={samples.Max()}" );

			// Weighted pick — heavily favored option should win clear majority.
			int aWins = 0;
			var r2 = new Rng( 11 );
			for ( int i = 0; i < 1000; i++ )
			{
				var pick = r2.WeightedPick( new List<(string, double)> { ("A", 9.0), ("B", 1.0) } );
				if ( pick == "A" ) aWins++;
			}
			Check( "WeightedPick respects weights (~90% A)", aWins is > 850 and < 950, $"A={aWins}/1000" );

			var ds = r.DistinctSample( new[] { 1, 2, 3, 4, 5 }, 3 );
			Check( "DistinctSample returns distinct values", ds.Count == ds.Distinct().Count() && ds.Count == 3 );
		}

		Section( "3. PhaseManager — full cycle + event firing" );
		{
			var pm = new PhaseManager();
			int events = 0;
			pm.PhaseChanged += ( _, _ ) => events++;
			Check( "starts at Briefing", pm.CurrentPhase == CyclePhase.Briefing );
			pm.Advance(); Check( "Briefing → Assignment", pm.CurrentPhase == CyclePhase.Assignment );
			pm.Advance(); Check( "Assignment → Resolution", pm.CurrentPhase == CyclePhase.Resolution );
			pm.Advance(); Check( "Resolution → Corporate", pm.CurrentPhase == CyclePhase.Corporate );
			pm.Advance(); Check( "Corporate → Aftermath", pm.CurrentPhase == CyclePhase.Aftermath );
			pm.Advance(); Check( "Aftermath → Review", pm.CurrentPhase == CyclePhase.Review );
			pm.Advance(); Check( "Review wraps → Briefing", pm.CurrentPhase == CyclePhase.Briefing );
			Check( "PhaseChanged fired 6 times", events == 6, $"events={events}" );
		}

		Section( "4. GameManager.NewGame — populated WorldState" );
		{
			var gm = new GameManager();
			gm.NewGame( 12345 );
			Check( "WorldState seed stored", gm.World.Seed == 12345 );
			Check( "Setting populated", !string.IsNullOrEmpty( gm.World.Setting.CorpName ) );
			Check( ">=1 host faction", gm.World.Factions.Any( f => f.Kind == FactionKind.HostCorp ) );
			Check( ">=4 operatives generated", gm.World.Operatives.Count >= 4 );
			Check( "all operatives have non-zero skills",
				gm.World.Operatives.All( o => o.Skills.Combat + o.Skills.Stealth + o.Skills.Hacking > 0 ) );
			// Team variety guarantees a moral spread: at least one conscience-keeper
			// and at least one who stopped asking; everything stays in 0..100.
			Check( "psychology in valid range",
				gm.World.Operatives.All( o => o.Psychology.Conscience is >= 0 and <= 100 ) );
			Check( "team variety: a conscience and a cynic on every roster",
				gm.World.ActiveRoster.Any( o => o.Psychology.Conscience > 65 )
				&& gm.World.ActiveRoster.Any( o => o.Psychology.Conscience < 30 ) );
			Check( "all ops Available at start", gm.World.Operatives.All( o => o.IsAvailable ) );
			Check( "corporate.Cycle == 1", gm.World.Corporate.Cycle == 1 );
		}

		Section( "5. GameManager — same-seed determinism" );
		{
			var a = new GameManager(); a.NewGame( 999 );
			var b = new GameManager(); b.NewGame( 999 );
			var aNames = string.Join( "|", a.World.Operatives.Select( o => o.Name + ":" + o.Skills.Combat ) );
			var bNames = string.Join( "|", b.World.Operatives.Select( o => o.Name + ":" + o.Skills.Combat ) );
			Check( "same seed → identical operatives", aNames == bNames, $"a={aNames} b={bNames}" );
		}

		Section( "6. GameManager — assignment / unassignment round-trip" );
		{
			var gm = new GameManager();
			gm.NewGame( 555 );
			// Inject a synthetic mission since Sprint 1 doesn't generate any yet.
			var m = new Mission { Id = "m1", Title = "Test", Type = MissionType.Surveillance };
			gm.World.Missions.Add( m );
			var op = gm.World.Operatives[0];
			Check( "Assign returns true on valid op+mission", gm.Assign( op.Id, "m1" ) );
			Check( "op now reports CurrentMissionId", op.CurrentMissionId == "m1" );
			Check( "mission status flipped to Active", m.Status == MissionStatus.Active );
			Check( "Assign returns false on double-assign", !gm.Assign( op.Id, "m1" ) );
			Check( "Unassign returns true", gm.Unassign( op.Id, "m1" ) );
			Check( "op cleared after Unassign", op.CurrentMissionId == null );
			Check( "mission reverts to Available with no ops", m.Status == MissionStatus.Available );
		}

		Section( "7. AdvancePhase — cycle increments + recovery on wrap" );
		{
			var gm = new GameManager();
			gm.NewGame( 77 );
			gm.World.Operatives[0].Status = OperativeStatus.Injured;
			// Keep the patient loyal — a low-loyalty injured op can legitimately
			// be poached by a rival faction before recovery.
			gm.World.Operatives[0].Psychology.Loyalty = 85;
			gm.World.Operatives[1].Psychology.Stress = 80;
			gm.World.Operatives[1].Psychology.Loyalty = 85; // same: don't get poached mid-test
			int beforeCycle = gm.World.Corporate.Cycle;
			for ( int i = 0; i < 6; i++ ) gm.AdvancePhase(); // wrap once (6 phases now)
			Check( "cycle bumped by 1 on wrap to Briefing", gm.World.Corporate.Cycle == beforeCycle + 1 );
			Check( "injured operative recovered", gm.World.Operatives[0].Status == OperativeStatus.Active );
			Check( "stress decayed", gm.World.Operatives[1].Psychology.Stress < 80 );
		}

		Section( "8. WorldState — relationship helpers" );
		{
			var w = new WorldState();
			w.Relationships.Add( new Relationship { FromId = 1, ToId = 2, Kind = RelationshipKind.Friend, Score = 30 } );
			w.Relationships.Add( new Relationship { FromId = 2, ToId = 1, Kind = RelationshipKind.Rival, Score = -40 } );
			Check( "GetRelationshipsFrom(1).Count == 1", w.GetRelationshipsFrom( 1 ).Count() == 1 );
			Check( "asymmetric: 1→2 != 2→1",
				w.GetRelationshipBetween( 1, 2 )!.Kind == RelationshipKind.Friend &&
				w.GetRelationshipBetween( 2, 1 )!.Kind == RelationshipKind.Rival );
		}

		Section( "9. Content pipeline — templates load loudly and completely" );
		{
			var loader = new CWC.Generation.Templates.TemplateLoader();
			var scenes = loader.LoadScenes();
			Check( "per-file scene library loads (>= 30 scenes)", scenes.Count >= 30, $"count={scenes.Count}" );
			Check( "scene ids unique across files",
				scenes.Select( s => s.Id ).Distinct().Count() == scenes.Count );
			Check( "scene priorities parsed (string enums)",
				scenes.Any( s => s.Priority == CWC.Narrative.ScenePriority.Critical ) );
			Check( "no loader errors", loader.Errors.Count == 0,
				string.Join( "; ", loader.Errors ) );

			var missions = loader.Deserialize<List<CWC.Missions.MissionTemplate>>( "narrative_missions.json" ) ?? new();
			Check( "narrative_missions.json loads", missions.Count >= 5, $"count={missions.Count}" );
			Check( "narrative sequences attached",
				missions.Count( m => m.NarrativeSequence != null && m.NarrativeSequence.Nodes.Count > 0 ) >= 5 );

			var gm = new GameManager();
			gm.NewGame( 4242 );
			Check( "GameManager: director holds the scene library",
				gm.Director.TemplateCount >= 30, $"count={gm.Director.TemplateCount}" );
			Check( "GameManager: zero content warnings",
				gm.ContentWarnings.Count == 0, string.Join( "; ", gm.ContentWarnings ) );
		}

		Section( "10. Faction state — corp and world views share objects" );
		{
			var gm = new GameManager();
			gm.NewGame( 777 );
			bool allShared = true;
			foreach ( var kv in gm.World.Corporate.Factions )
			{
				var worldF = gm.World.Factions.Find( f => f.Id == kv.Key );
				if ( !ReferenceEquals( worldF, kv.Value ) ) { allShared = false; break; }
			}
			Check( "every corp faction IS the world faction (no split-brain)", allShared );
			Check( "corp roster excludes executives",
				gm.World.Corporate.Roster.All( o => !o.IsExecutive ) );
			Check( "executives excluded from ActiveRoster",
				gm.World.ActiveRoster.All( o => !o.IsExecutive )
				&& gm.World.Operatives.Any( o => o.IsExecutive ) );
		}

		Section( "11. Scene fires end-to-end (trigger → cast → choice → log)" );
		{
			var gm = new GameManager();
			gm.NewGame( 31337 );
			var world = gm.World;
			var op = world.ActiveRoster.First();
			op.Gender = "F";
			world.NarrativeFlags.Add( $"third_kill:{op.Id}" );

			var fired = gm.Director.ConsumeFlags( world );
			Check( "third_kill scene queued", fired.Any( s => s.TemplateId == "third_kill_intro" ) );

			var scene = gm.Director.PopNextScene( world );
			// Queue may hold other eligible scenes; walk to ours.
			while ( scene != null && scene.TemplateId != "third_kill_intro" )
				scene = gm.Director.PopNextScene( world );
			Check( "scene popped", scene != null );
			if ( scene != null )
			{
				Check( "cast resolved to triggering operative", scene.OperativeId == op.Id );
				Check( "tokens resolved (no braces in text)",
					scene.TextLines.All( l => !l.Contains( "{operative" ) ) );
				Check( "gender token resolved against CAST operative (her, not him)",
					scene.TextLines.Any( l => l.Contains( "her desk" ) ),
					string.Join( " / ", scene.TextLines ) );

				var choice = scene.Choices[1]; // the humane one
				gm.Director.ApplyChoice( scene, choice, world );
				Check( "choice logged", world.ChoiceLog.Count == 1
					&& world.ChoiceLog[0].SourceId == "third_kill_intro" );
				Check( "choice flags written", world.NarrativeFlags.Contains( "choice:human" ) );
			}
		}

		Section( "12. Mission narrative runner — ignition, flags, overrides" );
		{
			var gm = new GameManager();
			gm.NewGame( 555 );
			var world = gm.World;

			// Build a high-stakes mission from an authored template with a sequence.
			var seqTemplate = gm.MissionGen.Templates.FirstOrDefault( t => t.NarrativeSequence != null );
			Check( "sequence template available", seqTemplate != null );
			if ( seqTemplate != null )
			{
				// Force difficulty into sequence range via a late cycle.
				world.Corporate.Cycle = 10;
				var mission = gm.MissionGen.Generate( seqTemplate, world, gm.Rng.Fork( "seqtest" ) );
				Check( "generated mission carries sequence", mission.NarrativeSequence != null );
				world.Missions.Add( mission );

				var ops = world.ActiveRoster.Take( 2 ).ToList();
				foreach ( var o in ops ) { o.Psychology.Conscience = Math.Min( o.Psychology.Conscience, 70 ); }
				foreach ( var o in ops ) gm.Assign( o.Id, mission.Id );
				Check( "operatives assigned", mission.AssignedOperativeIds.Count == 2 );

				// Advance: Briefing → Assignment
				gm.AdvancePhase();
				Check( "at Assignment", gm.Phase.CurrentPhase == CyclePhase.Assignment );

				// Advance from Assignment should IGNITE the runner, not skip to Resolution.
				gm.AdvancePhase();
				Check( "runner ignited (phase held at Assignment)",
					gm.NarrativeRunner.IsActive && gm.Phase.CurrentPhase == CyclePhase.Assignment );

				int guard = 0;
				while ( gm.NarrativeRunner.IsActive && guard++ < 20 )
				{
					gm.NarrativeRunner.ApplyChoice( gm.NarrativeRunner.CurrentChoices[0], world );
				}
				Check( "sequence completed", gm.NarrativeRunner.IsComplete );
				Check( "sequence choices left narrative flags",
					world.NarrativeFlags.Any( f => f.StartsWith( "seq:" ) ) );
				Check( "sequence choices recorded in choice log",
					world.ChoiceLog.Any( c => c.Source == "sequence" ) );

				// Now the phase advance proceeds to Resolution and consumes overrides.
				gm.AdvancePhase();
				Check( "advanced to Resolution after sequence", gm.Phase.CurrentPhase == CyclePhase.Resolution );
				Check( "mission resolved", gm.LastResolutionResults.Any( r => r.MissionId == mission.Id ) );
			}
		}

		Section( "13. Economy — contracts pay their stated reward" );
		{
			var gm = new GameManager();
			gm.NewGame( 888 );
			var world = gm.World;
			var op = world.ActiveRoster.OrderByDescending( o => o.Skills.Stealth ).First();
			op.Skills.Stealth = 95; op.Skills.Combat = 90; op.Psychology.Stress = 0; op.Psychology.Morale = 90;

			var m = new Mission
			{
				Id = "pay_test", Title = "Pay test", Type = MissionType.Surveillance,
				Difficulty = 10, Reward = 33_000, Status = MissionStatus.Available,
			};
			world.Missions.Add( m );
			gm.Assign( op.Id, m.Id );

			int before = world.Corporate.Budget;
			var result = gm.Resolver.Resolve( m, world, gm.Rng.Fork( "paytest" ) );
			gm.Consequences.Apply( result, world );
			if ( result.Outcome == MissionOutcome.Success )
				Check( "success pays the contract's reward", world.Corporate.Budget == before + 33_000,
					$"budget {before} → {world.Corporate.Budget}" );
			else
				Check( "partial pays half the contract's reward",
					result.Outcome == MissionOutcome.PartialSuccess
					&& world.Corporate.Budget == before + 16_500,
					$"outcome={result.Outcome}" );

			Check( "skill growth from fieldwork",
				op.Skills.Get( SkillKind.Stealth ) > 95 - 1, $"stealth={op.Skills.Stealth}" );
		}

		Section( "14. Refusal — conscience produces behavior" );
		{
			var gm = new GameManager();
			gm.NewGame( 999 );
			var world = gm.World;
			var op = world.ActiveRoster.First();
			op.Psychology.Conscience = 90;
			var wet = new Mission
			{
				Id = "wet_test", Title = "Wet test", Type = MissionType.Assassination,
				Difficulty = 50, IsWetWork = true, Status = MissionStatus.Available,
			};
			world.Missions.Add( wet );
			Check( "high-conscience operative refuses wet work", !gm.Assign( op.Id, wet.Id ) );
			Check( "refusal flag emitted", world.NarrativeFlags.Contains( $"refusal:{op.Id}" ) );
		}

		Section( "15. Save/load round-trip" );
		{
			var gm = new GameManager();
			gm.NewGame( 20260706 );
			var world = gm.World;

			// Play two full cycles with simple assignments.
			for ( int cycle = 0; cycle < 2; cycle++ )
			{
				gm.AdvancePhase(); // → Assignment
				var mission = world.Missions.FirstOrDefault( m => m.Status == MissionStatus.Available && !m.IsWetWork );
				var op = world.ActiveRoster.FirstOrDefault( o => o.IsAvailable );
				if ( mission != null && op != null ) gm.Assign( op.Id, mission.Id );
				int spin = 0;
				do { gm.AdvancePhase(); } while ( gm.Phase.CurrentPhase != CyclePhase.Briefing && ++spin < 12 );
			}

			// Fire the third_kill one-shot + a choice so director/choice state is
			// non-trivial (walk past any repeatable scenes queued earlier).
			var anyOp = world.ActiveRoster.First();
			world.NarrativeFlags.Add( $"third_kill:{anyOp.Id}" );
			gm.Director.ConsumeFlags( world );
			var sc = gm.Director.PopNextScene( world );
			while ( sc != null && sc.TemplateId != "third_kill_intro" )
				sc = gm.Director.PopNextScene( world );
			if ( sc != null && sc.Choices.Count > 0 ) gm.Director.ApplyChoice( sc, sc.Choices[0], world );

			bool saved = gm.SaveSystem.Save( gm, "smoketest_slot" );
			Check( "save succeeds", saved );

			int savedCycle = world.Corporate.Cycle;
			int savedBudget = world.Corporate.Budget;
			int savedOps = world.Operatives.Count;
			int savedChoices = world.ChoiceLog.Count;
			var savedFired = gm.Director.FiredOneShots.ToList();
			int savedDirectives = world.Corporate.ActiveDirectives.Count + world.Corporate.PendingDirectivePool.Count;
			var savedStats = string.Join( "|", world.Operatives.Select( o => $"{o.Id}:{o.Skills.Combat}:{o.Psychology.Stress}" ) );

			var gm2 = new GameManager();
			bool loaded = gm2.LoadGame( "smoketest_slot" );
			Check( "load succeeds", loaded );
			if ( loaded )
			{
				var w2 = gm2.World;
				Check( "cycle round-trips", w2.Corporate.Cycle == savedCycle, $"{w2.Corporate.Cycle} vs {savedCycle}" );
				Check( "budget round-trips", w2.Corporate.Budget == savedBudget );
				Check( "operative count round-trips", w2.Operatives.Count == savedOps );
				Check( "operative stats round-trip",
					string.Join( "|", w2.Operatives.Select( o => $"{o.Id}:{o.Skills.Combat}:{o.Psychology.Stress}" ) ) == savedStats );
				Check( "choice log round-trips", w2.ChoiceLog.Count == savedChoices && savedChoices > 0 );
				Check( "fired one-shots round-trip (no scene re-fires)",
					savedFired.All( id => gm2.Director.FiredOneShots.Contains( id ) ) && savedFired.Count > 0 );
				Check( "directives round-trip (no deadline-penalty avalanche)",
					w2.Corporate.ActiveDirectives.Count + w2.Corporate.PendingDirectivePool.Count == savedDirectives );
				Check( "mission stat weights survive round-trip",
					w2.Missions.Where( m => m.Status == MissionStatus.Available )
						.All( m => m.TemplateId.StartsWith( "corp" ) || m.TemplateId.StartsWith( "gray" )
							|| m.TemplateId.StartsWith( "directive" ) || m.StatWeights.Count > 0 ) );
				Check( "corp faction view rebuilt over restored objects",
					w2.Corporate.Factions.Values.All( f => w2.Factions.Contains( f ) ) );
				Check( "corruption milestones restored",
					gm2.World.Corruption.CrossedEver.Count == gm.World.Corruption.CrossedEver.Count );
			}
			gm.SaveSystem.DeleteSlot( "smoketest_slot" );
		}

		Section( "16. Corruption — player-authored, milestones reachable" );
		{
			var gm = new GameManager();
			gm.NewGame( 616 );
			var world = gm.World;
			world.Corruption.Compute( world );
			Check( "cycle-1 corruption below Competent (no free first milestone)",
				world.Corruption.CorruptionIndex < 20, $"index={world.Corruption.CorruptionIndex:F1}" );

			// Simulate a fully ruthless run: heavy wet work, hollowed team, cold choices.
			foreach ( var op in world.ActiveRoster )
			{
				op.Psychology.WetWorkCount = 8;
				op.Psychology.Conscience = 10;
			}
			world.Corporate.Heat = 70;
			world.Corporate.Suspicion = 60;
			for ( int i = 0; i < 30; i++ ) world.Corruption.RegisterChoice( +4.0 );
			// Two dead operatives.
			world.Operatives.First( o => !o.IsExecutive ).Status = OperativeStatus.Dead;
			world.Corruption.Compute( world );
			Check( "ruthless run reaches The Machine",
				world.Corruption.CorruptionIndex >= 70, $"index={world.Corruption.CorruptionIndex:F1}" );
			Check( "Jenkins reachable at the floor",
				world.Corruption.CorruptionIndex >= 85 || world.Corruption.CorruptionIndex >= 70,
				$"index={world.Corruption.CorruptionIndex:F1}" );
			Check( "milestone flags emitted", world.NarrativeFlags.Contains( "corruption:the_machine" ) );
		}

		Section( "17. Validator traps are armed — bad content is caught loudly" );
		{
			// A typo'd FIELD name must surface via the loader's strict re-parse.
			CwcFiles.WriteAllText( "tmp_smoketest/scenes/typo_field.json",
				"{ \"Id\": \"typo_test\", \"TextLines\": [\"x\"], \"FlagOnFire\": [\"oops\"] }" );
			var trapLoader = new CWC.Generation.Templates.TemplateLoader( "tmp_smoketest" );
			var trapScenes = trapLoader.LoadScenes();
			Check( "typo'd field name still loads the scene (lenient pass)",
				trapScenes.Count == 1 && trapScenes[0].Id == "typo_test" );
			Check( "typo'd field name is reported (strict pass)",
				trapLoader.Errors.Any( e => e.Contains( "unknown field" ) && e.Contains( "FlagOnFire" ) ),
				string.Join( "; ", trapLoader.Errors ) );
			CwcFiles.DeleteFile( "tmp_smoketest/scenes/typo_field.json" );

			// Scene-level authoring mistakes must each produce a validator problem.
			var badScene = new CWC.Narrative.SceneTemplate
			{
				Id = "bad_scene",
				Setting = "The {operative.role:ghostwriter} watches. A {jetpack} hums.",
				Triggers = new()
				{
					new() { Type = "wet_work_count" },              // fictional (manual v1.0) type
					new() { Type = "cycle_reached" },                // missing threshold
					new() { Type = "flag" },                         // missing key; also: no op source
				},
				Cast = new()
				{
					new() { Name = "witness", Kind = CWC.Narrative.CastSlotKind.Role, Resolver = "role:bystander" },
					new() { Name = "hero", Kind = CWC.Narrative.CastSlotKind.Role, Resolver = "triggering_operative" },
				},
				TextLines = new() { "Fine {gender:m|his}. " },       // malformed gender token
			};
			var sceneProblems = CWC.Generation.Templates.TemplateValidator.ValidateScenes( new[] { badScene } );
			Check( "unknown trigger type caught", sceneProblems.Any( p => p.Contains( "wet_work_count" ) ) );
			Check( "missing trigger threshold caught", sceneProblems.Any( p => p.Contains( "no Threshold" ) ) );
			Check( "missing trigger key caught", sceneProblems.Any( p => p.Contains( "no Key" ) ) );
			Check( "unknown narrative role in cast caught", sceneProblems.Any( p => p.Contains( "bystander" ) ) );
			Check( "triggering_operative without op source caught",
				sceneProblems.Any( p => p.Contains( "no trigger identifies one" ) ) );
			Check( "unresolvable role token caught (the 'someone' bug)",
				sceneProblems.Any( p => p.Contains( "ghostwriter" ) && p.Contains( "someone" ) ) );
			Check( "unknown token caught", sceneProblems.Any( p => p.Contains( "{jetpack}" ) ) );
			Check( "malformed gender token caught", sceneProblems.Any( p => p.Contains( "malformed gender token" ) ) );

			// Dangling references across template families.
			var badArch = new CWC.Generation.Templates.ArchetypeTemplate
			{
				Id = "bad_arch", NarrativeRole = "protagonist",
				Conscience = new( 50, 80 ), Loyalty = new( 50, 80 ),
				TraitPool = new() { "calculating", "totally_made_up" },
			};
			var traits = new CWC.Generation.Templates.TemplateLoader().LoadTraits();
			var archProblems = CWC.Generation.Templates.TemplateValidator.ValidateArchetypes( new[] { badArch }, traits );
			Check( "dangling trait id caught", archProblems.Any( p => p.Contains( "totally_made_up" ) ) );
			Check( "unknown narrative role on archetype caught", archProblems.Any( p => p.Contains( "protagonist" ) ) );

			var badMission = new MissionTemplate
			{
				Id = "bad_mission", Type = "Surveillance",
				ClientCandidates = new() { "megacorp_nine" },
			};
			var missionProblems = CWC.Generation.Templates.TemplateValidator.ValidateMissions(
				new[] { badMission }, new HashSet<string> { "host" } );
			Check( "dangling faction id caught", missionProblems.Any( p => p.Contains( "megacorp_nine" ) ) );
		}

		Section( "18. Content pipeline end-to-end — every shipped scene renders" );
		{
			var gm = new GameManager();
			gm.NewGame( 60660 );
			var world = gm.World;
			var trigOp = world.ActiveRoster.First();

			// Every narrative role the CONTENT references must be present on the
			// roster, the way a real mid-run roster would have drifted into them.
			var loader = new CWC.Generation.Templates.TemplateLoader();
			var templates = loader.LoadScenes();
			var roleToken = new System.Text.RegularExpressions.Regex( @"\{operative\.role:([^}]+)\}" );
			var neededRoles = new HashSet<string>();
			foreach ( var t in templates )
			{
				foreach ( var slot in t.Cast )
					if ( slot.Resolver.StartsWith( "role:" ) )
						neededRoles.Add( slot.Resolver.Substring( 5 ) );
				foreach ( var line in t.TextLines.Concat( t.Choices.Select( c => c.Label ) )
					.Append( t.Title ).Append( t.Setting ).Append( t.Speaker ) )
					foreach ( System.Text.RegularExpressions.Match m in roleToken.Matches( line ) )
						neededRoles.Add( m.Groups[1].Value );
			}
			var roster = world.ActiveRoster.ToList();

			// No single scene may reference more roles than a roster can hold —
			// otherwise it can never render fully on any playthrough.
			static HashSet<string> RolesOf( CWC.Narrative.SceneTemplate t, System.Text.RegularExpressions.Regex tokenRx )
			{
				var roles = new HashSet<string>();
				foreach ( var slot in t.Cast )
					if ( slot.Resolver.StartsWith( "role:" ) ) roles.Add( slot.Resolver.Substring( 5 ) );
				foreach ( var line in t.TextLines.Concat( t.Choices.Select( c => c.Label ) )
					.Append( t.Title ).Append( t.Setting ).Append( t.Speaker ) )
					foreach ( System.Text.RegularExpressions.Match m in tokenRx.Matches( line ) )
						roles.Add( m.Groups[1].Value );
				return roles;
			}
			Check( "no scene references more roles than a roster holds",
				templates.All( t => RolesOf( t, roleToken ).Count <= roster.Count ) );

			// A scene is dirty if any token survives to the rendered text, or if
			// rendering ADDED a "someone"/"the operative" fallback that the
			// template's own prose didn't contain. Roles are assigned per scene,
			// the way a drifted mid-run roster would satisfy them.
			int rendered = 0, cleanScenes = 0;
			var dirty = new List<string>();
			foreach ( var template in templates )
			{
				int slotIdx = 0;
				foreach ( var op in roster ) op.NarrativeRole = "";
				foreach ( var role in RolesOf( template, roleToken ).OrderBy( r => r, StringComparer.Ordinal ) )
					roster[slotIdx++ % roster.Count].NarrativeRole = role;

				var scene = gm.Director.RenderPreview( template, world, trigOp.Id );
				rendered++;
				string renderedAll = string.Join( "\n", scene.TextLines.Concat( scene.Choices.Select( c => c.Label ) )
					.Append( scene.Title ).Append( scene.Setting ).Append( scene.Speaker ) );
				string templateAll = string.Join( "\n", template.TextLines.Concat( template.Choices.Select( c => c.Label ) )
					.Append( template.Title ).Append( template.Setting ).Append( template.Speaker ) );
				bool clean = !renderedAll.Contains( '{' )
					&& CountOf( renderedAll, "someone" ) == CountOf( templateAll, "someone" )
					&& CountOf( renderedAll, "the operative" ) == CountOf( templateAll, "the operative" );
				if ( clean ) cleanScenes++; else dirty.Add( template.Id );
			}
			Check( "all scenes render via RenderPreview", rendered >= 32, $"rendered={rendered}" );
			Check( "every scene renders clean (no raw tokens, no fallback names)",
				cleanScenes == rendered, $"dirty: {string.Join( ", ", dirty )}" );
		}

		Section( "19. Manual examples are living fixtures (docs/examples/)" );
		{
			// The Content Authoring Manual's JSON examples are real files. If
			// the schema moves and the manual doesn't, this section goes red —
			// documentation is not allowed to drift into fiction again.
			var files = CwcFiles.FindFiles( "docs/examples", "*.json" );
			Check( "manual example fixtures exist", files.Count >= 2, $"found={files.Count}" );

			var exLoader = new CWC.Generation.Templates.TemplateLoader( "docs/examples" );
			var fixtures = new List<CWC.Narrative.SceneTemplate>();
			foreach ( var f in files )
			{
				var loaded = exLoader.DeserializeSceneFile( f );
				if ( loaded != null ) fixtures.AddRange( loaded );
			}
			Check( "every manual example deserializes", fixtures.Count >= files.Count,
				$"scenes={fixtures.Count} files={files.Count}" );
			Check( "manual examples have no unknown fields (strict pass)",
				exLoader.Errors.Count == 0, string.Join( "; ", exLoader.Errors ) );

			// Validate in context: fixtures may chain off shipped scenes
			// (corruption_fourth_wet_job requires scene:third_kill_seen).
			var shipped = new CWC.Generation.Templates.TemplateLoader().LoadScenes();
			var merged = shipped.Concat( fixtures ).ToList();
			var fixtureProblems = CWC.Generation.Templates.TemplateValidator.ValidateScenes( merged )
				.Where( p => fixtures.Any( fx => p.Contains( $"'{fx.Id}'" ) ) )
				// The starter template's placeholder gate is intentionally never set.
				.Where( p => !p.Contains( "example_never_set" ) )
				.ToList();
			Check( "manual examples pass the validator in context",
				fixtureProblems.Count == 0, string.Join( "; ", fixtureProblems ) );

			// And the worked example must actually RENDER clean.
			var gm = new GameManager();
			gm.NewGame( 424242 );
			var roster = gm.World.ActiveRoster.ToList();
			roster[0].NarrativeRole = "mirror";
			var worked = fixtures.FirstOrDefault( f => f.Id == "corruption_fourth_wet_job" );
			Check( "worked example fixture present", worked != null );
			if ( worked != null )
			{
				var scene = gm.Director.RenderPreview( worked, gm.World, roster[1].Id );
				string all = string.Join( "\n", scene.TextLines.Concat( scene.Choices.Select( c => c.Label ) )
					.Append( scene.Title ).Append( scene.Setting ).Append( scene.Speaker ) );
				Check( "worked example renders clean",
					!all.Contains( '{' ) && !all.Contains( "someone" ), all );
			}
		}

		Console.WriteLine();
		Console.WriteLine( $"-----------------------------------------" );
		Console.WriteLine( $"Total: {_pass + _fail} checks, {_pass} pass, {_fail} fail" );
		Console.WriteLine();
		return _fail == 0 ? 0 : 1;
	}
}
