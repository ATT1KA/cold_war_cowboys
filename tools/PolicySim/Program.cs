using System;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Game;
using CWC.Missions;

namespace CWC.PolicySim;

/// <summary>
/// Balance harness that drives the REAL game loop (GameManager, resolver,
/// corporate layer, narrative director) through 20-cycle runs under two
/// scripted management policies, and reports the numbers the design cares
/// about. Replaces tools/balance_test.py, whose hand-mirrored resolver had
/// drifted ~15 points from the game and validated nothing.
///
/// Policies:
///   HUMANE   — rotate stressed operatives, skip wet work, pick humane choices.
///   RUTHLESS — take every wet contract first, work everyone, pick cold choices.
///
/// Design targets this harness watches:
///   • All four outcomes occur at meaningful rates; late game is harder
///     (success rate drops by cycle 14+) but not impossible.
///   • HUMANE stays below the Effective corruption threshold and keeps its
///     people alive — kindness pays in resilience.
///   • RUTHLESS reaches The Machine in a meaningful share of runs (the two
///     best scenes in the game must be reachable), and pays for it in morale
///     and bodies.
/// </summary>
internal static class Program
{
	private const int Cycles = 20;
	private const int Seeds = 40;

	public static int Main()
	{
		CwcFiles.Provider = new CWC.SmokeTest.DiskFileProvider();
		CwcLog.Sink = null;

		Console.WriteLine( $"CWC policy simulation — {Seeds} seeds × {Cycles} cycles per policy" );
		Console.WriteLine();

		bool ok = true;
		var humane = RunPolicy( ruthless: false );
		var ruthless = RunPolicy( ruthless: true );

		ok &= Expect( "all four outcomes occur (humane)",
			humane.Succ > 0 && humane.Part > 0 && humane.Fail > 0 && humane.Cat > 0 );
		ok &= Expect( "late game is harder than early game (humane)",
			humane.LateSuccessRate < humane.SuccessRate + 0.01 );
		ok &= Expect( "late game is not a dead wall (humane success >= 15%)",
			humane.LateSuccessRate >= 0.15, $"{humane.LateSuccessRate:P0}" );
		ok &= Expect( "humane play stays under Effective (corr < 40)",
			humane.AvgCorruption < 40, $"{humane.AvgCorruption:F0}" );
		ok &= Expect( "humane play keeps people alive (< 0.5 deaths/run)",
			humane.DeathsPerRun < 0.5, $"{humane.DeathsPerRun:F2}" );
		ok &= Expect( "ruthless play reaches The Machine in >= 20% of runs",
			ruthless.MachineRuns >= Seeds * 0.2, $"{ruthless.MachineRuns}/{Seeds}" );
		ok &= Expect( "Jenkins is reachable",
			ruthless.JenkinsRuns >= 1, $"{ruthless.JenkinsRuns}/{Seeds}" );
		ok &= Expect( "cruelty costs morale (ruthless < humane)",
			ruthless.AvgMorale < humane.AvgMorale,
			$"{ruthless.AvgMorale:F0} vs {humane.AvgMorale:F0}" );
		ok &= Expect( "cruelty costs bodies (ruthless deaths > humane)",
			ruthless.DeathsPerRun > humane.DeathsPerRun );

		Console.WriteLine();
		Console.WriteLine( ok ? "POLICY SIM: PASS" : "POLICY SIM: FAIL" );
		return ok ? 0 : 1;
	}

	private static bool Expect( string label, bool condition, string? detail = null )
	{
		Console.WriteLine( $"  {(condition ? "PASS" : "FAIL")}  {label}{(detail == null ? "" : $"  ({detail})")}" );
		return condition;
	}

	private sealed class PolicyStats
	{
		public int Succ, Part, Fail, Cat, LateSucc, LateTotal, Deaths, MachineRuns, JenkinsRuns;
		public double CorrSum, MoraleSum;
		public double SuccessRate => (double)Succ / Math.Max( 1, Succ + Part + Fail + Cat );
		public double LateSuccessRate => (double)LateSucc / Math.Max( 1, LateTotal );
		public double AvgCorruption => CorrSum / Seeds;
		public double DeathsPerRun => (double)Deaths / Seeds;
		public double AvgMorale => MoraleSum / Seeds;
	}

	private static PolicyStats RunPolicy( bool ruthless )
	{
		var stats = new PolicyStats();

		for ( ulong seed = 1; seed <= Seeds; seed++ )
		{
			var gm = new GameManager();
			gm.NewGame( seed * 1337 );
			var w = gm.World;

			for ( int cyc = 0; cyc < Cycles; cyc++ )
			{
				gm.AdvancePhase(); // → Assignment

				var avail = w.Missions.Where( m => m.Status == MissionStatus.Available )
					.OrderByDescending( m => ruthless && m.IsWetWork ? 1 : 0 )
					.ThenByDescending( m => m.Reward )
					.ToList();
				var pool = w.ActiveRoster
					.OrderByDescending( o => o.Skills.Combat + o.Skills.Stealth + o.Skills.Hacking )
					.ToList();

				foreach ( var m in avail.Take( ruthless ? 3 : 2 ) )
				{
					if ( !ruthless && m.IsWetWork ) continue;
					foreach ( var candidate in pool.Where( o => o.IsAvailable && (ruthless || o.Psychology.Stress < 55) ) )
						if ( gm.Assign( candidate.Id, m.Id ) ) break; // walk past refusals
				}

				// Lock in; play any narrative sequence the runner ignites.
				int guard = 0;
				while ( gm.Phase.CurrentPhase == CyclePhase.Assignment && guard++ < 30 )
				{
					if ( gm.NarrativeRunner.IsActive )
					{
						var offered = gm.NarrativeRunner.CurrentChoices;
						var choice = ruthless
							? offered.OrderByDescending( c => c.WetWork ? 1 : 0 ).First()
							: offered.OrderBy( c => c.WetWork ? 1 : 0 ).First();
						gm.NarrativeRunner.ApplyChoice( choice, w );
					}
					else gm.AdvancePhase();
				}

				foreach ( var r in gm.LastResolutionResults )
				{
					bool late = cyc >= 13;
					switch ( r.Outcome )
					{
						case MissionOutcome.Success: stats.Succ++; if ( late ) stats.LateSucc++; break;
						case MissionOutcome.PartialSuccess: stats.Part++; break;
						case MissionOutcome.Failure: stats.Fail++; break;
						case MissionOutcome.Catastrophe: stats.Cat++; break;
					}
					if ( late ) stats.LateTotal++;
				}

				gm.AdvancePhase(); // Corporate
				gm.AdvancePhase(); // Aftermath — scenes queue

				var sc = gm.Director.PopNextScene( w );
				int sguard = 0;
				while ( sc != null && sguard++ < 5 )
				{
					if ( sc.Choices.Count > 0 )
					{
						var ch = ruthless
							? sc.Choices.OrderBy( c => c.ConscienceDelta ).First()
							: sc.Choices.OrderByDescending( c => c.ConscienceDelta ).First();
						gm.Director.ApplyChoice( sc, ch, w );
					}
					sc = gm.Director.PopNextScene( w );
				}

				gm.AdvancePhase(); // Review
				gm.AdvancePhase(); // Briefing (new cycle)
			}

			stats.Deaths += w.Operatives.Count( o => !o.IsExecutive && o.Status == OperativeStatus.Dead );
			stats.CorrSum += w.Corruption.CorruptionIndex;
			if ( w.Corruption.CrossedEver.Contains( CorruptionTracker.Milestone.TheMachine ) ) stats.MachineRuns++;
			if ( w.Corruption.CrossedEver.Contains( CorruptionTracker.Milestone.Jenkins ) ) stats.JenkinsRuns++;
			var act = w.ActiveRoster.ToList();
			if ( act.Count > 0 ) stats.MoraleSum += act.Average( o => o.Psychology.Morale );
		}

		int total = stats.Succ + stats.Part + stats.Fail + stats.Cat;
		Console.WriteLine(
			$"{(ruthless ? "RUTHLESS" : "HUMANE  ")}: missions={total} " +
			$"S={100.0 * stats.Succ / total:F0}% P={100.0 * stats.Part / total:F0}% " +
			$"F={100.0 * stats.Fail / total:F0}% C={100.0 * stats.Cat / total:F0}% | " +
			$"late S={100 * stats.LateSuccessRate:F0}% | deaths/run={stats.DeathsPerRun:F2} | " +
			$"corr={stats.AvgCorruption:F0} Machine={stats.MachineRuns}/{Seeds} Jenkins={stats.JenkinsRuns}/{Seeds} | " +
			$"morale={stats.AvgMorale:F0}" );
		return stats;
	}
}
