using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CWC.Core;
using CWC.Domain;
using CWC.Game;

namespace CWC.Save;

/// <summary>
/// Save/load with JSON serialization, named slots, and auto-save.
///
/// Save files live in Data/Saves/. Each slot is a separate JSON file.
/// Auto-save writes to slot "autosave" at the start of each cycle (after
/// Briefing bookkeeping, so a load resumes a coherent cycle boundary).
/// Manual saves go to named slots (save_1, save_2, save_3).
///
/// Disk access goes through <see cref="CwcFiles"/> — FileSystem.Data in the
/// s&amp;box sandbox, plain disk in tests. No reflection, no raw System.IO.
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
			return CwcFiles.WriteAllText( GetPath( slotName ), json );
		}
		catch ( Exception e )
		{
			CwcLog.Warn( $"Save failed for slot '{slotName}': {e.Message}" );
			return false;
		}
	}

	/// <summary>Auto-save to the dedicated auto-save slot.</summary>
	public bool AutoSave( GameManager game ) => Save( game, AutoSaveSlot, true );

	// ========================================================================
	// LOAD
	// ========================================================================

	/// <summary>
	/// Load a save file. Returns the SaveData for restore/UI, or null on failure.
	/// </summary>
	public SaveData? Load( string slotName )
	{
		try
		{
			string? json = CwcFiles.ReadAllText( GetPath( slotName ) );
			if ( string.IsNullOrEmpty( json ) ) return null;
			return JsonSerializer.Deserialize<SaveData>( json, _opts );
		}
		catch ( Exception e )
		{
			CwcLog.Warn( $"Load failed for slot '{slotName}': {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Restore a SaveData into a GameManager's WorldState.
	/// Call after GameManager.NewGame() (which rebuilds templates), then this
	/// overwrites world state, rebuilds the corporate views over the restored
	/// objects, reattaches narrative sequences from templates, and restores
	/// director/corruption/RNG state.
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

		// Corporate scalars
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

		// Directives — active list AND the pending pool, so a load doesn't
		// fire the whole stack of deadline penalties at once.
		world.Corporate.ActiveDirectives.Clear();
		foreach ( var d in cs.ActiveDirectives )
			world.Corporate.ActiveDirectives.Add( ToDirective( d ) );
		world.Corporate.PendingDirectivePool.Clear();
		foreach ( var d in cs.PendingDirectivePool )
			world.Corporate.PendingDirectivePool.Add( ToDirective( d ) );

		world.Corporate.RecentEventLog.Clear();
		world.Corporate.RecentEventLog.AddRange( cs.RecentEventLog );

		// World metadata
		world.ProtagonistGender = data.ProtagonistGender;
		world.Day = data.Day;
		world.HeatLevel = data.HeatLevel;
		world.PublicTrust = data.PublicTrust;
		world.ConsecutiveSuccesses = data.ConsecutiveSuccesses;
		world.SeedMissionTemplateId = data.SeedMissionTemplateId;
		world.Seed = data.Seed;

		// Loads resume at the Briefing boundary of the saved cycle — the point
		// auto-saves are taken at. (CurrentPhase is recorded for diagnostics.)
		game.Phase.Reset();

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
				IsExecutive = os.IsExecutive,
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

		// Missions — full round-trip including weights, flags, contract
		// metadata; sequences reattach from templates by TemplateId.
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
				NarrativeFlagsOnSuccess = new List<string>( ms.NarrativeFlagsOnSuccess ),
				NarrativeFlagsOnPartialSuccess = new List<string>( ms.NarrativeFlagsOnPartialSuccess ),
				NarrativeFlagsOnFailure = new List<string>( ms.NarrativeFlagsOnFailure ),
				NarrativeFlagsOnCatastrophe = new List<string>( ms.NarrativeFlagsOnCatastrophe ),
				SuccessText = ms.SuccessText, PartialText = ms.PartialText,
				FailureText = ms.FailureText, CatastropheText = ms.CatastropheText,
				IssuingFactionId = ms.IssuingFactionId,
				OpposesFactionIds = new List<string>( ms.OpposesFactionIds ),
				AlliedFactionIds = new List<string>( ms.AlliedFactionIds ),
				IsBoardDirective = ms.IsBoardDirective,
				IsMandatory = ms.IsMandatory,
				Tags = new List<string>( ms.Tags ),
			};
			if ( Enum.TryParse<MissionType>( ms.Type, out var mt ) ) m.Type = mt;
			if ( Enum.TryParse<MissionStatus>( ms.Status, out var mst ) ) m.Status = mst;
			foreach ( var kv in ms.StatWeights )
				if ( Enum.TryParse<SkillKind>( kv.Key, true, out var sk ) )
					m.StatWeights[sk] = kv.Value;
			if ( ms.HasNarrativeSequence )
				m.NarrativeSequence = game.MissionGen.FindTemplate( ms.TemplateId )?.NarrativeSequence;
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
			f.Personality.AddRange( fs.Personality );
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

		// Choice history
		world.ChoiceLog.Clear();
		foreach ( var c in data.ChoiceLog )
		{
			world.ChoiceLog.Add( new ChoiceRecord
			{
				Cycle = c.Cycle, Source = c.Source, SourceId = c.SourceId,
				Label = c.Label, Flags = new List<string>( c.Flags ),
				OperativeId = c.OperativeId,
			} );
		}

		// The corp Roster/Factions views must point at the restored objects,
		// never pre-restore phantoms.
		game.RebuildCorporateViews();

		// Available contracts are views over world.Missions.
		world.Corporate.AvailableContracts.Clear();
		foreach ( var id in cs.AvailableContractIds )
		{
			var m = world.GetMission( id );
			if ( m != null ) world.Corporate.AvailableContracts.Add( m );
		}

		// One-shot scenes must not re-fire after a load.
		game.Director.RestoreFiredState( data.FiredOneShotScenes );

		// Corruption: index, the player's choice weight, and crossed milestones
		// (milestone scenes must not re-fire either).
		var crossed = new List<CorruptionTracker.Milestone>();
		foreach ( var name in data.CrossedMilestones )
			if ( Enum.TryParse<CorruptionTracker.Milestone>( name, out var ms2 ) )
				crossed.Add( ms2 );
		world.Corruption.Restore( data.CorruptionIndex, data.CorruptionChoiceWeight, crossed );

		// Long-lived RNG streams resume where they left off — determinism
		// survives the load.
		foreach ( var kv in data.RngStreams )
		{
			if ( game.PersistentStreams.TryGetValue( kv.Key, out var rng ) )
				rng.SetState( kv.Value );
		}

		// Recompute the live index against restored state.
		world.Corruption.Compute( world );
	}

	private static BoardDirective ToDirective( DirectiveSave d ) => new()
	{
		Id = d.Id,
		Title = d.Title,
		Description = d.Description,
		Mandatory = d.Mandatory,
		IgnoreConfidencePenalty = d.IgnoreConfidencePenalty,
		ComplyConfidenceReward = d.ComplyConfidenceReward,
		DeadlineDay = d.DeadlineDay,
		DeadlineDayOffset = d.DeadlineDayOffset,
		Resolved = d.Resolved,
		Complied = d.Complied,
	};

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
		=> CwcFiles.DeleteFile( GetPath( slotName ) );

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
			ConsecutiveSuccesses = world.ConsecutiveSuccesses,
			SeedMissionTemplateId = world.SeedMissionTemplateId,
			CurrentPhase = game.Phase.CurrentPhase.ToString(),
			CorruptionIndex = world.Corruption.CorruptionIndex,
			CorruptionChoiceWeight = world.Corruption.ChoiceWeight,
			CurrentMilestone = world.Corruption.CurrentMilestone.ToString(),
			CrossedMilestones = world.Corruption.CrossedEver.Select( m => m.ToString() ).ToList(),
			FiredOneShotScenes = game.Director.FiredOneShots.ToList(),
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
			ActiveDirectives = c.ActiveDirectives.Select( ToDirectiveSave ).ToList(),
			PendingDirectivePool = c.PendingDirectivePool.Select( ToDirectiveSave ).ToList(),
			AvailableContractIds = c.AvailableContracts.Select( m => m.Id ).ToList(),
			RecentEventLog = new List<string>( c.RecentEventLog ),
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
				IsExecutive = op.IsExecutive,
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
				StatWeights = m.StatWeights.ToDictionary( kv => kv.Key.ToString(), kv => kv.Value ),
				NarrativeFlagsOnSuccess = new List<string>( m.NarrativeFlagsOnSuccess ),
				NarrativeFlagsOnPartialSuccess = new List<string>( m.NarrativeFlagsOnPartialSuccess ),
				NarrativeFlagsOnFailure = new List<string>( m.NarrativeFlagsOnFailure ),
				NarrativeFlagsOnCatastrophe = new List<string>( m.NarrativeFlagsOnCatastrophe ),
				SuccessText = m.SuccessText, PartialText = m.PartialText,
				FailureText = m.FailureText, CatastropheText = m.CatastropheText,
				IssuingFactionId = m.IssuingFactionId,
				OpposesFactionIds = new List<string>( m.OpposesFactionIds ),
				AlliedFactionIds = new List<string>( m.AlliedFactionIds ),
				IsBoardDirective = m.IsBoardDirective,
				IsMandatory = m.IsMandatory,
				Tags = new List<string>( m.Tags ),
				HasNarrativeSequence = m.NarrativeSequence != null,
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
				Personality = new List<string>( f.Personality ),
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

		// Narrative flags + choice history
		data.NarrativeFlags = world.NarrativeFlags.ToList();
		data.ChoiceLog = world.ChoiceLog.Select( ch => new ChoiceRecordSave
		{
			Cycle = ch.Cycle, Source = ch.Source, SourceId = ch.SourceId,
			Label = ch.Label, Flags = new List<string>( ch.Flags ),
			OperativeId = ch.OperativeId,
		} ).ToList();

		// Crises
		data.ActiveCrises = new List<string>( world.ActiveCrises );

		// Long-lived RNG streams
		foreach ( var kv in game.PersistentStreams )
			data.RngStreams[kv.Key] = kv.Value.GetState();

		return data;
	}

	private static DirectiveSave ToDirectiveSave( BoardDirective d ) => new()
	{
		Id = d.Id,
		Title = d.Title,
		Description = d.Description,
		Mandatory = d.Mandatory,
		IgnoreConfidencePenalty = d.IgnoreConfidencePenalty,
		ComplyConfidenceReward = d.ComplyConfidenceReward,
		DeadlineDay = d.DeadlineDay,
		DeadlineDayOffset = d.DeadlineDayOffset,
		Resolved = d.Resolved,
		Complied = d.Complied,
	};

	// ========================================================================
	// PATHS
	// ========================================================================

	private string GetPath( string slotName )
		=> $"{_saveDir}/{slotName}.json";
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
