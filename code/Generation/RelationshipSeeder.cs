using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;

namespace CWC.Generation;

/// <summary>
/// Seeds asymmetric directed relationships between operatives. Density target
/// ~1.5 edges per operative. Each pair has an independent chance of forming
/// in either direction; reverse-direction edge is conditional and biased
/// toward the same Kind (with mutation chance).
/// </summary>
public sealed class RelationshipSeeder
{
	public List<Relationship> Seed( IList<Operative> ops, Rng r )
	{
		var edges = new List<Relationship>();
		if ( ops.Count < 2 ) return edges;

		double pairChance = 0.30;

		for ( int i = 0; i < ops.Count; i++ )
		{
			for ( int j = i + 1; j < ops.Count; j++ )
			{
				if ( !r.Chance( pairChance ) ) continue;

				var (kindA, scoreA) = RollKindAndScore( r );
				edges.Add( new Relationship
				{
					FromId = ops[i].Id,
					ToId   = ops[j].Id,
					Kind   = kindA,
					Score  = scoreA,
				} );

				if ( r.Chance( 0.7 ) )
				{
					var kindB = MirrorKind( kindA, r );
					int scoreB = r.Chance( 0.65 )
						? scoreA + r.Next( -15, 16 )
						: r.Next( -60, 61 );
					edges.Add( new Relationship
					{
						FromId = ops[j].Id,
						ToId   = ops[i].Id,
						Kind   = kindB,
						Score  = System.Math.Clamp( scoreB, -100, 100 ),
					} );
				}
			}
		}
		return edges;
	}

	private static (RelationshipKind kind, int score) RollKindAndScore( Rng r )
	{
		var kind = r.WeightedPick( new List<(RelationshipKind, double)>
		{
			(RelationshipKind.Acquaintance, 4.0),
			(RelationshipKind.Friend,       2.0),
			(RelationshipKind.Rival,        1.5),
			(RelationshipKind.Confidant,    1.0),
			(RelationshipKind.Mentor,       0.7),
			(RelationshipKind.Protege,      0.7),
			(RelationshipKind.Lover,        0.5),
			(RelationshipKind.Debtor,       0.4),
			(RelationshipKind.Creditor,     0.4),
		} );

		int score = kind switch
		{
			RelationshipKind.Friend     => r.Next( 25, 70 ),
			RelationshipKind.Confidant  => r.Next( 35, 80 ),
			RelationshipKind.Lover      => r.Next( 40, 90 ),
			RelationshipKind.Mentor     => r.Next( 20, 60 ),
			RelationshipKind.Protege    => r.Next( 20, 60 ),
			RelationshipKind.Rival      => r.Next( -70, -20 ),
			RelationshipKind.Debtor     => r.Next( -50, 10 ),
			RelationshipKind.Creditor   => r.Next( -10, 50 ),
			_                           => r.Next( -15, 25 ),
		};
		return (kind, score);
	}

	private static RelationshipKind MirrorKind( RelationshipKind k, Rng r )
	{
		// Mostly mirror; sometimes flip to capture asymmetry.
		bool mutate = r.Chance( 0.25 );
		if ( !mutate )
		{
			return k switch
			{
				RelationshipKind.Mentor    => RelationshipKind.Protege,
				RelationshipKind.Protege   => RelationshipKind.Mentor,
				RelationshipKind.Debtor    => RelationshipKind.Creditor,
				RelationshipKind.Creditor  => RelationshipKind.Debtor,
				_                          => k,
			};
		}
		return r.Pick( new[]
		{
			RelationshipKind.Acquaintance,
			RelationshipKind.Rival,
			RelationshipKind.Friend,
			RelationshipKind.Confidant,
		} );
	}
}
