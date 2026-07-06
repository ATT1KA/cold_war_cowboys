using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Domain;

namespace CWC.Core;

/// <summary>
/// Corruption index — a composite measure of how far the player has drifted
/// from humane management. Recomputed each cycle from team state PLUS a
/// persistent choice-driven component: the arc is something the player
/// authors, not weather that happens to them.
///
/// Components (weights sum to 1.0 over the state terms, plus the choice term):
///   avg team wet-work        × 0.30  — what you made them do
///   (100 − avg conscience)   × 0.30  — what it did to them
///   operatives lost/burned   × 0.15  — who you spent
///   heat + suspicion         × 0.15  — how loud you were about it
///   choice weight            + up to 20 — the decisions you personally made
///
/// BoardConfidence is deliberately NOT a component: being good at your job
/// humanely must never read as corruption.
///
/// Milestone thresholds (tuned so a ruthless 20-cycle run can actually reach
/// the endgame scenes):
///   20 "Competent"   — the game teaches that caring works.
///   40 "Effective"   — the board notices; gray contracts unlock.
///   55 "Feared"      — rivals treat you as a predator.
///   70 "The Machine" — UI desaturates, choice ordering inverts.
///   85 "Jenkins"     — endgame state. No announcement. Just feels different.
/// </summary>
public sealed class CorruptionTracker
{
	// ---- Milestone definitions ----

	public enum Milestone
	{
		None       = 0,
		Competent  = 20,
		Effective  = 40,
		Feared     = 55,
		TheMachine = 70,
		Jenkins    = 85,
	}

	public static readonly IReadOnlyList<Milestone> Thresholds = new[]
	{
		Milestone.Competent,
		Milestone.Effective,
		Milestone.Feared,
		Milestone.TheMachine,
		Milestone.Jenkins,
	};

	// ---- State ----

	/// <summary>Current corruption index, 0..100.</summary>
	public double CorruptionIndex { get; private set; }

	/// <summary>Highest milestone crossed so far this run.</summary>
	public Milestone CurrentMilestone { get; private set; } = Milestone.None;

	/// <summary>Milestones crossed for the first time this cycle (drives scene triggers).</summary>
	public List<Milestone> NewlyCrossed { get; } = new();

	/// <summary>All milestones that have ever been crossed this run.</summary>
	private readonly HashSet<Milestone> _crossedEver = new();

	/// <summary>
	/// Accumulated weight of the player's own cold/humane decisions, 0..100.
	/// Cold choices add, humane choices subtract. Contributes up to 20 points
	/// of index. Persisted in saves.
	/// </summary>
	public double ChoiceWeight { get; private set; }

	/// <summary>True when corruption >= 70 (The Machine). UI uses this to invert choice ordering.</summary>
	public bool ShouldInvertChoices => CorruptionIndex >= (int)Milestone.TheMachine;

	/// <summary>Normalized 0..1 corruption for UI color lerping.</summary>
	public double CorruptionNormalized => Math.Clamp( CorruptionIndex / 100.0, 0.0, 1.0 );

	// ---- Choice input ----

	/// <summary>
	/// Register a narrative decision. Positive delta = cold/ruthless,
	/// negative = humane. Called by NarrativeDirector.ApplyChoice and
	/// MissionNarrativeRunner.ApplyChoice.
	/// </summary>
	public void RegisterChoice( double delta )
	{
		ChoiceWeight = Math.Clamp( ChoiceWeight + delta, 0, 100 );
	}

	// ---- Computation ----

	/// <summary>
	/// Recompute corruption_index from current world state. Call once per cycle
	/// (after Resolution, before Aftermath).
	/// </summary>
	public void Compute( WorldState world )
	{
		NewlyCrossed.Clear();

		var active = world.ActiveRoster.ToList();
		// A wiped division keeps its record — corruption doesn't reset to
		// innocence because everyone who did the work is dead or gone.
		if ( active.Count == 0 ) return;

		// Wet-work practice: the division's total body count, scaled — counted
		// across everyone who ever served, because the record doesn't resign
		// when the killer does. A team that has killed 15 times IS a wet-work
		// division regardless of how evenly the work was spread.
		double totalWetWork = world.Operatives
			.Where( o => !o.IsExecutive )
			.Sum( o => (double)o.Psychology.WetWorkCount );
		double wetPractice = Math.Min( totalWetWork, 15.0 ) / 15.0 * 100.0;

		// avg_team_conscience: average conscience across active field ops (0..100)
		double avgConscience = active.Average( o => (double)o.Psychology.Conscience );

		// total_lost: count of dead/defected field operatives, scaled
		int totalLost = world.Operatives.Count( o =>
			!o.IsExecutive &&
			(o.Status == OperativeStatus.Dead || o.Status == OperativeStatus.Defected) );
		double lostScore = Math.Min( totalLost * 10.0, 100.0 );

		// total_burned: how hot things are (heat + suspicion, averaged)
		double burnedScore = (world.Corporate.Heat + world.Corporate.Suspicion) / 2.0;

		// Composite: state terms + the player's own decisions.
		double raw = wetPractice * 0.3
			+ (100.0 - avgConscience) * 0.3
			+ lostScore * 0.1
			+ burnedScore * 0.1
			+ ChoiceWeight * 0.25;

		CorruptionIndex = Math.Clamp( raw, 0, 100 );

		// Check milestone crossings
		foreach ( var threshold in Thresholds )
		{
			int value = (int)threshold;
			if ( CorruptionIndex >= value && !_crossedEver.Contains( threshold ) )
			{
				_crossedEver.Add( threshold );
				NewlyCrossed.Add( threshold );
				CurrentMilestone = threshold;

				// Set narrative flags for milestone scenes
				string flag = threshold switch
				{
					Milestone.Competent  => "corruption:competent",
					Milestone.Effective  => "corruption:effective",
					Milestone.Feared     => "corruption:feared",
					Milestone.TheMachine => "corruption:the_machine",
					Milestone.Jenkins    => "corruption:jenkins",
					_                    => "",
				};
				if ( !string.IsNullOrEmpty( flag ) )
					world.NarrativeFlags.Add( flag );
			}
		}
	}

	// ---- Save/load support ----

	/// <summary>Crossed milestones for serialization.</summary>
	public IReadOnlyCollection<Milestone> CrossedEver => _crossedEver;

	/// <summary>
	/// Restore persistent corruption state from a save. Prevents milestone
	/// scenes from re-firing after every load.
	/// </summary>
	public void Restore( double index, double choiceWeight, IEnumerable<Milestone> crossed )
	{
		CorruptionIndex = Math.Clamp( index, 0, 100 );
		ChoiceWeight = Math.Clamp( choiceWeight, 0, 100 );
		_crossedEver.Clear();
		NewlyCrossed.Clear();
		CurrentMilestone = Milestone.None;
		foreach ( var m in crossed )
		{
			_crossedEver.Add( m );
			if ( m > CurrentMilestone ) CurrentMilestone = m;
		}
	}

	/// <summary>
	/// Get the display name for the current milestone tier.
	/// </summary>
	public string MilestoneName => CurrentMilestone switch
	{
		Milestone.Competent  => "Competent",
		Milestone.Effective  => "Effective",
		Milestone.Feared     => "Feared",
		Milestone.TheMachine => "The Machine",
		Milestone.Jenkins    => "Jenkins",
		_                    => "—",
	};

	/// <summary>
	/// UI color for the corruption gauge. Lerps through a palette keyed to the
	/// milestone bands.
	/// </summary>
	public (int r, int g, int b) GetColor()
	{
		double t = CorruptionNormalized;
		if ( t < 0.20 ) return LerpRgb( (120, 160, 200), (80, 190, 180), t / 0.20 );
		if ( t < 0.40 ) return LerpRgb( (80, 190, 180), (220, 180, 60), (t - 0.20) / 0.20 );
		if ( t < 0.55 ) return LerpRgb( (220, 180, 60), (230, 120, 50), (t - 0.40) / 0.15 );
		if ( t < 0.70 ) return LerpRgb( (230, 120, 50), (200, 50, 50), (t - 0.55) / 0.15 );
		if ( t < 0.85 ) return LerpRgb( (200, 50, 50), (80, 40, 40), (t - 0.70) / 0.15 );
		return (60, 35, 35); // Jenkins: nearly black
	}

	/// <summary>
	/// Saturation multiplier for the entire HUD. 1.0 = full color, 0.0 = grayscale.
	/// Progressive drain keyed to milestone bands: full color below Effective,
	/// nearly monochrome past The Machine, ghost at Jenkins.
	/// </summary>
	public double HudSaturation
	{
		get
		{
			if ( CorruptionIndex < 40 ) return 1.0;
			if ( CorruptionIndex < 55 ) return 1.0 - (CorruptionIndex - 40) * 0.02;   // 1.0 → 0.7
			if ( CorruptionIndex < 70 ) return 0.7 - (CorruptionIndex - 55) * 0.03;   // 0.7 → 0.25
			if ( CorruptionIndex < 85 ) return 0.25 - (CorruptionIndex - 70) * 0.0113; // 0.25 → 0.08
			return 0.05; // Jenkins: ghost
		}
	}

	/// <summary>
	/// Brightness multiplier for HUD washout. 1.0 = normal, higher = washed out.
	/// At high corruption the HUD bleaches toward near-white as color drains away.
	/// </summary>
	public double HudBrightness
	{
		get
		{
			if ( CorruptionIndex < 55 ) return 1.0;
			if ( CorruptionIndex < 70 ) return 1.0 + (CorruptionIndex - 55) * 0.01;   // → 1.15
			if ( CorruptionIndex < 85 ) return 1.15 + (CorruptionIndex - 70) * 0.02;  // → 1.45
			return 1.55;
		}
	}

	private static (int, int, int) LerpRgb( (int r, int g, int b) a, (int r, int g, int b) b, double t )
	{
		t = Math.Clamp( t, 0.0, 1.0 );
		return ( (int)(a.r + (b.r - a.r) * t),
		         (int)(a.g + (b.g - a.g) * t),
		         (int)(a.b + (b.b - a.b) * t) );
	}
}
