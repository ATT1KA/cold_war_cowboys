using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;

namespace CWC.Missions;

/// <summary>
/// Owns the per-cycle "available work" queue. 3-5 missions generated per
/// briefing phase; at least one morally ambiguous (MoralWeight >= 30) is
/// guaranteed when the template pool supports it.
/// </summary>
public sealed class MissionBoard
{
	private readonly MissionGenerator _gen;

	public MissionBoard( MissionGenerator gen ) { _gen = gen; }

	public List<Mission> Refresh( WorldState world, Rng rng, int min = 3, int max = 5 )
	{
		ExpireStale( world );

		int target = rng.Next( min, max + 1 );
		// Count only template-generated work toward the quota. Corporate
		// contracts (faction offers, directives, gray market) are a separate
		// channel — letting them fill the count starved the authored mission
		// pool to zero after cycle 1.
		int existingAvailable = world.Missions.Count( m =>
			m.Status == MissionStatus.Available
			&& m.IssuingFactionId == null
			&& !m.IsBoardDirective );
		int toGenerate = System.Math.Max( 0, target - existingAvailable );

		var pool = _gen.Templates.ToList();
		var generated = new List<Mission>();

		bool ambiguousSeeded = world.Missions.Any( m =>
			m.Status == MissionStatus.Available && m.MoralWeight >= 30 );

		for ( int i = 0; i < toGenerate && pool.Count > 0; i++ )
		{
			MissionTemplate template;
			if ( !ambiguousSeeded && i == toGenerate - 1 )
			{
				var ambiguous = pool.Where( t => t.MoralWeight >= 30 ).ToList();
				template = ambiguous.Count > 0 ? rng.Pick( ambiguous ) : rng.Pick( pool );
				if ( template.MoralWeight >= 30 ) ambiguousSeeded = true;
			}
			else
			{
				template = rng.Pick( pool );
				if ( template.MoralWeight >= 30 ) ambiguousSeeded = true;
			}

			var mission = _gen.Generate( template, world, rng.Fork( $"mission:{world.Corporate.Cycle}:{i}" ) );
			world.Missions.Add( mission );
			generated.Add( mission );
		}
		return generated;
	}

	public void SeedFromScenario( string templateId, WorldState world, Rng rng )
	{
		var t = _gen.FindTemplate( templateId );
		if ( t == null ) return;
		var mission = _gen.Generate( t, world, rng );
		mission.NarrativeFlagsOnSuccess.Add( "scenario:opening_resolved" );
		world.Missions.Add( mission );
	}

	private static void ExpireStale( WorldState world )
	{
		foreach ( var m in world.Missions )
		{
			if ( m.Status == MissionStatus.Available
				&& world.Corporate.Cycle > m.CycleDeadline )
			{
				m.Status = MissionStatus.Expired;
			}
		}
	}
}
