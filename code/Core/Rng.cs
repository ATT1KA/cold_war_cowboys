// [FRAMEWORK] Game-agnostic. Copy as-is into another s&box project. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;

namespace CWC.Core;

/// <summary>
/// Forkable seeded PRNG. Sub-streams isolate concerns: e.g. Fork("stats") vs Fork("names")
/// so adding a random call in one generator doesn't shift another's numbers.
///
/// Implemented as xoshiro256** with explicit state so long-lived streams (the
/// corporate systems keep theirs across cycles) can be serialized into saves —
/// determinism survives a load, which System.Random could never guarantee.
/// </summary>
public sealed class Rng
{
	public ulong Seed { get; }

	private ulong _s0, _s1, _s2, _s3;

	public Rng( ulong seed )
	{
		Seed = seed;
		// SplitMix64 to spread the seed across the four state words.
		ulong x = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
		_s0 = SplitMix( ref x );
		_s1 = SplitMix( ref x );
		_s2 = SplitMix( ref x );
		_s3 = SplitMix( ref x );
	}

	private static ulong SplitMix( ref ulong x )
	{
		x += 0x9E3779B97F4A7C15UL;
		ulong z = x;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
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

	// ---- xoshiro256** core ----

	private ulong NextUInt64()
	{
		ulong result = Rotl( _s1 * 5, 7 ) * 9;
		ulong t = _s1 << 17;
		_s2 ^= _s0;
		_s3 ^= _s1;
		_s1 ^= _s2;
		_s0 ^= _s3;
		_s2 ^= t;
		_s3 = Rotl( _s3, 45 );
		return result;
	}

	private static ulong Rotl( ulong x, int k ) => (x << k) | (x >> (64 - k));

	// ---- Save/load support ----

	/// <summary>Snapshot the stream position for serialization.</summary>
	public ulong[] GetState() => new[] { _s0, _s1, _s2, _s3 };

	/// <summary>Restore a stream position captured by <see cref="GetState"/>.</summary>
	public void SetState( IReadOnlyList<ulong> state )
	{
		if ( state == null || state.Count != 4 ) return;
		_s0 = state[0]; _s1 = state[1]; _s2 = state[2]; _s3 = state[3];
	}

	// ---- Public API (unchanged surface) ----

	public int Next( int minInclusive, int maxExclusive )
	{
		if ( maxExclusive <= minInclusive ) return minInclusive;
		ulong range = (ulong)( (long)maxExclusive - minInclusive );
		return minInclusive + (int)( NextUInt64() % range );
	}

	public int Next( int maxExclusive ) => Next( 0, maxExclusive );

	public double NextDouble() => ( NextUInt64() >> 11 ) * ( 1.0 / (1UL << 53) );

	public bool Chance( double p ) => NextDouble() < p;

	public int BellInt( int min, int max, int rolls = 3 )
	{
		if ( max <= min ) return min;
		double sum = 0;
		for ( int i = 0; i < rolls; i++ ) sum += NextDouble();
		double avg = sum / rolls;
		return min + (int)Math.Round( avg * (max - min) );
	}

	public T Pick<T>( IList<T> options )
	{
		if ( options == null || options.Count == 0 )
			throw new ArgumentException( "Pick requires non-empty list" );
		return options[Next( options.Count )];
	}

	public T WeightedPick<T>( IList<(T value, double weight)> options )
	{
		double total = 0;
		for ( int i = 0; i < options.Count; i++ )
			if ( options[i].weight > 0 ) total += options[i].weight;
		if ( total <= 0 )
			throw new ArgumentException( "WeightedPick requires positive total weight" );
		double roll = NextDouble() * total;
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
			int idx = Next( pool.Count );
			result.Add( pool[idx] );
			pool.RemoveAt( idx );
		}
		return result;
	}
}
