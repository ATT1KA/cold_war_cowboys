using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CWC.Core;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Hybrid resolver. Skills primary, psychology penalty, relationship swing, RNG.
///
/// score = SkillContribution - PsychologyPenalty + RelationshipSwing + RngSwing
/// outcome by margin against Difficulty:
///   margin >= +15 -> Success
///   margin >=  -5 -> PartialSuccess
///   margin >= -25 -> Failure
///   else          -> Catastrophe
///
/// Resolver is pure: builds MissionResult only. ConsequenceProcessor applies it.
/// </summary>
public sealed class MissionResolver
{
	public MissionResult Resolve( Mission mission, WorldState world, Rng rng )
	{
		var ops = mission.AssignedOperativeIds
			.Select( world.GetOperative )
			.Where( o => o != null )!
			.Cast<Operative>()
			.ToList();

		var result = new MissionResult
		{
			MissionId = mission.Id,
			Target = mission.Difficulty,
			AssignedOperativeIds = mission.AssignedOperativeIds.ToList(),
		};

		if ( ops.Count == 0 )
		{
			// No operatives — auto-catastrophe.
			result.Outcome = MissionOutcome.Catastrophe;
			result.Score = 0;
			result.NarrativeText = "No operatives assigned. The contract goes dark.";
			AddFlags( result, mission.NarrativeFlagsOnCatastrophe );
			return result;
		}

		int skill = ComputeSkill( mission, ops );
		int psych = ComputePsychPenalty( ops );
		int relSwing = ComputeRelationshipSwing( ops, world, rng );
		int rngSwing = rng.Next( -15, 16 );

		int score = skill - psych + relSwing + rngSwing;
		var outcome = ClassifyOutcome( score, mission.Difficulty );

		result.SkillContribution = skill;
		result.PsychologyPenalty = psych;
		result.RelationshipSwing = relSwing;
		result.RngSwing = rngSwing;
		result.Score = score;
		result.Outcome = outcome;
		result.NarrativeText = BuildNarrative( mission, world, ops, outcome );

		AddFlags( result, outcome switch
		{
			MissionOutcome.Success         => mission.NarrativeFlagsOnSuccess,
			MissionOutcome.PartialSuccess  => mission.NarrativeFlagsOnPartialSuccess,
			MissionOutcome.Failure         => mission.NarrativeFlagsOnFailure,
			MissionOutcome.Catastrophe     => mission.NarrativeFlagsOnCatastrophe,
			_                              => new List<string>(),
		} );

		BuildPerOperativeImpact( result, mission, ops, rng );
		BuildWorldConsequences( result, mission, ops, outcome );

		return result;
	}

	// Per-required-skill team max with effectiveness penalty for non-leads.
	// effective = max(skill_i * effectiveness_i) where effectiveness = 1 - psychPenalty/100.
	// Result then weighted by mission.StatWeights.
	private static int ComputeSkill( Mission mission, List<Operative> ops )
	{
		double weightedSum = 0;
		int weightTotal = 0;

		var weights = mission.StatWeights.Count > 0
			? mission.StatWeights
			: DefaultWeightsFor( mission.Type );

		foreach ( var (kind, weight) in weights )
		{
			if ( weight <= 0 ) continue;
			int best = 0;
			foreach ( var op in ops )
			{
				int raw = op.Skills.Get( kind );
				double eff = 1.0 - PsychPenaltyFor( op ) / 100.0;
				int adjusted = (int)Math.Round( raw * Math.Clamp( eff, 0.5, 1.0 ) );
				if ( adjusted > best ) best = adjusted;
			}
			weightedSum += best * weight;
			weightTotal += weight;
		}

		if ( weightTotal == 0 ) return 50;
		return (int)Math.Round( weightedSum / weightTotal );
	}

	// Up to 25pt penalty per operative; capped collectively.
	private static int ComputePsychPenalty( List<Operative> ops )
	{
		int total = 0;
		foreach ( var op in ops ) total += PsychPenaltyFor( op );
		return Math.Min( total / Math.Max( 1, ops.Count ), 25 );
	}

	private static int PsychPenaltyFor( Operative op )
	{
		int p = 0;
		if ( op.Psychology.Stress > 60 ) p += (op.Psychology.Stress - 60) / 4;     // up to 10
		if ( op.Psychology.Morale < 40 ) p += (40 - op.Psychology.Morale) / 4;     // up to 10
		if ( op.Psychology.Loyalty < 40 ) p += (40 - op.Psychology.Loyalty) / 4;   // up to 10
		return Math.Min( p, 25 );
	}

	// ±12. Friends/confidants/lovers help; rivals hurt. Mentor/protégé small bonus.
	private static int ComputeRelationshipSwing( List<Operative> ops, WorldState world, Rng rng )
	{
		if ( ops.Count < 2 ) return 0;

		int bond = 0;
		int rivalry = 0;

		for ( int i = 0; i < ops.Count; i++ )
		{
			for ( int j = 0; j < ops.Count; j++ )
			{
				if ( i == j ) continue;
				var rel = world.GetRelationshipBetween( ops[i].Id, ops[j].Id );
				if ( rel == null ) continue;

				switch ( rel.Kind )
				{
					case RelationshipKind.Friend:
					case RelationshipKind.Confidant:
					case RelationshipKind.Lover:
					case RelationshipKind.Mentor:
					case RelationshipKind.Protege:
						bond += Math.Max( 0, rel.Score );
						break;
					case RelationshipKind.Rival:
						rivalry += Math.Max( 0, -rel.Score );
						break;
					default:
						break;
				}
			}
		}

		int swing = (bond / 30) - (rivalry / 25);
		// Small jitter so the same team doesn't synergise identically each run.
		swing += rng.Next( -2, 3 );
		return Math.Clamp( swing, -12, 12 );
	}

	private static MissionOutcome ClassifyOutcome( int score, int difficulty )
	{
		int margin = score - difficulty;
		if ( margin >= 15 ) return MissionOutcome.Success;
		if ( margin >= -5 ) return MissionOutcome.PartialSuccess;
		if ( margin >= -25 ) return MissionOutcome.Failure;
		return MissionOutcome.Catastrophe;
	}

	private static string BuildNarrative( Mission mission, WorldState world, List<Operative> ops, MissionOutcome outcome )
	{
		string template = outcome switch
		{
			MissionOutcome.Success         => "Clean exit. {leader} signs off, the others fall in behind.",
			MissionOutcome.PartialSuccess  => "Done, but rough. {leader} reports complications. Cleanup will cost.",
			MissionOutcome.Failure         => "Aborted. The team falls back. {leader}'s after-action report is short and unhappy.",
			MissionOutcome.Catastrophe     => "It went sideways. {leader} barely got the team out. Someone's going to ask questions.",
			_                              => "",
		};

		var leader = ops.OrderByDescending( o => o.Skills.Combat + o.Skills.Stealth ).First();
		string leaderName = !string.IsNullOrEmpty( leader.Codename ) ? leader.Codename : leader.Name;
		return template.Replace( "{leader}", leaderName );
	}

	// Per-operative impacts. Stress always rises; conscience erodes on wet work
	// regardless of outcome; loyalty/morale move with outcome.
	private static void BuildPerOperativeImpact( MissionResult result, Mission mission, List<Operative> ops, Rng rng )
	{
		foreach ( var op in ops )
		{
			var impact = new OperativeImpact();
			switch ( result.Outcome )
			{
				case MissionOutcome.Success:
					impact.StressDelta = 5;
					impact.MoraleDelta = 8;
					impact.LoyaltyDelta = 2;
					break;
				case MissionOutcome.PartialSuccess:
					impact.StressDelta = 10;
					impact.MoraleDelta = 0;
					break;
				case MissionOutcome.Failure:
					impact.StressDelta = 18;
					impact.MoraleDelta = -10;
					impact.LoyaltyDelta = -3;
					break;
				case MissionOutcome.Catastrophe:
					impact.StressDelta = 28;
					impact.MoraleDelta = -20;
					impact.LoyaltyDelta = -8;
					if ( rng.Chance( 0.65 ) ) impact.Injured = true;
					if ( rng.Chance( 0.06 ) ) impact.Killed = true;
					break;
			}

			if ( mission.IsWetWork )
			{
				impact.WetWorkDelta = 1;
				impact.ConscienceDelta = -7;
				impact.StressDelta += 5;
			}
			else if ( mission.MoralWeight >= 30 )
			{
				impact.ConscienceDelta = -3;
			}

			if ( result.Outcome == MissionOutcome.Catastrophe && mission.IsWetWork )
			{
				impact.ConscienceDelta -= 5;
			}

			result.PerOperative[op.Id] = impact;
		}
	}

	// World-level consequences (heat, suspicion, budget, faction standing).
	private static void BuildWorldConsequences( MissionResult result, Mission mission, List<Operative> ops, MissionOutcome outcome )
	{
		int heatDelta = 0, suspDelta = 0, repDelta = 0, budgetDelta = 0;

		switch ( outcome )
		{
			case MissionOutcome.Success:
				heatDelta = mission.IsWetWork ? 3 : 1;
				repDelta = 2;
				budgetDelta = 12_000;
				break;
			case MissionOutcome.PartialSuccess:
				heatDelta = mission.IsWetWork ? 6 : 3;
				repDelta = 0;
				budgetDelta = 6_000;
				break;
			case MissionOutcome.Failure:
				heatDelta = 4;
				suspDelta = 5;
				repDelta = -3;
				budgetDelta = -8_000;
				break;
			case MissionOutcome.Catastrophe:
				heatDelta = mission.IsWetWork ? 18 : 10;
				suspDelta = 12;
				repDelta = -8;
				budgetDelta = -20_000;
				if ( mission.IsWetWork )
					result.NarrativeFlags.Add( "heat:body_found" );
				break;
		}

		result.Consequences.Add( new MissionConsequence { Kind = ConsequenceKind.HeatChange, IntValue = heatDelta } );
		result.Consequences.Add( new MissionConsequence { Kind = ConsequenceKind.SuspicionChange, IntValue = suspDelta } );
		result.Consequences.Add( new MissionConsequence { Kind = ConsequenceKind.ReputationChange, IntValue = repDelta } );
		result.Consequences.Add( new MissionConsequence { Kind = ConsequenceKind.BudgetChange, IntValue = budgetDelta } );

		if ( !string.IsNullOrEmpty( mission.TargetFactionId ) )
		{
			int standingDelta = outcome switch
			{
				MissionOutcome.Success         => -8,
				MissionOutcome.PartialSuccess  => -4,
				MissionOutcome.Failure         => 2,
				MissionOutcome.Catastrophe     => 6,
				_                              => 0,
			};
			result.Consequences.Add( new MissionConsequence
			{
				Kind = ConsequenceKind.FactionStandingChange,
				IntValue = standingDelta,
				StringValue = mission.TargetFactionId,
			} );
		}
		if ( !string.IsNullOrEmpty( mission.ClientFactionId ) )
		{
			int clientDelta = outcome switch
			{
				MissionOutcome.Success         => 6,
				MissionOutcome.PartialSuccess  => 2,
				MissionOutcome.Failure         => -4,
				MissionOutcome.Catastrophe     => -8,
				_                              => 0,
			};
			result.Consequences.Add( new MissionConsequence
			{
				Kind = ConsequenceKind.FactionStandingChange,
				IntValue = clientDelta,
				StringValue = mission.ClientFactionId,
			} );
		}
	}

	private static void AddFlags( MissionResult r, IEnumerable<string> flags )
	{
		foreach ( var f in flags ) r.NarrativeFlags.Add( f );
	}

	// Default weights when a template/mission doesn't specify any. Coarse but
	// keeps Resolver functional for synthetic missions in tests.
	private static IEnumerable<KeyValuePair<SkillKind, int>> DefaultWeightsFor( MissionType type )
	{
		var d = new Dictionary<SkillKind, int>();
		switch ( type )
		{
			case MissionType.Extraction:
				d[SkillKind.Stealth] = 50; d[SkillKind.Combat] = 30; d[SkillKind.Persuasion] = 20; break;
			case MissionType.Sabotage:
				d[SkillKind.Stealth] = 40; d[SkillKind.Hacking] = 30; d[SkillKind.Combat] = 30; break;
			case MissionType.Surveillance:
				d[SkillKind.Stealth] = 50; d[SkillKind.Hacking] = 30; d[SkillKind.Deception] = 20; break;
			case MissionType.Assassination:
				d[SkillKind.Combat] = 50; d[SkillKind.Stealth] = 30; d[SkillKind.Intimidation] = 20; break;
			case MissionType.DataTheft:
				d[SkillKind.Hacking] = 60; d[SkillKind.Stealth] = 30; d[SkillKind.Deception] = 10; break;
			case MissionType.CounterIntel:
				d[SkillKind.Deception] = 40; d[SkillKind.Hacking] = 30; d[SkillKind.Persuasion] = 30; break;
		}
		return d;
	}
}
