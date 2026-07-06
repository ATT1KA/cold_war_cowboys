using System;
using System.Collections.Generic;
using System.Linq;
using CWC.Core;
using CWC.Domain;
using CWC.Missions;

namespace CWC.Corporate;

public enum NegotiationLever
{
	MoreBudget,
	MoreTime,
	FewerConstraints,
	BetterReward,
}

public readonly record struct NegotiationResult( bool Accepted, string Reason, int CapitalSpent );

/// <summary>
/// Sprint 6 contract pipeline. Each cycle every faction surfaces one contract
/// whose quality scales with their relationship to the player. Hostile
/// factions still surface contracts — those are poisoned (hidden risk/exposure
/// surfaced via Tags). Active board directives also inject mandatory contracts.
/// </summary>
public sealed class ContractSystem
{
	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly CorporateConsequenceProcessor _consequences;

	public ContractSystem( Rng rng, EventBus bus, CorporateConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	public void RefreshContracts( CorporateState corp, WorldState world )
	{
		corp.AvailableContracts.RemoveAll( c =>
			c.Status == MissionStatus.Available &&
			( !c.IsMandatory || corp.Cycle > c.CycleDeadline ) );

		foreach ( var f in corp.Factions.Values )
		{
			if ( f.Kind == FactionKind.HostCorp ) continue;
			var c = GenerateContractFor( f, corp );
			if ( c != null ) corp.AvailableContracts.Add( c );
		}

		// Past "Effective", the gray market opens: contracts clean divisions
		// never see. High pay, wet work, real exposure — the other half of the
		// devil's bargain.
		if ( world.Corruption.CorruptionIndex >= 40 )
		{
			var brokers = corp.Factions.Values
				.Where( f => f.Kind != FactionKind.HostCorp && f.RelationshipToPlayer < 25 )
				.ToList();
			if ( brokers.Count > 0 && !corp.AvailableContracts.Any( m => m.Tags.Contains( "gray_market" ) ) )
			{
				var broker = brokers[_rng.Next( brokers.Count )];
				var black = GenerateGrayContract( broker, corp );
				corp.AvailableContracts.Add( black );
			}
		}

		foreach ( var d in corp.ActiveDirectives )
		{
			if ( d.Resolved ) continue;
			if ( corp.AvailableContracts.Any( m => m.IsBoardDirective && m.Title == d.Title ) ) continue;
			corp.AvailableContracts.Add( BuildDirectiveContract( d, corp ) );
		}
	}

	/// <summary>
	/// Spend political capital on better contract terms. Applies immediately —
	/// the player negotiates during Briefing/Assignment and the mission may
	/// resolve the same cycle, so a deferred consequence would arrive too late.
	/// </summary>
	public NegotiationResult Negotiate( CorporateState corp, Mission contract, NegotiationLever lever )
	{
		if ( contract.IsMandatory )
			return new NegotiationResult( false, "Board directives cannot be negotiated.", 0 );

		Faction? issuer = contract.IssuingFactionId != null
			&& corp.Factions.TryGetValue( contract.IssuingFactionId, out var f ) ? f : null;
		int rel = issuer?.RelationshipToPlayer ?? 0;
		int baseCost = lever switch
		{
			NegotiationLever.MoreBudget       => 2,
			NegotiationLever.MoreTime         => 1,
			NegotiationLever.FewerConstraints => 3,
			NegotiationLever.BetterReward     => 3,
			_                                 => 2,
		};
		int cost = Math.Max( 1, baseCost - rel / 25 );
		if ( corp.PoliticalCapital < cost )
			return new NegotiationResult( false, "Not enough political capital.", 0 );

		corp.PoliticalCapital = Math.Max( 0, corp.PoliticalCapital - cost );
		ApplyLever( contract, lever );
		corp.RecentEventLog.Add(
			$"[Negotiation] Spent {cost} capital negotiating {lever} on '{contract.Title}'." );

		return new NegotiationResult( true, $"Accepted at cost {cost}.", cost );
	}

	private Mission GenerateGrayContract( Faction broker, CorporateState corp )
	{
		int reward = 35_000 + _rng.Next( 0, 15_000 );
		var m = new Mission
		{
			Id = $"gray_{broker.Id}_{corp.Cycle}_{_rng.Next( 1000, 9999 )}",
			TemplateId = $"gray:{broker.Id}",
			Type = MissionType.Assassination,
			Status = MissionStatus.Available,
			Title = $"Off-ledger resolution ({broker.Name})",
			Briefing = $"No paper. No questions. {broker.Leader} pays on completion, in full, through three shells.",
			IssuingFactionId = broker.Id,
			ClientFactionId = broker.Id,
			IsWetWork = true,
			MoralWeight = 70,
			Difficulty = Math.Clamp( 55 + _rng.Next( 0, 20 ) + Math.Min( 20, corp.Cycle ), 5, 95 ),
			Reward = reward,
			Risk = 45,
			Exposure = 30,
			CycleAvailable = corp.Cycle,
			CycleDeadline = corp.Cycle + 2,
		};
		m.Tags.Add( "gray_market" );
		return m;
	}

	private static void ApplyLever( Mission contract, NegotiationLever lever )
	{
		switch ( lever )
		{
			case NegotiationLever.MoreBudget:       contract.Reward += 5_000; break;
			case NegotiationLever.MoreTime:         contract.Risk = Math.Max( 0, contract.Risk - 5 ); break;
			case NegotiationLever.FewerConstraints: contract.Exposure = Math.Max( 0, contract.Exposure - 5 ); break;
			case NegotiationLever.BetterReward:     contract.Reward += 10_000; contract.Risk += 5; break;
		}
	}

	private Mission? GenerateContractFor( Faction f, CorporateState corp )
	{
		double quality = (f.RelationshipToPlayer + 100) / 200.0; // 0..1

		string nameStem = f.Agenda switch
		{
			FactionAgenda.Predatory    => "Hostile takeover prep",
			FactionAgenda.Expansionist => "Market expansion sweep",
			FactionAgenda.Defensive    => "Asset protection contract",
			FactionAgenda.Cooperative  => "Joint operations brief",
			_                          => "Standard operations brief",
		};

		int reward = 10_000 + (int)(quality * 20_000) + _rng.Next( 0, 5_000 );
		int riskShown = Math.Max( 5, 40 - (int)(quality * 30) + _rng.Next( -5, 6 ) );
		int exposureShown = Math.Max( 0, 20 - (int)(quality * 15) );
		// Contract work hardens with the campaign like everything else — an
		// unscaled easy-money channel would dissolve all late-game pressure.
		int cycleBoost = Math.Min( 26, (corp.Cycle - 1) * 2 );

		var m = new Mission
		{
			Id = $"corp_{f.Id}_{corp.Cycle}_{_rng.Next( 1000, 9999 )}",
			TemplateId = $"corp:{f.Id}",
			Type = SuggestType( f.Agenda ),
			Status = MissionStatus.Available,
			Title = $"{nameStem} ({f.Name})",
			Briefing = $"Contract surfaced by {f.Name}. Issued by {f.Leader}.",
			IssuingFactionId = f.Id,
			ClientFactionId = f.Id,
			Difficulty = Math.Clamp( riskShown + 30 + cycleBoost, 5, 95 ),
			Reward = reward,
			Risk = riskShown,
			Exposure = exposureShown,
			CycleAvailable = corp.Cycle,
			CycleDeadline = corp.Cycle + 3,
		};

		if ( f.RelationshipToPlayer < -40 )
		{
			int hiddenRisk = _rng.Next( 10, 26 );
			int hiddenExposure = _rng.Next( 5, 16 );
			m.Tags.Add( $"hidden_risk:{hiddenRisk}" );
			m.Tags.Add( $"hidden_exposure:{hiddenExposure}" );
			m.OpposesFactionIds.AddRange( OtherFactionIds( corp, f.Id ) );
		}
		else if ( f.RelationshipToPlayer > 25 )
		{
			m.AlliedFactionIds.Add( f.Id );
		}

		return m;
	}

	private static MissionType SuggestType( FactionAgenda agenda ) => agenda switch
	{
		FactionAgenda.Predatory    => MissionType.Sabotage,
		FactionAgenda.Expansionist => MissionType.DataTheft,
		FactionAgenda.Defensive    => MissionType.Surveillance,
		FactionAgenda.Cooperative  => MissionType.CounterIntel,
		_                          => MissionType.Surveillance,
	};

	private static IEnumerable<string> OtherFactionIds( CorporateState corp, string excludeId )
	{
		foreach ( var id in corp.Factions.Keys )
			if ( id != excludeId ) yield return id;
	}

	private static Mission BuildDirectiveContract( BoardDirective d, CorporateState corp )
		=> new()
		{
			Id = $"directive_{d.Id}",
			TemplateId = $"directive:{d.Id}",
			Type = MissionType.CounterIntel,
			Status = MissionStatus.Available,
			Title = d.Title,
			Briefing = d.Description,
			IsBoardDirective = true,
			IsMandatory = d.Mandatory,
			Reward = 25_000,
			Risk = 30,
			Exposure = 15,
			Difficulty = Math.Clamp( 60 + Math.Min( 20, corp.Cycle ), 5, 95 ),
			CycleAvailable = corp.Cycle,
			CycleDeadline = corp.Cycle + 2,
		};
}
