// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
namespace CWC.Domain;

/// <summary>
/// Operative skill axes. 0..100. Used by Sprint 3 MissionResolver to compute the
/// raw skill margin against mission StatWeights. Mutable across a run; deltas
/// applied through ConsequenceProcessor (e.g. mission XP, training, injury).
/// </summary>
public sealed class Skills
{
	public int Combat { get; set; }
	public int Stealth { get; set; }
	public int Hacking { get; set; }
	public int Deception { get; set; }
	public int Intimidation { get; set; }
	public int Persuasion { get; set; }

	public int Get( SkillKind kind ) => kind switch
	{
		SkillKind.Combat => Combat,
		SkillKind.Stealth => Stealth,
		SkillKind.Hacking => Hacking,
		SkillKind.Deception => Deception,
		SkillKind.Intimidation => Intimidation,
		SkillKind.Persuasion => Persuasion,
		_ => 0,
	};

	public void Add( SkillKind kind, int delta )
	{
		switch ( kind )
		{
			case SkillKind.Combat: Combat += delta; break;
			case SkillKind.Stealth: Stealth += delta; break;
			case SkillKind.Hacking: Hacking += delta; break;
			case SkillKind.Deception: Deception += delta; break;
			case SkillKind.Intimidation: Intimidation += delta; break;
			case SkillKind.Persuasion: Persuasion += delta; break;
		}
	}

	public Skills Clone() => new()
	{
		Combat = Combat, Stealth = Stealth, Hacking = Hacking,
		Deception = Deception, Intimidation = Intimidation, Persuasion = Persuasion,
	};
}

public enum SkillKind
{
	Combat, Stealth, Hacking, Deception, Intimidation, Persuasion,
}

/// <summary>
/// Psychology dials. Sprint 3's effective-skill multiplier reads these to compute
/// the corruption arc — high stress / low loyalty / low conscience reduces the
/// operative's effective skill, regardless of raw stats.
///
/// Loyalty: how committed the op is to the corp. Low → defection risk.
/// Stress: 0..100, accumulates from missions, decays slowly. High → effectiveness loss.
/// Morale: 0..100, lifts/drops with outcomes.
/// Conscience: 0..100, eroded by wet-work / morally heavy missions. The arc dial.
/// </summary>
public sealed class Psychology
{
	public int Loyalty { get; set; } = 70;
	public int Stress { get; set; } = 20;
	public int Morale { get; set; } = 70;
	public int Conscience { get; set; } = 70;

	/// <summary>Drive to climb. High → power plays, low → content to follow. Night 2 variety axis.</summary>
	public int Ambition { get; set; } = 50;

	/// <summary>Per-operative wet-work counter. Tripwires at 3 and 7 (third_kill, cold_blooded).</summary>
	public int WetWorkCount { get; set; } = 0;

	public Psychology Clone() => new()
	{
		Loyalty = Loyalty, Stress = Stress, Morale = Morale,
		Conscience = Conscience, Ambition = Ambition, WetWorkCount = WetWorkCount,
	};
}
