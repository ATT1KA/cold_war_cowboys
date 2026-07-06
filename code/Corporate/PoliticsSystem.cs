using System;
using System.Linq;
using CWC.Core;
using CWC.Missions;

namespace CWC.Corporate;

public readonly record struct RankChanged( CorporateRank From, CorporateRank To, string Reason );

public readonly record struct DirectiveResolved( string DirectiveId, bool Complied, int ConfidenceDelta );

/// <summary>
/// Sprint 6 board-level dynamics. EvaluateBoard runs once per cycle; it
/// reviews directive compliance, recomputes board confidence, considers
/// promotion/demotion, and lets the Director nudge confidence according to
/// their personal agenda.
/// </summary>
public sealed class PoliticsSystem
{
	private const int PromoteThreshold = 80;
	private const int DemoteThreshold = 20;

	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly CorporateConsequenceProcessor _consequences;

	public PoliticsSystem( Rng rng, EventBus bus, CorporateConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	public void EvaluateBoard( CorporateState corp, WorldState world )
	{
		ReviewDirectives( corp, world );
		IssueFromPool( corp, world );
		ApplyDirectorAgenda( corp );
		ConsiderRankChange( corp );
	}

	public void OnMissionResolved( CorporateState corp, MissionResolved result )
	{
		int delta = result.Outcome switch
		{
			MissionOutcome.Success         => +5,
			MissionOutcome.PartialSuccess  => +1,
			MissionOutcome.Failure         => -4,
			MissionOutcome.Catastrophe     => -10,
			_                              => 0,
		};
		if ( result.Mission.IsBoardDirective ) delta *= 2;
		// The board pays a premium for decisive (wet) work — this is half of
		// the devil's bargain the corruption arc is built on.
		if ( result.Mission.IsWetWork && result.Outcome == MissionOutcome.Success ) delta += 2;

		// Directive compliance: completing the directive's mission marks it
		// complied and pays the reward — succeeding must never still eat the
		// non-compliance penalty.
		if ( result.Mission.IsBoardDirective
			&& result.Mission.TemplateId.StartsWith( "directive:" )
			&& result.Outcome is MissionOutcome.Success or MissionOutcome.PartialSuccess )
		{
			string directiveId = result.Mission.TemplateId.Substring( "directive:".Length );
			var directive = corp.ActiveDirectives.Find( d => d.Id == directiveId && !d.Resolved );
			if ( directive != null )
			{
				directive.Resolved = true;
				directive.Complied = true;
				int reward = directive.ComplyConfidenceReward;
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = "Board",
					Description = $"Directive '{directive.Title}' fulfilled (+{reward} confidence).",
					Apply = c => c.BoardConfidence = Math.Clamp( c.BoardConfidence + reward, 0, 100 ),
				} );
				_bus.Publish( new DirectiveResolved( directive.Id, true, reward ) );
			}
		}

		if ( delta == 0 ) return;

		_consequences.Enqueue( new CorporateConsequence
		{
			Source = "Board",
			Description = $"Confidence {(delta >= 0 ? "+" : "")}{delta} ({result.Mission.Title})",
			Apply = c => c.BoardConfidence = Math.Clamp( c.BoardConfidence + delta, 0, 100 ),
		} );
	}

	/// <summary>
	/// The board drip-feeds directives from the pending pool over the run
	/// instead of dumping them all on cycle 1.
	/// </summary>
	private void IssueFromPool( CorporateState corp, WorldState world )
	{
		int unresolved = corp.ActiveDirectives.Count( d => !d.Resolved );
		if ( unresolved >= 2 ) return;
		if ( corp.PendingDirectivePool.Count == 0 ) return;
		if ( !_rng.Chance( 0.4 ) ) return;

		var directive = corp.PendingDirectivePool[0];
		corp.PendingDirectivePool.RemoveAt( 0 );
		directive.DeadlineDay = world.Day + directive.DeadlineDayOffset;
		IssueDirective( corp, directive );
	}

	public void IssueDirective( CorporateState corp, BoardDirective directive )
	{
		corp.ActiveDirectives.Add( directive );
		_consequences.Enqueue( new CorporateConsequence
		{
			Source = "Board",
			Description = $"New directive issued: {directive.Title}",
			Apply = _ => { },
		} );
	}

	private void ReviewDirectives( CorporateState corp, WorldState world )
	{
		foreach ( var d in corp.ActiveDirectives.ToArray() )
		{
			if ( d.Resolved ) continue;
			if ( world.Day < d.DeadlineDay ) continue;

			d.Resolved = true;
			int delta = -d.IgnoreConfidencePenalty;
			_consequences.Enqueue( new CorporateConsequence
			{
				Source = "Board",
				Description = $"Directive '{d.Title}' deadline reached without compliance.",
				Apply = c => c.BoardConfidence = Math.Clamp( c.BoardConfidence + delta, 0, 100 ),
			} );
			_bus.Publish( new DirectiveResolved( d.Id, false, delta ) );
		}
	}

	private void ApplyDirectorAgenda( CorporateState corp )
	{
		// Director nudges confidence ±1-3 to model boss-bias on top of board metrics.
		int nudge = _rng.Next( -3, 4 );
		if ( nudge == 0 ) return;

		string director = corp.DirectorName;
		string agenda = corp.DirectorAgenda;
		_consequences.Enqueue( new CorporateConsequence
		{
			Source = director,
			Description = nudge > 0
				? $"{director} privately backed the player ({agenda})."
				: $"{director} undermined the player ({agenda}).",
			Apply = c => c.BoardConfidence = Math.Clamp( c.BoardConfidence + nudge, 0, 100 ),
		} );
	}

	private void ConsiderRankChange( CorporateState corp )
	{
		if ( corp.BoardConfidence >= PromoteThreshold && corp.Rank < CorporateRank.BoardLiaison )
		{
			var from = corp.Rank;
			var to = (CorporateRank)((int)corp.Rank + 1);
			_consequences.Enqueue( new CorporateConsequence
			{
				Source = "Board",
				Description = $"Promotion: {from} → {to}.",
				Apply = c =>
				{
					c.Rank = to;
					c.Budget += 50_000;
					c.BoardConfidence = 60;
				},
			} );
			_bus.Publish( new RankChanged( from, to, "Confidence threshold reached" ) );
		}
		else if ( corp.BoardConfidence <= DemoteThreshold && corp.Rank > CorporateRank.Probationary )
		{
			var from = corp.Rank;
			var to = (CorporateRank)((int)corp.Rank - 1);
			_consequences.Enqueue( new CorporateConsequence
			{
				Source = "Board",
				Description = $"Demotion: {from} → {to}.",
				Apply = c =>
				{
					c.Rank = to;
					c.Budget = Math.Max( 10_000, c.Budget - 30_000 );
					c.BoardConfidence = 40;
				},
			} );
			_bus.Publish( new RankChanged( from, to, "Confidence floor reached" ) );
		}
	}
}
