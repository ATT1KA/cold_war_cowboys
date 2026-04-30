using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;
using CWC.Generation.Templates;

namespace CWC.Generation;

/// <summary>
/// Orchestrates the full world build for a NewGame. Each sub-generator gets a
/// salt-isolated Rng fork — adding a roll in one generator can't shift another's
/// numbers, so seeds remain reproducible across code changes.
/// </summary>
public sealed class WorldGenerator
{
	public sealed class Options
	{
		public int RosterSize { get; set; } = 6;
		public string TemplateRoot { get; set; } = "Data/Templates";
	}

	public WorldState Generate( ulong seed, Options? opts = null )
	{
		opts ??= new Options();
		var loader = new TemplateLoader( opts.TemplateRoot );

		var rootRng = new Rng( seed );
		var world = new WorldState { Seed = seed };

		BuildSetting( world, rootRng.Fork( "setting" ), loader );
		BuildFactions( world, loader );

		var names = new NameGenerator( loader.LoadNames() );
		var opGen = new OperativeGenerator( loader.LoadArchetypes(), loader.LoadTraits(), names );

		BuildRoster( world, rootRng.Fork( "roster" ), opGen, opts.RosterSize );
		BuildHierarchy( world, rootRng.Fork( "hierarchy" ), names );
		BuildRelationships( world, rootRng.Fork( "relations" ) );
		BuildScenario( world, rootRng.Fork( "scenario" ), loader );

		world.Corporate.Cycle = 1;
		return world;
	}

	private static void BuildSetting( WorldState world, Rng r, TemplateLoader loader )
	{
		var t = loader.LoadWorld();
		world.Setting = new WorldSetting
		{
			CorpName    = t.Corps.Count > 0 ? r.Pick( t.Corps ) : "Panopticon Holdings",
			Location    = t.Locations.Count > 0 ? r.Pick( t.Locations ) : "Neo-Detroit",
			Year        = t.Years.Count > 0 ? r.Pick( t.Years ) : 2087,
			EraTagline  = t.EraTaglines.Count > 0 ? r.Pick( t.EraTaglines ) : "after the second collapse",
			ToneTags    = t.ToneTags.Count > 0 ? new List<string>( t.ToneTags ) : new() { "noir", "cyberpunk", "corporate" },
		};
	}

	private static void BuildFactions( WorldState world, TemplateLoader loader )
	{
		var t = loader.LoadWorld();
		var sources = t.Factions.Count > 0 ? t.Factions : DefaultFactions();
		foreach ( var f in sources )
		{
			var name = f.Name.Replace( "{HOST}", world.Setting.CorpName );
			world.Factions.Add( new Faction
			{
				Id = f.Id,
				Name = name,
				Kind = ParseKind( f.Kind ),
				Standing = f.Standing,
				Reputation = f.Reputation,
				Cash = f.Cash,
				InternalPressure = 0,
			} );
		}
	}

	private static void BuildRoster( WorldState world, Rng r, OperativeGenerator opGen, int size )
	{
		string[] mix = { "operator", "ghost", "decker", "fixer" };
		for ( int i = 0; i < size; i++ )
		{
			var arch = mix[i % mix.Length];
			world.Operatives.Add( opGen.Generate( i + 1, r, arch ) );
		}
	}

	private static void BuildHierarchy( WorldState world, Rng r, NameGenerator names )
	{
		var hier = new CorporateHierarchyGenerator( names ).Generate( r );
		foreach ( var npc in hier.All() )
			world.Operatives.Add( npc );
	}

	private static void BuildRelationships( WorldState world, Rng r )
	{
		var seeder = new RelationshipSeeder();
		// Field roster only — IDs <1000.
		var roster = new List<Operative>();
		foreach ( var o in world.Operatives )
			if ( o.Id < CorporateHierarchyGenerator.BossId ) roster.Add( o );
		var edges = seeder.Seed( roster, r );
		world.Relationships.AddRange( edges );
	}

	private static void BuildScenario( WorldState world, Rng r, TemplateLoader loader )
	{
		var sg = new ScenarioGenerator( loader.LoadScenarios() );
		var result = sg.Generate( world, r );
		world.NarrativeFlags.Add( $"scenario:{result.ScenarioId}" );
		// Seed mission body is built by Sprint 3's MissionGenerator on first cycle.
	}

	private static FactionKind ParseKind( string s )
		=> Enum.TryParse<FactionKind>( s, true, out var k ) ? k : FactionKind.RivalCorp;

	private static List<FactionTemplate> DefaultFactions() => new()
	{
		new FactionTemplate { Id = "host", Name = "{HOST}", Kind = "HostCorp", Standing = 0, Cash = 250000 },
		new FactionTemplate { Id = "rival_kasumi", Name = "Kasumi Dynamics", Kind = "RivalCorp", Standing = -25, Cash = 400000 },
		new FactionTemplate { Id = "razor", Name = "The Razor", Kind = "Agency", Standing = -50, Cash = 1000000 },
		new FactionTemplate { Id = "div_ops", Name = "Operations Division", Kind = "InternalDivision", Standing = 10 },
		new FactionTemplate { Id = "div_finance", Name = "Finance Division", Kind = "InternalDivision", Standing = -10 },
	};
}
