// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Hybrid resolver. Skills primary, psychology penalty, relationship swing, RNG.
///
/// score = SkillContribution - PsychologyPenalty + RelationshipSwing + RngSwing
/// outcome by margin against Difficulty (dice span ±18 so every band is
/// genuinely reachable from a matched fight):
///   margin >= +8  -> Success        (~30% at matched skill/difficulty)
///   margin >= -12 -> PartialSuccess
///   margin >= -30 -> Failure
///   else          -> Catastrophe
///
/// Skill weights come from <see cref="MissionWeights"/> — the same source the
/// UI fit hints use, so the picker and the resolver can't drift apart.
///
/// Resolver is pure: builds MissionResult only. ConsequenceProcessor applies it.
/// </summary>
public sealed class MissionResolver
{
	/// <summary>
	/// Margin needed for full success. Public so UI fit displays can be honest
	/// about what "a good fit" actually means at resolution time.
	/// </summary>
	public const int SuccessMargin = 8;

	/// <summary>
	/// Resolve with default mission parameters (no narrative overrides).
	/// </summary>
	public MissionResult Resolve( Mission mission, WorldState world, Rng rng )
		=> Resolve( mission, world, rng, null );

	/// <summary>
	/// Resolve with optional narrative overrides from MissionNarrativeRunner.
	/// Overrides modify skill weights and difficulty based on player choices.
	/// </summary>
	public MissionResult Resolve( Mission mission, WorldState world, Rng rng, NarrativeOverrides? overrides )
	{
		var ops = mission.AssignedOperativeIds
			.Select( world.GetOperative )
			.Where( o => o != null )!
			.Cast<Operative>()
			.ToList();

		// Hostile factions bury real risk in the fine print. Parsed here so the
		// tags actually cost something.
		int hiddenRisk = ParseTag( mission, "hidden_risk" );
		int hiddenExposure = ParseTag( mission, "hidden_exposure" );

		int difficulty = Math.Clamp(
			mission.Difficulty + hiddenRisk + ( overrides?.DifficultyDelta ?? 0 ), 5, 95 );

		var result = new MissionResult
		{
			MissionId = mission.Id,
			Target = difficulty,
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

		// Apply narrative overrides to mission parameters before resolution
		var effectiveMission = mission;
		if ( overrides != null && ( overrides.SkillWeightDeltas.Count > 0 || overrides.ForcedWetWork ) )
		{
			var adjusted = MissionWeights.EffectiveWeights( mission )
				.ToDictionary( kv => kv.Key, kv => kv.Value );
			foreach ( var (sk, delta) in overrides.SkillWeightDeltas )
			{
				adjusted.TryGetValue( sk, out var current );
				adjusted[sk] = Math.Max( 0, current + delta );
			}
			effectiveMission = new Mission
			{
				Id = mission.Id, TemplateId = mission.TemplateId, Type = mission.Type,
				Status = mission.Status, Title = mission.Title, Briefing = mission.Briefing,
				Difficulty = mission.Difficulty, MoralWeight = mission.MoralWeight,
				IsWetWork = mission.IsWetWork || overrides.ForcedWetWork,
				StatWeights = adjusted,
				AssignedOperativeIds = mission.AssignedOperativeIds,
				SuccessText = mission.SuccessText, PartialText = mission.PartialText,
				FailureText = mission.FailureText, CatastropheText = mission.CatastropheText,
			};
		}

		int skill = ComputeSkill( effectiveMission, ops );
		int psych = ComputePsychPenalty( ops );
		int relSwing = ComputeRelationshipSwing( ops, world, rng );
		int rngSwing = rng.Next( -18, 19 );

		int score = skill - psych + relSwing + rngSwing;
		var outcome = ClassifyOutcome( score, difficulty );

		result.SkillContribution = skill;
		result.PsychologyPenalty = psych;
		result.RelationshipSwing = relSwing;
		result.RngSwing = rngSwing;
		result.Score = score;
		result.Outcome = outcome;
		result.NarrativeText = BuildNarrative( effectiveMission, world, ops, outcome );

		AddFlags( result, outcome switch
		{
			MissionOutcome.Success         => mission.NarrativeFlagsOnSuccess,
			MissionOutcome.PartialSuccess  => mission.NarrativeFlagsOnPartialSuccess,
			MissionOutcome.Failure         => mission.NarrativeFlagsOnFailure,
			MissionOutcome.Catastrophe     => mission.NarrativeFlagsOnCatastrophe,
			_                              => new List<string>(),
		} );

		BuildPerOperativeImpact( result, effectiveMission, ops, rng );
		BuildWorldConsequences( result, effectiveMission, ops, outcome, hiddenExposure );

		return result;
	}

	private static int ParseTag( Mission mission, string prefix )
	{
		foreach ( var tag in mission.Tags )
		{
			if ( !tag.StartsWith( prefix + ":" ) ) continue;
			if ( int.TryParse( tag.Substring( prefix.Length + 1 ), out var v ) ) return v;
		}
		return 0;
	}

	// Per-required-skill team max with effectiveness penalty for non-leads.
	// effective = max(skill_i * effectiveness_i) where effectiveness = 1 - psychPenalty/100.
	// Result then weighted by the mission's effective weights (MissionWeights).
	private static int ComputeSkill( Mission mission, List<Operative> ops )
	{
		double weightedSum = 0;
		int weightTotal = 0;

		foreach ( var (kind, weight) in MissionWeights.EffectiveWeights( mission ) )
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
		if ( margin >= SuccessMargin ) return MissionOutcome.Success;
		if ( margin >= -12 ) return MissionOutcome.PartialSuccess;
		if ( margin >= -30 ) return MissionOutcome.Failure;
		return MissionOutcome.Catastrophe;
	}

	private static string BuildNarrative( Mission mission, WorldState world, List<Operative> ops, MissionOutcome outcome )
	{
		// Authored template text first; stock lines as fallback.
		string template = outcome switch
		{
			MissionOutcome.Success         => string.IsNullOrEmpty( mission.SuccessText )
				? "Clean exit. {leader} signs off, the others fall in behind." : mission.SuccessText,
			MissionOutcome.PartialSuccess  => string.IsNullOrEmpty( mission.PartialText )
				? "Done, but rough. {leader} reports complications. Cleanup will cost." : mission.PartialText,
			MissionOutcome.Failure         => string.IsNullOrEmpty( mission.FailureText )
				? "Aborted. The team falls back. {leader}'s after-action report is short and unhappy." : mission.FailureText,
			MissionOutcome.Catastrophe     => string.IsNullOrEmpty( mission.CatastropheText )
				? "It went sideways. {leader} barely got the team out. Someone's going to ask questions." : mission.CatastropheText,
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
				impact.MoraleDelta -= 3; // dirty jobs wear, whatever the outcome
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
	// Contracts pay their stated Reward — the economy is real. Wet work pays a
	// premium (set at generation) but triples the heat: that trade is the game.
	private static void BuildWorldConsequences( MissionResult result, Mission mission, List<Operative> ops, MissionOutcome outcome, int hiddenExposure )
	{
		int heatDelta = 0, suspDelta = 0, repDelta = 0, budgetDelta = 0;
		int payout = mission.Reward > 0 ? mission.Reward : 12_000;

		switch ( outcome )
		{
			case MissionOutcome.Success:
				heatDelta = mission.IsWetWork ? 3 : 1;
				repDelta = 2;
				budgetDelta = payout;
				break;
			case MissionOutcome.PartialSuccess:
				heatDelta = mission.IsWetWork ? 6 : 3;
				repDelta = 0;
				budgetDelta = payout / 2;
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

		// Hidden exposure from poisoned contracts costs suspicion whatever the
		// outcome — you took the job before reading the fine print.
		suspDelta += hiddenExposure / 3;

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
}
