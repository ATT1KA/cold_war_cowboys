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
		// Cap individual wet-work at 10 for scaling purposes.
		double avgWetWork = active.Average( o => Math.Min( o.Psychology.WetWorkCount, 10 ) / 10.0 * 100.0 );

		// avg_team_conscience: average conscience across active ops (already 0..100)
		double avgConscience = active.Average( o => (double)o.Psychology.Conscience );

		// corporate_standing: board confidence (0..100)
		double corpStanding = world.Corporate.BoardConfidence;

