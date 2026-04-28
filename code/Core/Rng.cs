using System;

namespace ColdWarCowboys.Core;

/// <summary>
/// Seeded deterministic PRNG used by every system that needs randomness.
/// Replays of a save with the same seed produce identical sequences, which
/// keeps mission resolution and corporate AI auditable.
/// </summary>
public sealed class Rng
{
	private readonly Random _rng;

	public int Seed { get; }

	public Rng( int seed )
	{
		Seed = seed;
		_rng = new Random( seed );
	}

	/// <summary>Returns an int in [0, max).</summary>
	public int Next( int max ) => _rng.Next( max );

	/// <summary>Returns an int in [min, max).</summary>
	public int Range( int min, int max ) => _rng.Next( min, max );

	/// <summary>Returns a double in [0,1).</summary>
	public double NextDouble() => _rng.NextDouble();

	/// <summary>True with probability p.</summary>
	public bool Chance( double p ) => _rng.NextDouble() < p;

	/// <summary>Picks a random element from a non-empty list.</summary>
	public T Pick<T>( IReadOnlyList<T> items )
	{
		if ( items == null || items.Count == 0 )
			throw new ArgumentException( "Cannot pick from empty list", nameof(items) );
		return items[_rng.Next( items.Count )];
	}

	/// <summary>Picks an element from a list of (item, weight) pairs.</summary>
	public T PickWeighted<T>( IReadOnlyList<(T item, double weight)> items )
	{
		double total = 0;
		for ( int i = 0; i < items.Count; i++ ) total += items[i].weight;
		double roll = _rng.NextDouble() * total;
		double acc = 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			acc += items[i].weight;
			if ( roll <= acc ) return items[i].item;
		}
		return items[^1].item;
	}
}
