using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Generation;

/// <summary>
/// Generates the player's place in the corp: a boss, two rival division heads,
/// and a board. Stored as Operatives with high IDs (1000+) so they can be
/// referenced from missions and scenes without colliding with the field roster.
///
/// These NPCs are not assignable; they exist to drive Sprint 6 (Corporate Sim)
/// boardroom pressure and Sprint 4 narrative beats.
/// </summary>
public sealed class CorporateHierarchyGenerator
{
	public const int BossId = 1000;
	public const int RivalAId = 1001;
	public const int RivalBId = 1002;
	public const int BoardBaseId = 1100;

	private readonly NameGenerator _names;

	public CorporateHierarchyGenerator( NameGenerator names ) { _names = names; }

	public HierarchyResult Generate( Rng r )
	{
		var result = new HierarchyResult();

		result.Boss = MakeNpc( BossId, "Director", r.Fork( "boss" ) );
		result.RivalDivisionHeads.Add( MakeNpc( RivalAId, "Division Head — Operations", r.Fork( "rivalA" ) ) );
		result.RivalDivisionHeads.Add( MakeNpc( RivalBId, "Division Head — Finance", r.Fork( "rivalB" ) ) );

		for ( int i = 0; i < 5; i++ )
		{
			var npc = MakeNpc( BoardBaseId + i, "Board Member", r.Fork( $"board:{i}" ) );
			result.BoardMembers.Add( npc );
		}

		return result;
	}

	private Operative MakeNpc( int id, string title, Rng r )
	{
		var op = new Operative
		{
			Id = id,
			Name = _names.FullName( r.Fork( "name" ) ),
			Codename = title,
			Archetype = "executive",
			Gender = _names.Gender( r.Fork( "gen" ) ),
			Status = OperativeStatus.Active,
			Tenure = r.BellInt( 4, 30 ),
		};
		// Executives aren't field operatives, but stat them so consumers can
		// reason about pressure / influence (and so audits don't trip on zeros).
		var sr = r.Fork( "stats" );
		op.Skills.Combat = sr.BellInt( 15, 35 );
		op.Skills.Stealth = sr.BellInt( 15, 35 );
		op.Skills.Hacking = sr.BellInt( 20, 50 );
		op.Skills.Deception = sr.BellInt( 50, 80 );
		op.Skills.Intimidation = sr.BellInt( 40, 70 );
		op.Skills.Persuasion = sr.BellInt( 60, 90 );
		op.Psychology.Loyalty = sr.BellInt( 40, 70 );
		op.Psychology.Conscience = sr.BellInt( 50, 80 );
		return op;
	}

	public sealed class HierarchyResult
	{
		public Operative? Boss { get; set; }
		public List<Operative> RivalDivisionHeads { get; } = new();
		public List<Operative> BoardMembers { get; } = new();

		public IEnumerable<Operative> All()
		{
			if ( Boss != null ) yield return Boss;
			foreach ( var n in RivalDivisionHeads ) yield return n;
			foreach ( var n in BoardMembers ) yield return n;
		}
	}
}
