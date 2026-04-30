using System.Collections.Generic;
using System.Linq;
using CWC.Domain;

namespace CWC.Core;

/// <summary>
/// Single source of truth for the run. Mutated only by ConsequenceProcessor (Sprint 3+).
/// </summary>
public sealed class WorldState
{
	public ulong Seed { get; set; }
	public WorldSetting Setting { get; set; } = new();
	public CorporateState Corporate { get; set; } = new();

	public List<Operative> Operatives { get; } = new();
	public List<Mission> Missions { get; } = new();
	public List<Faction> Factions { get; } = new();
	public List<Relationship> Relationships { get; } = new();

	public HashSet<string> NarrativeFlags { get; } = new();

	public Operative? GetOperative( int id ) => Operatives.FirstOrDefault( o => o.Id == id );
	public Mission? GetMission( string id ) => Missions.FirstOrDefault( m => m.Id == id );
	public Faction? GetFaction( string id ) => Factions.FirstOrDefault( f => f.Id == id );

	public IEnumerable<Relationship> GetRelationshipsFrom( int operativeId )
		=> Relationships.Where( r => r.FromId == operativeId );

	public Relationship? GetRelationshipBetween( int fromId, int toId )
		=> Relationships.FirstOrDefault( r => r.FromId == fromId && r.ToId == toId );
}
