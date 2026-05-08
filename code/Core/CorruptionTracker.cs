using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Domain;

namespace CWC.Core;

/// <summary>
/// Night 5: Corruption index — a composite measure of how far the player has
/// drifted from humane management. Computed each cycle from team psychology,
/// corporate standing, and operational losses.
///
/// Five milestone thresholds define the arc:
///   20 "Competent"   — baseline; the game teaches that caring works.
///   40 "Effective"   — board gives harder directives; first signal of redefinition.
///   60 "Feared"      — rival factions more aggressive; darker archetype pool unlocks.
///   80 "The Machine" — UI desaturates, choice ordering inverts, dialogue turns transactional.
///   95 "Jenkins"     — endgame state. No announcement. Just feels different.
/// </summary>
public sealed class CorruptionTracker
{
	// ---- Milestone definitions ----

	public enum Milestone
	{
		None       = 0,
		Competent  = 20,
		Effective  = 40,
		Feared     = 60,
		TheMachine = 80,
		Jenkins    = 95,
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

	/// <summary>True when corruption >= 80 (The Machine). UI uses this to invert choice ordering.</summary>
	public bool ShouldInvertChoices => CorruptionIndex >= 80;

	/// <summary>Normalized 0..1 corruption for UI color lerping.</summary>
	public double CorruptionNormalized => Math.Clamp( CorruptionIndex / 100.0, 0.0, 1.0 );

	// ---- Computation ----

	/// <summary>
	/// Recompute corruption_index from current world state. Call once per cycle
	/// (after Resolution, before Aftermath).
	///
	/// corruption = (avg_team_wet_work * 0.3)
	///            + ((100 - avg_team_conscience) * 0.3)
	///            + (corporate_standing * 0.2)
	///            + (total_lost * 0.1)
	///            + (total_burned * 0.1)
	/// </summary>
	public void Compute( WorldState world )
	{
		NewlyCrossed.Clear();

		var active = world.Operatives.Where( o => o.Active ).ToList();
		if ( active.Count == 0 ) { CorruptionIndex = 0; return; }

		// avg_team_wet_work: average WetWorkCount across active ops, scaled 0..100
		double avgWetWork = active.Average( o =>
			Math.Min( o.Psychology.WetWorkCount, 10 ) / 10.0 * 100.0 );

		// avg_team_conscience: average conscience across active ops (already 0..100)
		double avgConscience = active.Average( o => (double)o.Psychology.Conscience );

		// corporate_standing: board confidence (0..100)
		double corpStanding = world.Corporate.BoardConfidence;

		// total_lost: count of dead/defected operatives, scaled
		int totalLost = world.Operatives.Count( o =>
			o.Status == OperativeStatus.Dead || o.Status == OperativeStatus.Defected );
		double lostScore = Math.Min( totalLost * 10.0, 100.0 );

		// total_burned: how hot things are (heat + suspicion, averaged)
		double burnedScore = (world.Corporate.Heat + world.Corporate.Suspicion) / 2.0;

		// Composite
		double raw = avgWetWork * 0.3
			+ (100.0 - avgConscience) * 0.3
			+ corpStanding * 0.2
			+ lostScore * 0.1
			+ burnedScore * 0.1;

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
	/// UI color for the corruption gauge. Lerps through a palette:
	///   0-20:  steel blue (neutral)
	///   20-40: teal (competent)
	///   40-60: amber (effective)
	///   60-80: deep orange (feared)
	///   80-95: crimson (the machine)
	///   95+:   near-black desaturated (jenkins)
	/// </summary>
	public (int r, int g, int b) GetColor()
	{
		double t = CorruptionNormalized;
		if ( t < 0.20 ) return LerpRgb( (120, 160, 200), (80, 190, 180), t / 0.20 );
		if ( t < 0.40 ) return LerpRgb( (80, 190, 180), (220, 180, 60), (t - 0.20) / 0.20 );
		if ( t < 0.60 ) return LerpRgb( (220, 180, 60), (230, 120, 50), (t - 0.40) / 0.20 );
		if ( t < 0.80 ) return LerpRgb( (230, 120, 50), (200, 50, 50), (t - 0.60) / 0.20 );
		if ( t < 0.95 ) return LerpRgb( (200, 50, 50), (80, 40, 40), (t - 0.80) / 0.15 );
		return (60, 35, 35); // Jenkins: nearly black
	}

	/// <summary>
	/// Saturation multiplier for the entire HUD. 1.0 = full color, 0.0 = grayscale.
	/// Desaturates progressively from 80+ (The Machine).
	/// </summary>
	public double HudSaturation
	{
		get
		{
			if ( CorruptionIndex < 60 ) return 1.0;
			if ( CorruptionIndex < 80 ) return 1.0 - (CorruptionIndex - 60) / 80.0; // subtle fade
			if ( CorruptionIndex < 95 ) return 0.5 - (CorruptionIndex - 80) / 60.0; // aggressive
			return 0.15; // Jenkins: nearly monochrome
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
