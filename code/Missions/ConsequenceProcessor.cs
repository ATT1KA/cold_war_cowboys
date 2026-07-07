// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Sole writer of WorldState mutations from MissionResults. Tripwires that
/// produce narrative flags (third_kill, cold_blooded, hollowed_out) live here
/// rather than in the Resolver — they need access to the post-mutation state.
/// </summary>
public sealed class ConsequenceProcessor
{
	public void Apply( MissionResult result, WorldState world )
	{
		ApplyOperativeImpacts( result, world );
		ApplySkillGrowth( result, world );
		ApplyWorldConsequences( result, world );
		ApplyMissionStatus( result, world );
		EmitTripwireFlags( result, world );
		AppendNarrativeFlags( result, world );
		ClearAssignments( result, world );
	}

	// Fieldwork teaches. Each op grows the mission's top-weighted skill by +1
	// on success or partial success; nothing from failure or catastrophe.
	// Capped at 95 so mastery stays asymptotic. This is the counterweight to
	// per-cycle difficulty scaling: a worked roster climbs the late-game wall
	// slower than the wall rises — the squeeze is the design.
	private static void ApplySkillGrowth( MissionResult result, WorldState world )
	{
		if ( result.Outcome is MissionOutcome.Failure or MissionOutcome.Catastrophe ) return;
		var mission = world.GetMission( result.MissionId );
		if ( mission == null ) return;

		SkillKind topSkill = SkillKind.Stealth;
		int topWeight = -1;
		foreach ( var kv in MissionWeights.EffectiveWeights( mission ) )
		{
			if ( kv.Value > topWeight ) { topWeight = kv.Value; topSkill = kv.Key; }
		}
		if ( topWeight <= 0 ) return;

		const int gain = 1;
		foreach ( var id in result.AssignedOperativeIds )
		{
			var op = world.GetOperative( id );
			if ( op == null ) continue;
			int current = op.Skills.Get( topSkill );
			if ( current < 95 )
				op.Skills.Add( topSkill, Math.Min( gain, 95 - current ) );
		}
	}

	private static void ApplyOperativeImpacts( MissionResult result, WorldState world )
	{
		foreach ( var (id, impact) in result.PerOperative )
		{
			var op = world.GetOperative( id );
			if ( op == null ) continue;

			op.Psychology.Stress     = Math.Clamp( op.Psychology.Stress + impact.StressDelta, 0, 100 );
			op.Psychology.Loyalty    = Math.Clamp( op.Psychology.Loyalty + impact.LoyaltyDelta, 0, 100 );
			op.Psychology.Morale     = Math.Clamp( op.Psychology.Morale + impact.MoraleDelta, 0, 100 );
			op.Psychology.Conscience = Math.Clamp( op.Psychology.Conscience + impact.ConscienceDelta, 0, 100 );
			op.Psychology.WetWorkCount += impact.WetWorkDelta;

			if ( impact.Killed )
				op.Status = OperativeStatus.Dead;
			else if ( impact.Compromised )
				op.Status = OperativeStatus.Compromised;
			else if ( impact.Injured && op.Status == OperativeStatus.Active )
				op.Status = OperativeStatus.Injured;

			ApplyTeammateRelationshipDrift( op.Id, result, world );
		}
	}

	// Shared trauma / blame between teammates. Catastrophe makes the team
	// blame each other; Success tightens bonds slightly.
	private static void ApplyTeammateRelationshipDrift( int opId, MissionResult result, WorldState world )
	{
		int drift = result.Outcome switch
		{
			MissionOutcome.Success         => 3,
			MissionOutcome.PartialSuccess  => 1,
			MissionOutcome.Failure         => -2,
			MissionOutcome.Catastrophe     => -5,
			_                              => 0,
		};
		if ( drift == 0 ) return;

		foreach ( var otherId in result.AssignedOperativeIds )
		{
			if ( otherId == opId ) continue;
			var rel = world.GetRelationshipBetween( opId, otherId );
			if ( rel == null )
			{
				world.Relationships.Add( new Relationship
				{
					FromId = opId,
					ToId = otherId,
					Kind = drift > 0 ? RelationshipKind.Friend : RelationshipKind.Rival,
					Score = drift,
				} );
			}
			else
			{
				rel.Score = Math.Clamp( rel.Score + drift, -100, 100 );
			}
		}
	}

	private static void ApplyWorldConsequences( MissionResult result, WorldState world )
	{
		foreach ( var c in result.Consequences )
		{
			switch ( c.Kind )
			{
				case ConsequenceKind.HeatChange:
					world.Corporate.Heat = Math.Clamp( world.Corporate.Heat + c.IntValue, 0, 100 );
					break;
				case ConsequenceKind.SuspicionChange:
					world.Corporate.Suspicion = Math.Clamp( world.Corporate.Suspicion + c.IntValue, 0, 100 );
					break;
				case ConsequenceKind.ReputationChange:
					world.Corporate.Reputation = Math.Clamp( world.Corporate.Reputation + c.IntValue, 0, 100 );
					break;
				case ConsequenceKind.BudgetChange:
					world.Corporate.Budget += c.IntValue;
					break;
				case ConsequenceKind.FactionStandingChange:
					var faction = world.GetFaction( c.StringValue );
					if ( faction != null )
						faction.Standing = Math.Clamp( faction.Standing + c.IntValue, -100, 100 );
					break;
				case ConsequenceKind.OperativeStatus:
					var op = world.GetOperative( c.OperativeId );
					if ( op != null && Enum.TryParse<OperativeStatus>( c.StringValue, out var s ) )
						op.Status = s;
					break;
				case ConsequenceKind.NarrativeFlag:
					world.NarrativeFlags.Add( c.StringValue );
					break;
			}
		}
	}

	private static void ApplyMissionStatus( MissionResult result, WorldState world )
	{
		var mission = world.GetMission( result.MissionId );
		if ( mission == null ) return;
		mission.Status = result.Outcome switch
		{
			MissionOutcome.Success         => MissionStatus.Completed,
			MissionOutcome.PartialSuccess  => MissionStatus.Completed,
			MissionOutcome.Failure         => MissionStatus.Failed,
			MissionOutcome.Catastrophe     => MissionStatus.Failed,
			_                              => MissionStatus.Failed,
		};
	}

	// Tripwires that depend on post-mutation operative state.
	private static void EmitTripwireFlags( MissionResult result, WorldState world )
	{
		foreach ( var id in result.AssignedOperativeIds )
		{
			var op = world.GetOperative( id );
			if ( op == null ) continue;

			int wet = op.Psychology.WetWorkCount;
			if ( wet == 3 ) world.NarrativeFlags.Add( $"third_kill:{id}" );
			if ( wet >= 7 ) world.NarrativeFlags.Add( $"cold_blooded:{id}" );

			if ( op.Psychology.Conscience <= 15 )
				world.NarrativeFlags.Add( $"hollowed_out:{id}" );
			if ( op.Psychology.Stress >= 90 )
				world.NarrativeFlags.Add( $"breaking_point:{id}" );
			if ( op.Psychology.Loyalty <= 20 )
				world.NarrativeFlags.Add( $"defection_risk:{id}" );
		}

		if ( world.Corporate.Heat >= 80 ) world.NarrativeFlags.Add( "heat:critical" );
		if ( world.Corporate.Suspicion >= 80 ) world.NarrativeFlags.Add( "suspicion:critical" );
	}

	private static void AppendNarrativeFlags( MissionResult result, WorldState world )
	{
		foreach ( var f in result.NarrativeFlags )
			world.NarrativeFlags.Add( f );
	}

	private static void ClearAssignments( MissionResult result, WorldState world )
	{
		foreach ( var id in result.AssignedOperativeIds )
		{
			var op = world.GetOperative( id );
			if ( op != null ) op.CurrentMissionId = null;
		}
		var mission = world.GetMission( result.MissionId );
		if ( mission != null ) mission.AssignedOperativeIds.Clear();
	}
}
