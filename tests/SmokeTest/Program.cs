using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Game;

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

	public static int Main()
	{
		Console.WriteLine( "CWC Sprint 1 Foundation — smoke test" );

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
			Check( "default psychology in expected band",
				gm.World.Operatives.All( o => o.Psychology.Conscience is >= 50 and <= 100 ) );
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
			gm.World.Operatives[1].Psychology.Stress = 80;
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

		Console.WriteLine();
		Console.WriteLine( $"-----------------------------------------" );
		Console.WriteLine( $"Total: {_pass + _fail} checks, {_pass} pass, {_fail} fail" );
		Console.WriteLine();
		return _fail == 0 ? 0 : 1;
	}
}
