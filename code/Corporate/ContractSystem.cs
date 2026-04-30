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

	public void RefreshContracts( CorporateState corp )
	{
		corp.AvailableContracts.RemoveAll( c => !c.IsMandatory && c.Status == MissionStatus.Available );

		foreach ( var f in corp.Factions.Values )
		{
			if ( f.Kind == FactionKind.HostCorp ) continue;
			var c = GenerateContractFor( f, corp );
			if ( c != null ) corp.AvailableContracts.Add( c );
		}

		foreach ( var d in corp.ActiveDirectives )
		{
			if ( d.Resolved ) continue;
			if ( corp.AvailableContracts.Any( m => m.IsBoardDirective && m.Title == d.Title ) ) continue;
			corp.AvailableContracts.Add( BuildDirectiveContract( d, corp ) );
		}
	}

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

		_consequences.Enqueue( new CorporateConsequence
		{
			Source = "Negotiation",
			Description = $"Spent {cost} capital negotiating {lever} on '{contract.Title}'.",
			Apply = c =>
			{
				c.PoliticalCapital = Math.Max( 0, c.PoliticalCapital - cost );
				ApplyLever( contract, lever );
			},
		} );

		return new NegotiationResult( true, $"Accepted at cost {cost}.", cost );
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
			Difficulty = Math.Clamp( riskShown + 30, 5, 95 ),
			Reward = reward,
			Risk = riskShown,
			Exposure = exposureShown,
			CycleAvailable = corp.Cycle,
			CycleDeadline = corp.Cycle + 3,
		};

		if ( f.RelationshipToPlayer < -25 )
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
			Difficulty = 60,
			CycleAvailable = corp.Cycle,
			CycleDeadline = corp.Cycle + 2,
		};
}
