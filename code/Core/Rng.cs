using System;
using System.Collections.Generic;

namespace CWC.Core;

/// <summary>
/// Forkable seeded PRNG. Sub-streams isolate concerns: e.g. Fork("stats") vs Fork("names")
/// so adding a random call in one generator doesn't shift another's numbers.
/// </summary>
public sealed class Rng
{
	public ulong Seed { get; }
	private readonly Random _r;

	public Rng( ulong seed )
	{
		Seed = seed;
		int s = unchecked( (int)((seed ^ (seed >> 32)) & 0x7fffffff) );
		_r = new Random( s == 0 ? 1 : s );
	}

	public Rng Fork( string label )
	{
		ulong h = 14695981039346656037UL; // FNV-1a offset basis
		foreach ( var c in label )
		{
			h ^= c;
			h *= 1099511628211UL;
		}
		return new Rng( Seed ^ h );
	}

	public int Next( int minInclusive, int maxExclusive ) => _r.Next( minInclusive, maxExclusive );
	public int Next( int maxExclusive ) => _r.Next( maxExclusive );
	public double NextDouble() => _r.NextDouble();
	public bool Chance( double p ) => _r.NextDouble() < p;

	public int BellInt( int min, int max, int rolls = 3 )
	{
		if ( max <= min ) return min;
		double sum = 0;
		for ( int i = 0; i < rolls; i++ ) sum += _r.NextDouble();
		double avg = sum / rolls;
		return min + (int)Math.Round( avg * (max - min) );
	}

	public T Pick<T>( IList<T> options )
	{
		if ( options == null || options.Count == 0 )
			throw new ArgumentException( "Pick requires non-empty list" );
		return options[_r.Next( options.Count )];
	}

	public T WeightedPick<T>( IList<(T value, double weight)> options )
	{
		double total = 0;
		for ( int i = 0; i < options.Count; i++ )
			if ( options[i].weight > 0 ) total += options[i].weight;
		if ( total <= 0 )
			throw new ArgumentException( "WeightedPick requires positive total weight" );
		double roll = _r.NextDouble() * total;
		double acc = 0;
		for ( int i = 0; i < options.Count; i++ )
		{
			if ( options[i].weight <= 0 ) continue;
			acc += options[i].weight;
			if ( roll <= acc ) return options[i].value;
		}
		return options[^1].value;
	}

	public List<T> DistinctSample<T>( IList<T> source, int n )
	{
		var pool = new List<T>( source );
		n = Math.Min( n, pool.Count );
		var result = new List<T>( n );
		for ( int i = 0; i < n; i++ )
		{
			int idx = _r.Next( pool.Count );
			result.Add( pool[idx] );
			pool.RemoveAt( idx );
		}
		return result;
	}
}
