using System;
using System.Collections.Generic;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Single source of truth for "what skills does this mission need, and how
/// well does an operative fit it?" Used by:
///   • <see cref="MissionResolver"/> when computing the team-max skill
///     contribution at resolve time.
///   • UI presenters (e.g. the mission picker) so the player sees fit hints
///     before committing an assignment.
///
/// Keeping the weight resolution and fit math in one place prevents the
/// picker hint and the resolver from drifting out of sync — a common bug
/// class when the same domain rule lives in two files.
/// </summary>
public static class MissionWeights
{
	/// <summary>Default skill weights per mission type (used when a Mission has no explicit StatWeights).</summary>
	public static IReadOnlyDictionary<SkillKind, int> DefaultsFor( MissionType type ) => type switch
	{
		MissionType.Extraction =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Stealth, 50 }, { SkillKind.Combat, 30 }, { SkillKind.Persuasion, 20 } },
		MissionType.Sabotage =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Stealth, 40 }, { SkillKind.Hacking, 30 }, { SkillKind.Combat, 30 } },
		MissionType.Surveillance =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Stealth, 50 }, { SkillKind.Hacking, 30 }, { SkillKind.Deception, 20 } },
		MissionType.Assassination =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Combat, 50 }, { SkillKind.Stealth, 30 }, { SkillKind.Intimidation, 20 } },
		MissionType.DataTheft =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Hacking, 60 }, { SkillKind.Stealth, 30 }, { SkillKind.Deception, 10 } },
		MissionType.CounterIntel =>
			new Dictionary<SkillKind, int> {
				{ SkillKind.Deception, 40 }, { SkillKind.Hacking, 30 }, { SkillKind.Persuasion, 30 } },
		_ => new Dictionary<SkillKind, int>(),
	};

	/// <summary>Returns the weights actually used to score a mission — explicit if set, else defaults.</summary>
	public static IReadOnlyDictionary<SkillKind, int> EffectiveWeights( Mission m )
		=> m.StatWeights.Count > 0 ? m.StatWeights : DefaultsFor( m.Type );

	/// <summary>
	/// Solo fit score 0..100 — what this single operative would contribute to the
	/// mission, weighted by the mission's required skills, with a light psych
	/// modifier so a stressed/demoralised op shows lower fit. Pure presentation
	/// helper; does NOT mutate state.
	/// </summary>
	public static int FitScore( Operative op, Mission m )
	{
		var weights = EffectiveWeights( m );
		if ( weights.Count == 0 ) return 50;

		int weightedSum = 0;
		int totalWeight = 0;
		foreach ( var kv in weights )
		{
			if ( kv.Value <= 0 ) continue;
			weightedSum += op.Skills.Get( kv.Key ) * kv.Value;
			totalWeight += kv.Value;
		}
		if ( totalWeight == 0 ) return 50;

		double raw = weightedSum / (double)totalWeight; // 0..100

		// Light psych modifier: stressed / demoralised / hollowed ops show a
		// real but bounded drop in displayed fit. Mirrors the resolver's
		// effectiveness curve at half-weight so the hint stays advisory and
		// doesn't overpromise compared to actual resolution.
		int penalty = 0;
		if ( op.Psychology.Stress >= 70 ) penalty += 8;
		else if ( op.Psychology.Stress >= 50 ) penalty += 4;
		if ( op.Psychology.Morale <= 30 ) penalty += 6;
		if ( op.Psychology.Conscience <= 25 && m.IsWetWork ) penalty -= 3; // numb to it
		if ( op.Status == OperativeStatus.Injured ) penalty += 10;

		int score = (int)Math.Round( raw ) - penalty;
		return Math.Clamp( score, 0, 100 );
	}

	/// <summary>Three-tier presentation bucket. Thresholds tuned for 0..100 fit scores.</summary>
	public static FitTier ToTier( int fitScore ) => fitScore switch
	{
		>= 75 => FitTier.Great,
		>= 60 => FitTier.Good,
		>= 40 => FitTier.Ok,
		_     => FitTier.Poor,
	};

	/// <summary>
	/// Honest fit-vs-difficulty tier: compares the operative's fit score to the
	/// mission difficulty INCLUDING the resolver's success margin, so "Great"
	/// on the picker means a genuinely strong success chance at the table —
	/// the hint and the dice can't disagree.
	/// </summary>
	public static FitTier TierFor( Operative op, Mission m )
	{
		int margin = FitScore( op, m ) - m.Difficulty - MissionResolver.SuccessMargin;
		return margin switch
		{
			>= 10  => FitTier.Great,  // solid success odds
			>= -5  => FitTier.Good,   // success plausible, partial likely
			>= -20 => FitTier.Ok,     // partial territory
			_      => FitTier.Poor,   // failure risk is real
		};
	}
}

public enum FitTier
{
	Poor,
	Ok,
	Good,
	Great,
}
