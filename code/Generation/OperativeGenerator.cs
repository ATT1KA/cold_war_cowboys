// [FRAMEWORK-PATTERN] The shape is reusable; the vocabulary is CWC's. Port by rename. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Generation.Templates;

namespace CWC.Generation;

/// <summary>
/// Builds Operatives from archetype templates. Enforces the one-trait-per-axis
/// invariant. Trait incidence: Personality ~85%, Background ~85%, Vice ~35%,
/// Compulsion ~25%.
/// </summary>
public sealed class OperativeGenerator
{
	private readonly List<ArchetypeTemplate> _archetypes;
	private readonly Dictionary<string, TraitTemplate> _traitsById;
	private readonly NameGenerator _names;

	public OperativeGenerator(
		List<ArchetypeTemplate> archetypes,
		List<TraitTemplate> traits,
		NameGenerator names )
	{
		_archetypes = archetypes.Count > 0 ? archetypes : FallbackArchetypes();
		_traitsById = traits.ToDictionary( t => t.Id, t => t );
		_names = names;
	}

	public Operative Generate( int id, Rng r, string? archetypeId = null )
	{
		var archetype = ResolveArchetype( r, archetypeId );

		var op = new Operative
		{
			Id = id,
			Archetype = archetype.Id,
			Background = archetype.Background,
			NarrativeRole = archetype.NarrativeRole,
			Name = _names.FullName( r.Fork( $"name:{id}" ) ),
			Codename = _names.Codename( r.Fork( $"code:{id}" ), archetype.CodenamePool ),
			Gender = _names.Gender( r.Fork( $"gen:{id}" ) ),
			Status = OperativeStatus.Active,
			Tenure = 0,
		};

		RollSkills( op, archetype, r.Fork( $"skills:{id}" ) );
		RollPsychology( op, archetype, r.Fork( $"psych:{id}" ) );
		RollTraits( op, archetype, r.Fork( $"traits:{id}" ) );
		ApplyTraitModifiers( op );
		Clamp( op );

		return op;
	}

	/// <summary>
	/// Generate a starting team with Night 2 variety enforcement:
	/// - No two operatives from the same archetype
	/// - No more than 2 from the same background category
	/// - At least one with Conscience > 65
	/// - At least one with Conscience &lt; 30
	/// - At least one with Ambition > 70
	/// - At least one with Loyalty &lt; 40
	/// Re-rolls up to maxAttempts times to satisfy all constraints.
	/// </summary>
	public List<Operative> GenerateTeam( int count, Rng r, int maxAttempts = 200 )
	{
		for ( int attempt = 0; attempt < maxAttempts; attempt++ )
		{
			var team = new List<Operative>();
			var usedArchetypes = new HashSet<string>();
			var backgroundCounts = new Dictionary<string, int>();
			var pool = new List<ArchetypeTemplate>( _archetypes );

			var fork = r.Fork( $"team:{attempt}" );

			for ( int i = 0; i < count; i++ )
			{
				// Filter: exclude used archetypes and backgrounds at cap
				var eligible = pool
					.Where( a => !usedArchetypes.Contains( a.Id ) )
					.Where( a => !backgroundCounts.TryGetValue( a.Background, out var c ) || c < 2 )
					.ToList();
				if ( eligible.Count == 0 ) break;

				var archetype = fork.Pick( eligible );
				var op = Generate( i + 1, fork.Fork( $"op:{i}" ), archetype.Id );
				team.Add( op );
				usedArchetypes.Add( archetype.Id );
				backgroundCounts[archetype.Background] = backgroundCounts.GetValueOrDefault( archetype.Background ) + 1;
			}

			if ( team.Count < count ) continue;
			if ( !ValidateTeamVariety( team ) ) continue;
			return team;
		}

		// Fallback: generate without variety constraints rather than fail
		var fallback = new List<Operative>();
		for ( int i = 0; i < count; i++ )
			fallback.Add( Generate( i + 1, r.Fork( $"fallback:{i}" ) ) );
		return fallback;
	}

	private static bool ValidateTeamVariety( List<Operative> team )
	{
		bool hasHighConscience = team.Any( o => o.Psychology.Conscience > 65 );
		bool hasLowConscience  = team.Any( o => o.Psychology.Conscience < 30 );
		bool hasHighAmbition   = team.Any( o => o.Psychology.Ambition > 70 );
		bool hasLowLoyalty     = team.Any( o => o.Psychology.Loyalty < 40 );
		return hasHighConscience && hasLowConscience && hasHighAmbition && hasLowLoyalty;
	}

	private ArchetypeTemplate ResolveArchetype( Rng r, string? id )
	{
		if ( id != null )
		{
			var match = _archetypes.FirstOrDefault( a => a.Id == id );
			if ( match != null ) return match;
		}
		return r.Pick( _archetypes );
	}

	private void RollSkills( Operative op, ArchetypeTemplate a, Rng r )
	{
		op.Skills.Combat       = RollBand( r, a, "Combat",       30, 60 );
		op.Skills.Stealth      = RollBand( r, a, "Stealth",      30, 60 );
		op.Skills.Hacking      = RollBand( r, a, "Hacking",      20, 50 );
		op.Skills.Deception    = RollBand( r, a, "Deception",    30, 60 );
		op.Skills.Intimidation = RollBand( r, a, "Intimidation", 25, 55 );
		op.Skills.Persuasion   = RollBand( r, a, "Persuasion",   25, 55 );
	}

	private static int RollBand( Rng r, ArchetypeTemplate a, string key, int defMin, int defMax )
	{
		if ( a.SkillBands.TryGetValue( key, out var band ) ) return band.Roll( r );
		return r.BellInt( defMin, defMax );
	}

	private static void RollPsychology( Operative op, ArchetypeTemplate a, Rng r )
	{
		op.Psychology.Loyalty    = a.Loyalty.Roll( r );
		op.Psychology.Stress     = a.Stress.Roll( r );
		op.Psychology.Morale     = a.Morale.Roll( r );
		op.Psychology.Conscience = a.Conscience.Roll( r );
		op.Psychology.Ambition   = a.Ambition.Roll( r );
	}

	private void RollTraits( Operative op, ArchetypeTemplate a, Rng r )
	{
		TryAddTraitFromPool( op, r, TraitAxis.Personality, a.PersonalityPool, 0.85 );
		TryAddTraitFromPool( op, r, TraitAxis.Background,  a.BackgroundPool,  0.85 );
		TryAddTraitFromPool( op, r, TraitAxis.Vice,        a.VicePool,        0.35 );
		TryAddTraitFromPool( op, r, TraitAxis.Compulsion,  a.CompulsionPool,  0.25 );
	}

	private void TryAddTraitFromPool( Operative op, Rng r, TraitAxis axis, IList<string> pool, double chance )
	{
		if ( !r.Chance( chance ) ) return;
		if ( op.Traits.Any( t => t.Axis == axis ) ) return; // one-per-axis

		var candidates = pool.Count > 0
			? pool.Where( id => _traitsById.ContainsKey( id ) ).ToList()
			: _traitsById.Values.Where( t => MatchesAxis( t.Axis, axis ) ).Select( t => t.Id ).ToList();
		if ( candidates.Count == 0 ) return;

		var picked = _traitsById[r.Pick( candidates )];
		// Defensive: don't add a trait of the wrong axis even if a pool entry was mis-tagged.
		if ( !MatchesAxis( picked.Axis, axis ) ) return;

		op.Traits.Add( ToDomain( picked ) );
	}

	private static bool MatchesAxis( string axisString, TraitAxis axis )
		=> Enum.TryParse<TraitAxis>( axisString, true, out var parsed ) && parsed == axis;

	private static Trait ToDomain( TraitTemplate t )
	{
		var axis = Enum.TryParse<TraitAxis>( t.Axis, true, out var a ) ? a : TraitAxis.Personality;
		var trait = new Trait
		{
			Id = t.Id,
			Name = t.Name,
			Axis = axis,
			Description = t.Description,
			LoyaltyModifier = t.LoyaltyModifier,
			StressModifier = t.StressModifier,
			ConscienceModifier = t.ConscienceModifier,
		};
		foreach ( var kv in t.SkillModifiers )
		{
			if ( Enum.TryParse<SkillKind>( kv.Key, true, out var sk ) )
				trait.SkillModifiers[sk] = kv.Value;
		}
		return trait;
	}

	private static void ApplyTraitModifiers( Operative op )
	{
		foreach ( var t in op.Traits )
		{
			foreach ( var kv in t.SkillModifiers )
				op.Skills.Add( kv.Key, kv.Value );
			op.Psychology.Loyalty    += t.LoyaltyModifier;
			op.Psychology.Stress     += t.StressModifier;
			op.Psychology.Conscience += t.ConscienceModifier;
		}
	}

	private static void Clamp( Operative op )
	{
		var s = op.Skills;
		var p = op.Psychology;
		s.Combat = Math.Clamp( s.Combat, 0, 100 );
		s.Stealth = Math.Clamp( s.Stealth, 0, 100 );
		s.Hacking = Math.Clamp( s.Hacking, 0, 100 );
		s.Deception = Math.Clamp( s.Deception, 0, 100 );
		s.Intimidation = Math.Clamp( s.Intimidation, 0, 100 );
		s.Persuasion = Math.Clamp( s.Persuasion, 0, 100 );
		p.Loyalty = Math.Clamp( p.Loyalty, 0, 100 );
		p.Stress = Math.Clamp( p.Stress, 0, 100 );
		p.Morale = Math.Clamp( p.Morale, 0, 100 );
		p.Conscience = Math.Clamp( p.Conscience, 0, 100 );
		p.Ambition = Math.Clamp( p.Ambition, 0, 100 );
	}

	private static List<ArchetypeTemplate> FallbackArchetypes() => new()
	{
		new ArchetypeTemplate
		{
			Id = "operator", DisplayName = "Operator",
			SkillBands = new() {
				{ "Combat", new IntRange(55,85) }, { "Stealth", new IntRange(35,65) },
				{ "Hacking", new IntRange(20,45) }, { "Intimidation", new IntRange(50,80) },
			},
		},
		new ArchetypeTemplate { Id = "ghost", DisplayName = "Ghost",
			SkillBands = new() { { "Stealth", new IntRange(60,90) }, { "Deception", new IntRange(50,80) } } },
		new ArchetypeTemplate { Id = "decker", DisplayName = "Decker",
			SkillBands = new() { { "Hacking", new IntRange(65,95) } } },
		new ArchetypeTemplate { Id = "fixer", DisplayName = "Fixer",
			SkillBands = new() { { "Persuasion", new IntRange(65,90) }, { "Deception", new IntRange(60,85) } } },
	};
}
