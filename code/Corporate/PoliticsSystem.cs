using System;
using ColdWarCowboys.Core;
using ColdWarCowboys.Missions;
using ColdWarCowboys.World;

namespace ColdWarCowboys.Corporate;

/// <summary>Audit record published when the player's rank changes.</summary>
public readonly record struct RankChanged( CorporateRank From, CorporateRank To, string Reason );

/// <summary>Audit record published when a board directive resolves.</summary>
public readonly record struct DirectiveResolved( string DirectiveId, bool Complied, int ConfidenceDelta );

/// <summary>
/// Sprint 6 board-level dynamics. EvaluateBoard is called once per cycle; it
/// reviews directive compliance, recomputes board confidence, considers
/// promotion/demotion, and lets the Director nudge confidence according to
/// their personal agenda (which may pull against the board's stated wishes).
/// </summary>
public sealed class PoliticsSystem
{
	private const int PromoteThreshold = 80;
	private const int DemoteThreshold = 20;

	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly ConsequenceProcessor _consequences;

	public PoliticsSystem( Rng rng, EventBus bus, ConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	/// <summary>Reviews directives, applies compliance fallout, considers rank change.</summary>
	public void EvaluateBoard( CorporateState corp, WorldState world )
	{
		ReviewDirectives( corp, world );
		ApplyDirectorAgenda( corp );
		ConsiderRankChange( corp );
	}

	/// <summary>Hooks mission outcomes into board confidence.</summary>
	public void OnMissionResolved( CorporateState corp, MissionResolved result )
	{
		int delta = result.Outcome switch
		{
			MissionOutcome.Success => +5,
			MissionOutcome.PartialSuccess => +1,
			MissionOutcome.Failure => -4,
			MissionOutcome.Catastrophe => -10,
			_ => 0,
		};
		// Board directives weigh double (visibility).
		if ( result.Mission.IsBoardDirective ) delta *= 2;
		if ( delta == 0 ) return;
		_consequences.Enqueue( new Consequence
		{
			Source = "Board",
			Description = $"Confidence {(delta >= 0 ? "+" : "")}{delta} ({result.Mission.Name})",
			Apply = c =>
			{
				c.BoardConfidence = Math.Clamp( c.BoardConfidence + delta, 0, 100 );
			},
		} );
	}

	/// <summary>Issues a new directive — public so seeders/JSON loaders can call it.</summary>
	public void IssueDirective( CorporateState corp, BoardDirective directive )
	{
		corp.ActiveDirectives.Add( directive );
		_consequences.Enqueue( new Consequence
		{
			Source = "Board",
			Description = $"New directive issued: {directive.Title}",
			Apply = _ => { },
		} );
	}

	private void ReviewDirectives( CorporateState corp, WorldState world )
	{
		// Walk a copy so we can mutate the original list.
		foreach ( var d in corp.ActiveDirectives.ToArray() )
		{
			if ( d.Resolved ) continue;
			if ( world.Day < d.DeadlineDay ) continue;

			// Compliance proxy: directives that completed via a board-tagged mission
			// flag themselves Resolved=true elsewhere. Anything still unresolved at
			// deadline is treated as ignored.
			d.Resolved = true;
			int delta = -d.IgnoreConfidencePenalty;
			_consequences.Enqueue( new Consequence
			{
				Source = "Board",
				Description = $"Directive '{d.Title}' deadline reached without compliance.",
				Apply = c =>
				{
					c.BoardConfidence = Math.Clamp( c.BoardConfidence + delta, 0, 100 );
				},
			} );
			_bus.Publish( new DirectiveResolved( d.Id, false, delta ) );
		}
	}

	private void ApplyDirectorAgenda( CorporateState corp )
	{
		// The Director nudges confidence by ±1-3 based on whether the player's
		// recent behavior aligned with their personal agenda. We model this as
		// a small biased roll — the user-facing surface is the boss feeling
		// either supportive or hostile despite identical board metrics.
		int nudge = _rng.Range( -3, 4 );
		if ( nudge == 0 ) return;
		_consequences.Enqueue( new Consequence
		{
			Source = corp.DirectorName,
			Description = nudge > 0
				? $"{corp.DirectorName} privately backed the player ({corp.DirectorAgenda})."
				: $"{corp.DirectorName} undermined the player ({corp.DirectorAgenda}).",
			Apply = c =>
			{
				c.BoardConfidence = Math.Clamp( c.BoardConfidence + nudge, 0, 100 );
			},
		} );
	}

	private void ConsiderRankChange( CorporateState corp )
	{
		if ( corp.BoardConfidence >= PromoteThreshold && corp.Rank < CorporateRank.BoardLiaison )
		{
			var from = corp.Rank;
			var to = (CorporateRank)((int)corp.Rank + 1);
			_consequences.Enqueue( new Consequence
			{
				Source = "Board",
				Description = $"Promotion: {from} → {to}.",
				Apply = c =>
				{
					c.Rank = to;
					c.Budget += 500;
					// Promotions raise the bar — confidence resets toward the middle.
					c.BoardConfidence = 60;
				},
			} );
			_bus.Publish( new RankChanged( from, to, "Confidence threshold reached" ) );
		}
		else if ( corp.BoardConfidence <= DemoteThreshold && corp.Rank > CorporateRank.Probationary )
		{
			var from = corp.Rank;
			var to = (CorporateRank)((int)corp.Rank - 1);
			_consequences.Enqueue( new Consequence
			{
				Source = "Board",
				Description = $"Demotion: {from} → {to}.",
				Apply = c =>
				{
					c.Rank = to;
					c.Budget = Math.Max( 100, c.Budget - 300 );
					c.BoardConfidence = 40;
				},
			} );
			_bus.Publish( new RankChanged( from, to, "Confidence floor reached" ) );
		}
	}
}
