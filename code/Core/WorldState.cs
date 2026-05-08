using System.Collections.Generic;
using System.Linq;
using CWC.Domain;

namespace CWC.Core;

/// <summary>
/// Single source of truth for the run. Mutated only by ConsequenceProcessor (Sprint 3+).
/// Sprint 6 adds Day, HeatLevel, PublicTrust to drive event-generation gating.
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

	// ---- Sprint 6: world-tick metadata ----
	/// <summary>Scenario seed mission template id, set by WorldGenerator.BuildScenario.</summary>
	public string SeedMissionTemplateId { get; set; } = "extraction_defector";

	/// <summary>Night 3: protagonist gender for token resolution. Values: "m", "f", "nb".</summary>
	public string ProtagonistGender { get; set; } = "m";

	/// <summary>Night 5: corruption tracker — computed each cycle, drives milestones + UI.</summary>
	public CorruptionTracker Corruption { get; set; } = new();

	public int Day { get; set; } = 1;
	public int HeatLevel { get; set; } = 0;
	public int PublicTrust { get; set; } = 50;
	public List<string> ActiveCrises { get; } = new();
	public List<string> Headlines { get; } = new();

	public Operative? GetOperative( int id ) => Operatives.FirstOrDefault( o => o.Id == id );
	public Mission? GetMission( string id ) => Missions.FirstOrDefault( m => m.Id == id );
	public Faction? GetFaction( string id ) => Factions.FirstOrDefault( f => f.Id == id );

	public IEnumerable<Relationship> GetRelationshipsFrom( int operativeId )
		=> Relationships.Where( r => r.FromId == operativeId );

	public Relationship? GetRelationshipBetween( int fromId, int toId )
		=> Relationships.FirstOrDefault( r => r.FromId == fromId && r.ToId == toId );
}
