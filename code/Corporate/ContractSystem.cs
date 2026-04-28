using System;
using System.Collections.Generic;
using ColdWarCowboys.Core;
using ColdWarCowboys.Factions;
using ColdWarCowboys.Missions;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Corporate;

/// <summary>The lever a player can pull when negotiating a contract.</summary>
public enum NegotiationLever
{
	MoreBudget,
	MoreTime,
	FewerConstraints,
	BetterReward,
}

/// <summary>Result of a negotiation attempt.</summary>
public readonly record struct NegotiationResult( bool Accepted, string Reason, int CapitalSpent );

/// <summary>
/// Sprint 6 contract pipeline. Each cycle, every faction surfaces one
/// contract whose quality scales with their relationship to the player. Low
/// relationships produce poisoned contracts (look good, hidden complications
/// surface as mission Risk/Exposure that don't show in the brief). The board
/// also injects mandatory directive contracts.
/// </summary>
public sealed class ContractSystem
{
	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly ConsequenceProcessor _consequences;

	public ContractSystem( Rng rng, EventBus bus, ConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	/// <summary>Drops yesterday's contracts and rolls today's.</summary>
	public void RefreshContracts( CorporateState corp )
	{
		corp.AvailableContracts.RemoveAll( c => !c.IsMandatory && c.Outcome == MissionOutcome.Pending );

		foreach ( var f in corp.Factions.Values )
		{
			var c = GenerateContractFor( f, corp );
			if ( c != null ) corp.AvailableContracts.Add( c );
		}

		// Any active directive that doesn't already have a contract gets one.
		foreach ( var d in corp.ActiveDirectives )
		{
			if ( d.Resolved ) continue;
			if ( corp.AvailableContracts.Exists( m => m.IsBoardDirective && m.Name == d.Title ) ) continue;
			corp.AvailableContracts.Add( BuildDirectiveContract( d ) );
		}
	}

	/// <summary>
	/// Tries to negotiate. Higher faction relationships succeed cheaper.
	/// Mandatory directive contracts can't be renegotiated.
	/// </summary>
	public NegotiationResult Negotiate( CorporateState corp, Mission contract, NegotiationLever lever )
	{
		if ( contract.IsMandatory )
			return new NegotiationResult( false, "Board directives cannot be negotiated.", 0 );

		Faction? issuer = contract.IssuingFactionId != null && corp.Factions.TryGetValue( contract.IssuingFactionId, out var f ) ? f : null;
		int rel = issuer?.RelationshipToPlayer ?? 0;
		int baseCost = lever switch
		{
			NegotiationLever.MoreBudget => 2,
			NegotiationLever.MoreTime => 1,
			NegotiationLever.FewerConstraints => 3,
			NegotiationLever.BetterReward => 3,
			_ => 2,
		};
		// Friendly factions discount, hostile factions surcharge.
		int cost = Math.Max( 1, baseCost - rel / 25 );
		if ( corp.PoliticalCapital < cost )
		{
			return new NegotiationResult( false, "Not enough political capital.", 0 );
		}

		_consequences.Enqueue( new Consequence
		{
			Source = "Negotiation",
			Description = $"Spent {cost} capital negotiating {lever} on '{contract.Name}'.",
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
			case NegotiationLever.MoreBudget: contract.Reward += 50; break;
			case NegotiationLever.MoreTime: contract.Risk = Math.Max( 0, contract.Risk - 5 ); break;
			case NegotiationLever.FewerConstraints: contract.Exposure = Math.Max( 0, contract.Exposure - 5 ); break;
			case NegotiationLever.BetterReward: contract.Reward += 100; contract.Risk += 5; break;
		}
	}

	private Mission? GenerateContractFor( Faction f, CorporateState corp )
	{
		// Hostile factions still surface contracts — they're poisoned, not absent.
		double quality = (f.RelationshipToPlayer + 100) / 200.0; // 0..1

		var name = f.Agenda switch
		{
			FactionAgenda.Predatory => "Hostile takeover prep",
			FactionAgenda.Expansionist => "Market expansion sweep",
			FactionAgenda.Defensive => "Asset protection contract",
			FactionAgenda.Cooperative => "Joint operations brief",
			_ => "Standard operations brief",
		};

		int reward = 100 + (int)(quality * 200) + _rng.Range( 0, 50 );
		int riskShown = Math.Max( 5, 40 - (int)(quality * 30) + _rng.Range( -5, 6 ) );
		int exposureShown = Math.Max( 0, 20 - (int)(quality * 15) );

		var m = new Mission
		{
			Name = $"{name} ({f.Name})",
			Description = $"Contract surfaced by {f.Name}. Issued by {f.Leader}.",
			IssuingFactionId = f.Id,
			Reward = reward,
			Risk = riskShown,
			Exposure = exposureShown,
		};

		// Poisoned contracts: low-relationship factions hide extra Risk/Exposure
		// that surface during execution. We attach the hidden modifiers as tags
		// for MissionResolver to pick up without leaking them in the brief.
		if ( f.RelationshipToPlayer < -25 )
		{
			int hiddenRisk = _rng.Range( 10, 25 );
			int hiddenExposure = _rng.Range( 5, 15 );
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

	private static IEnumerable<string> OtherFactionIds( CorporateState corp, string excludeId )
	{
		foreach ( var id in corp.Factions.Keys )
		{
			if ( id != excludeId ) yield return id;
		}
	}

	private static Mission BuildDirectiveContract( BoardDirective d )
	{
		return new Mission
		{
			Name = d.Title,
			Description = d.Description,
			IsBoardDirective = true,
			IsMandatory = d.Mandatory,
			Reward = 200,
			Risk = 30,
			Exposure = 15,
		};
	}
}
