using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CWC.Core;
using CWC.Domain;
using CWC.Game;

namespace CWC.Save;

/// <summary>
/// Night 6: Save/load with JSON serialization, named slots, and auto-save.
///
/// Save files live in Data/Saves/. Each slot is a separate JSON file.
/// Auto-save writes to slot "autosave" at the end of each cycle.
/// Manual saves go to named slots (save_1, save_2, save_3).
///
/// The system snapshots WorldState into flat DTOs (SaveData) and
/// reconstructs WorldState on load. Engine-coupled state (templates,
/// director queue) is re-initialized from templates on load.
/// </summary>
public sealed class SaveSystem
{
	public const int MaxSlots = 3;
	public const string AutoSaveSlot = "autosave";

	private static readonly JsonSerializerOptions _opts = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
	};

	private readonly string _saveDir;

	public SaveSystem( string saveDir = "Data/Saves" )
	{
		_saveDir = saveDir;
	}

	// ========================================================================
	// SAVE
	// ========================================================================

	/// <summary>
	/// Save the current game state to a named slot.
	/// </summary>
	public bool Save( GameManager game, string slotName, bool isAutoSave = false )
	{
		try
		{
			var data = Snapshot( game, slotName, isAutoSave );
			string json = JsonSerializer.Serialize( data, _opts );
			WriteToDisk( slotName, json );
			return true;
		}
		catch { return false; }
	}

	/// <summary>Auto-save to the dedicated auto-save slot.</summary>
	public bool AutoSave( GameManager game ) => Save( game, AutoSaveSlot, true );

	// ========================================================================
	// LOAD
	// ========================================================================

	/// <summary>
	/// Load a save file and restore it into the GameManager.
	/// Returns the loaded SaveData for UI display, or null on failure.
	/// </summary>
	public SaveData? Load( string slotName )
	{
		try
		{
			string json = ReadFromDisk( slotName );
			if ( string.IsNullOrEmpty( json ) ) return null;
			return JsonSerializer.Deserialize<SaveData>( json, _opts );
		}
		catch { return null; }
	}

	/// <summary>
	/// Restore a SaveData into a GameManager's WorldState.
	/// Call after GameManager.NewGame() to re-initialize templates,
	/// then overwrite the world state.
	/// </summary>
	public static void Restore( SaveData data, GameManager game )
	{
		var world = game.World;

		// Setting
		world.Setting.CorpName = data.Setting.CorpName;
		world.Setting.Location = data.Setting.Location;
		world.Setting.Year = data.Setting.Year;
		world.Setting.EraTagline = data.Setting.EraTagline;
		world.Setting.ToneTags = new List<string>( data.Setting.ToneTags );

		// Corporate
		var cs = data.Corporate;
		world.Corporate.Heat = cs.Heat;
		world.Corporate.Suspicion = cs.Suspicion;
		world.Corporate.PoliticalPressure = cs.PoliticalPressure;
		world.Corporate.Reputation = cs.Reputation;
		world.Corporate.Budget = cs.Budget;
		world.Corporate.Cycle = cs.Cycle;
		world.Corporate.BoardConfidence = cs.BoardConfidence;
		world.Corporate.InternalReputation = cs.InternalReputation;
		world.Corporate.ExternalReputation = cs.ExternalReputation;
		world.Corporate.PoliticalCapital = cs.PoliticalCapital;
		world.Corporate.DirectorName = cs.DirectorName;
		world.Corporate.DirectorAgenda = cs.DirectorAgenda;
		if ( Enum.TryParse<CorporateRank>( cs.Rank, out var rank ) )
			world.Corporate.Rank = rank;

		// World metadata
		world.ProtagonistGender = data.ProtagonistGender;
		world.Day = data.Day;
		world.HeatLevel = data.HeatLevel;
		world.PublicTrust = data.PublicTrust;
		world.SeedMissionTemplateId = data.SeedMissionTemplateId;
		world.Seed = data.Seed;

		// Phase
		if ( Enum.TryParse<CyclePhase>( data.CurrentPhase, out var phase ) )
			game.Phase.Reset(); // Will be set to Briefing; load always resumes at Briefing

		// Operatives
		world.Operatives.Clear();
		foreach ( var os in data.Operatives )
		{
			var op = new Operative
			{
				Id = os.Id, Name = os.Name, Codename = os.Codename,
				Archetype = os.Archetype, Background = os.Background,
				Gender = os.Gender, NarrativeRole = os.NarrativeRole,
				Tenure = os.Tenure, CurrentMissionId = os.CurrentMissionId,
				FactionLoyalty = os.FactionLoyalty,
				Tags = new List<string>( os.Tags ),
			};
			if ( Enum.TryParse<OperativeStatus>( os.Status, out var st ) )
				op.Status = st;
			op.Skills.Combat = os.Combat;
			op.Skills.Stealth = os.Stealth;
			op.Skills.Hacking = os.Hacking;
			op.Skills.Deception = os.Deception;
			op.Skills.Intimidation = os.Intimidation;
			op.Skills.Persuasion = os.Persuasion;
			op.Psychology.Loyalty = os.Loyalty;
			op.Psychology.Stress = os.Stress;
			op.Psychology.Morale = os.Morale;
			op.Psychology.Conscience = os.Conscience;
			op.Psychology.Ambition = os.Ambition;
			op.Psychology.WetWorkCount = os.WetWorkCount;
			foreach ( var ts in os.Traits )
			{
				var trait = new Trait { Id = ts.Id, Name = ts.Name, Description = ts.Description };
				if ( Enum.TryParse<TraitAxis>( ts.Axis, out var ax ) ) trait.Axis = ax;
				op.Traits.Add( trait );
			}
			world.Operatives.Add( op );
		}

		// Missions
		world.Missions.Clear();
		foreach ( var ms in data.Missions )
		{
			var m = new Mission
			{
				Id = ms.Id, TemplateId = ms.TemplateId,
				Title = ms.Title, Briefing = ms.Briefing,
				Difficulty = ms.Difficulty, MoralWeight = ms.MoralWeight,
				IsWetWork = ms.IsWetWork,
				CycleAvailable = ms.CycleAvailable, CycleDeadline = ms.CycleDeadline,
				AssignedOperativeIds = new List<int>( ms.AssignedOperativeIds ),
				ClientFactionId = ms.ClientFactionId,
				TargetFactionId = ms.TargetFactionId,
				Reward = ms.Reward, Risk = ms.Risk, Exposure = ms.Exposure,
			};
			if ( Enum.TryParse<MissionType>( ms.Type, out var mt ) ) m.Type = mt;
			if ( Enum.TryParse<MissionStatus>( ms.Status, out var mst ) ) m.Status = mst;
			world.Missions.Add( m );
		}

		// Factions
		world.Factions.Clear();
		foreach ( var fs in data.Factions )
		{
			var f = new Faction
			{
				Id = fs.Id, Name = fs.Name,
				Standing = fs.Standing, Reputation = fs.Reputation,
				Cash = fs.Cash, Leader = fs.Leader,
				RelationshipToPlayer = fs.RelationshipToPlayer,
			};
			if ( Enum.TryParse<FactionKind>( fs.Kind, out var fk ) ) f.Kind = fk;
			if ( Enum.TryParse<FactionAgenda>( fs.Agenda, out var fa ) ) f.Agenda = fa;
			world.Factions.Add( f );
		}

		// Relationships
		world.Relationships.Clear();
		foreach ( var rs in data.Relationships )
		{
			var r = new Relationship { FromId = rs.FromId, ToId = rs.ToId, Score = rs.Score };
			if ( Enum.TryParse<RelationshipKind>( rs.Kind, out var rk ) ) r.Kind = rk;
			world.Relationships.Add( r );
		}

		// Narrative flags
		world.NarrativeFlags.Clear();
		foreach ( var f in data.NarrativeFlags )
			world.NarrativeFlags.Add( f );

		// Recompute corruption from restored state
		world.Corruption.Compute( world );
	}

	// ========================================================================
	// SLOT MANAGEMENT
	// ========================================================================

	/// <summary>List all available save slots with metadata.</summary>
	public List<SaveSlotInfo> ListSlots()
	{
		var slots = new List<SaveSlotInfo>();
		var names = new[] { "save_1", "save_2", "save_3", AutoSaveSlot };

		foreach ( var name in names )
		{
			var data = Load( name );
			slots.Add( new SaveSlotInfo
			{
				SlotName = name,
				IsOccupied = data != null,
				IsAutoSave = name == AutoSaveSlot,
				Summary = data?.DisplaySummary ?? "",
				Timestamp = data?.Timestamp ?? DateTime.MinValue,
				Cycle = data?.Corporate.Cycle ?? 0,
			} );
		}
		return slots;
	}

	/// <summary>Delete a save slot.</summary>
	public bool DeleteSlot( string slotName )
	{
		try
		{
			string path = GetPath( slotName );
			if ( File.Exists( path ) ) { File.Delete( path ); return true; }
			return false;
		}
		catch { return false; }
	}

	// ========================================================================
	// SNAPSHOT — WorldState → SaveData
	// ========================================================================

	private static SaveData Snapshot( GameManager game, string slotName, bool isAutoSave )
	{
		var world = game.World;
		var data = new SaveData
		{
			SaveId = Guid.NewGuid().ToString( "N" ),
			SlotName = slotName,
			Timestamp = DateTime.UtcNow,
			IsAutoSave = isAutoSave,
			Seed = world.Seed,
			ProtagonistGender = world.ProtagonistGender,
			Day = world.Day,
			HeatLevel = world.HeatLevel,
			PublicTrust = world.PublicTrust,
			SeedMissionTemplateId = world.SeedMissionTemplateId,
			CurrentPhase = game.Phase.CurrentPhase.ToString(),
			CorruptionIndex = world.Corruption.CorruptionIndex,
			CurrentMilestone = world.Corruption.CurrentMilestone.ToString(),
		};

		// Setting
		data.Setting = new WorldSettingSave
		{
			CorpName = world.Setting.CorpName,
			Location = world.Setting.Location,
			Year = world.Setting.Year,
			EraTagline = world.Setting.EraTagline,
			ToneTags = new List<string>( world.Setting.ToneTags ),
		};

		// Corporate
		var c = world.Corporate;
		data.Corporate = new CorporateStateSave
		{
			Heat = c.Heat, Suspicion = c.Suspicion,
			PoliticalPressure = c.PoliticalPressure,
			Reputation = c.Reputation, Budget = c.Budget, Cycle = c.Cycle,
			Rank = c.Rank.ToString(),
			BoardConfidence = c.BoardConfidence,
			InternalReputation = c.InternalReputation,
			ExternalReputation = c.ExternalReputation,
			PoliticalCapital = c.PoliticalCapital,
			DirectorName = c.DirectorName,
			DirectorAgenda = c.DirectorAgenda,
		};

		// Operatives
		foreach ( var op in world.Operatives )
		{
			data.Operatives.Add( new OperativeSave
			{
				Id = op.Id, Name = op.Name, Codename = op.Codename,
				Archetype = op.Archetype, Background = op.Background,
				Gender = op.Gender, NarrativeRole = op.NarrativeRole,
				Status = op.Status.ToString(), Tenure = op.Tenure,
				CurrentMissionId = op.CurrentMissionId,
				FactionLoyalty = op.FactionLoyalty,
				Tags = new List<string>( op.Tags ),
				Combat = op.Skills.Combat, Stealth = op.Skills.Stealth,
				Hacking = op.Skills.Hacking, Deception = op.Skills.Deception,
				Intimidation = op.Skills.Intimidation, Persuasion = op.Skills.Persuasion,
				Loyalty = op.Psychology.Loyalty, Stress = op.Psychology.Stress,
				Morale = op.Psychology.Morale, Conscience = op.Psychology.Conscience,
				Ambition = op.Psychology.Ambition, WetWorkCount = op.Psychology.WetWorkCount,
				Traits = op.Traits.Select( t => new TraitSave
				{
					Id = t.Id, Name = t.Name,
					Axis = t.Axis.ToString(), Description = t.Description,
				} ).ToList(),
			} );
		}

		// Missions
		foreach ( var m in world.Missions )
		{
			data.Missions.Add( new MissionSave
			{
				Id = m.Id, TemplateId = m.TemplateId,
				Type = m.Type.ToString(), Status = m.Status.ToString(),
				Title = m.Title, Briefing = m.Briefing,
				Difficulty = m.Difficulty, MoralWeight = m.MoralWeight,
				IsWetWork = m.IsWetWork,
				CycleAvailable = m.CycleAvailable, CycleDeadline = m.CycleDeadline,
				AssignedOperativeIds = new List<int>( m.AssignedOperativeIds ),
				ClientFactionId = m.ClientFactionId,
				TargetFactionId = m.TargetFactionId,
				Reward = m.Reward, Risk = m.Risk, Exposure = m.Exposure,
			} );
		}

		// Factions
		foreach ( var f in world.Factions )
		{
			data.Factions.Add( new FactionSave
			{
				Id = f.Id, Name = f.Name, Kind = f.Kind.ToString(),
				Standing = f.Standing, Reputation = f.Reputation,
				Cash = f.Cash, Agenda = f.Agenda.ToString(),
				Leader = f.Leader, RelationshipToPlayer = f.RelationshipToPlayer,
			} );
		}

		// Relationships
		foreach ( var r in world.Relationships )
		{
			data.Relationships.Add( new RelationshipSave
			{
				FromId = r.FromId, ToId = r.ToId,
				Kind = r.Kind.ToString(), Score = r.Score,
			} );
		}

		// Narrative flags
		data.NarrativeFlags = world.NarrativeFlags.ToList();

		// Crises
		data.ActiveCrises = new List<string>( world.ActiveCrises );

		return data;
	}

	// ========================================================================
	// DISK I/O — abstracted for S&box / test portability
	// ========================================================================

	private string GetPath( string slotName )
		=> Path.Combine( _saveDir, $"{slotName}.json" );

	private void WriteToDisk( string slotName, string json )
	{
		// Try S&box filesystem first, fall back to disk
		string path = GetPath( slotName );
		try
		{
			var fsType = Type.GetType( "Sandbox.FileSystem, Sandbox.System" )
				?? Type.GetType( "Sandbox.FileSystem" );
			if ( fsType != null )
			{
				var mountedProp = fsType.GetProperty( "Mounted",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
				if ( mountedProp != null )
				{
					var mounted = mountedProp.GetValue( null );
					if ( mounted != null )
					{
						var createDir = mounted.GetType().GetMethod( "CreateDirectory", new[] { typeof( string ) } );
						var writeMethod = mounted.GetType().GetMethod( "WriteAllText", new[] { typeof( string ), typeof( string ) } );
						if ( createDir != null && writeMethod != null )
						{
							createDir.Invoke( mounted, new object[] { _saveDir } );
							writeMethod.Invoke( mounted, new object[] { path, json } );
							return;
						}
					}
				}
			}
		}
		catch { /* fall through to disk */ }

		// Disk fallback
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllText( path, json );
	}

	private string ReadFromDisk( string slotName )
	{
		string path = GetPath( slotName );
		try
		{
			var fsType = Type.GetType( "Sandbox.FileSystem, Sandbox.System" )
				?? Type.GetType( "Sandbox.FileSystem" );
			if ( fsType != null )
			{
				var mountedProp = fsType.GetProperty( "Mounted",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
				if ( mountedProp != null )
				{
					var mounted = mountedProp.GetValue( null );
					if ( mounted != null )
					{
						var existsMethod = mounted.GetType().GetMethod( "FileExists", new[] { typeof( string ) } );
						var readMethod = mounted.GetType().GetMethod( "ReadAllText", new[] { typeof( string ) } );
						if ( existsMethod != null && readMethod != null )
						{
							bool exists = (bool)( existsMethod.Invoke( mounted, new object[] { path } ) ?? false );
							if ( exists ) return readMethod.Invoke( mounted, new object[] { path } ) as string ?? "";
						}
					}
				}
			}
		}
		catch { /* fall through to disk */ }

		// Disk fallback
		if ( File.Exists( path ) ) return File.ReadAllText( path );
		return "";
	}
}

/// <summary>Metadata for a save slot, used by the save/load UI.</summary>
public sealed class SaveSlotInfo
{
	public string SlotName { get; set; } = "";
	public bool IsOccupied { get; set; }
	public bool IsAutoSave { get; set; }
	public string Summary { get; set; } = "";
	public DateTime Timestamp { get; set; }
	public int Cycle { get; set; }

	public string DisplayName => SlotName == SaveSystem.AutoSaveSlot
		? "Auto-Save"
		: $"Slot {SlotName.Replace( "save_", "" )}";
}
