// [CWC-SPECIFIC] Cold War Cowboys game design in code form. Rewrite for a different game. Map: docs/FRAMEWORK_MAP.md
using System;
using System.Collections.Generic;
using CWC.Core;
using CWC.Domain;
using CWC.Missions;

namespace CWC.Corporate;

/// <summary>The set of moves a rival faction can make on its turn.</summary>
public enum FactionActionKind
{
	PoachOperative,
	SabotageMission,
	ProposeAlliance,
	EscalateToBoard,
	UndercutBudget,
	BankCash,
}

/// <summary>Audit record published when a faction takes an action.</summary>
public readonly record struct FactionActionTaken(
	string FactionId,
	FactionActionKind Kind,
	string Description );

/// <summary>
/// Sprint 6 faction AI. Each cycle every rival faction takes one action,
/// weighted by Agenda + relationship + standing + cash. Mission outcomes
/// feed back into faction relationships through OnMissionResolved.
/// </summary>
public sealed class FactionSystem
{
	private readonly Rng _rng;
	private readonly EventBus _bus;
	private readonly CorporateConsequenceProcessor _consequences;

	public FactionSystem( Rng rng, EventBus bus, CorporateConsequenceProcessor consequences )
	{
		_rng = rng;
		_bus = bus;
		_consequences = consequences;
	}

	public void ProcessTurn( CorporateState corp )
	{
		var ordered = new List<Faction>( corp.Factions.Values );
		ordered.Sort( ( a, b ) => StringComparer.Ordinal.Compare( a.Id, b.Id ) );
		foreach ( var faction in ordered )
		{
			// HostCorp & InternalDivisions don't act as autonomous rivals.
			if ( faction.Kind == FactionKind.HostCorp ) continue;
			var kind = ChooseAction( faction, corp );
			ExecuteAction( faction, kind, corp );
		}
	}

	public void OnMissionResolved( CorporateState corp, MissionResolved result )
	{
		var m = result.Mission;
		int magnitude = result.Outcome switch
		{
			MissionOutcome.Success         => 8,
			MissionOutcome.PartialSuccess  => 4,
			MissionOutcome.Failure         => -3,
			MissionOutcome.Catastrophe     => -8,
			_                              => 0,
		};
		if ( magnitude == 0 ) return;

		foreach ( var fid in m.OpposesFactionIds )
		{
			if ( !corp.Factions.TryGetValue( fid, out var f ) ) continue;
			QueueRelationshipShift( f, -magnitude, $"Player op opposed our interests ({m.Title})" );
		}
		foreach ( var fid in m.AlliedFactionIds )
		{
			if ( !corp.Factions.TryGetValue( fid, out var f ) ) continue;
			QueueRelationshipShift( f, magnitude, $"Joint op succeeded ({m.Title})" );
		}
		if ( m.IssuingFactionId != null && corp.Factions.TryGetValue( m.IssuingFactionId, out var issuer ) )
		{
			QueueRelationshipShift( issuer, magnitude, $"Contract delivered ({m.Title})" );
		}
	}

	private FactionActionKind ChooseAction( Faction f, CorporateState corp )
	{
		var candidates = new List<(FactionActionKind kind, double weight)>
		{
			(FactionActionKind.PoachOperative,  Weight( f, FactionActionKind.PoachOperative )),
			(FactionActionKind.SabotageMission, Weight( f, FactionActionKind.SabotageMission )),
			(FactionActionKind.ProposeAlliance, Weight( f, FactionActionKind.ProposeAlliance )),
			(FactionActionKind.EscalateToBoard, Weight( f, FactionActionKind.EscalateToBoard )),
			(FactionActionKind.UndercutBudget,  Weight( f, FactionActionKind.UndercutBudget )),
			(FactionActionKind.BankCash,        Weight( f, FactionActionKind.BankCash )),
		};
		return _rng.WeightedPick( candidates );
	}

	private static double Weight( Faction f, FactionActionKind kind )
	{
		double rel = f.RelationshipToPlayer / 100.0;       // -1..+1
		double standing = Math.Clamp( f.Standing, 0, 100 ) / 100.0;
		double cashFactor = Math.Min( 1.0, f.Cash / 50_000.0 );

		double agendaWeight = (f.Agenda, kind) switch
		{
			(FactionAgenda.Predatory,   FactionActionKind.SabotageMission) => 3.0,
			(FactionAgenda.Predatory,   FactionActionKind.PoachOperative)  => 2.5,
			(FactionAgenda.Predatory,   FactionActionKind.EscalateToBoard) => 1.8,
			(FactionAgenda.Expansionist,FactionActionKind.PoachOperative)  => 2.5,
			(FactionAgenda.Expansionist,FactionActionKind.UndercutBudget)  => 2.0,
			(FactionAgenda.Defensive,   FactionActionKind.BankCash)        => 2.5,
			(FactionAgenda.Defensive,   FactionActionKind.EscalateToBoard) => 1.5,
			(FactionAgenda.Cooperative, FactionActionKind.ProposeAlliance) => 3.0,
			(FactionAgenda.Cooperative, FactionActionKind.BankCash)        => 1.0,
			_ => 1.0,
		};

		double relationshipBias = kind switch
		{
			FactionActionKind.ProposeAlliance => Math.Max( 0, rel + 0.2 ) * 2.0,
			FactionActionKind.SabotageMission => Math.Max( 0, -rel ) * 2.5,
			FactionActionKind.EscalateToBoard => Math.Max( 0, -rel ) * 1.5,
			FactionActionKind.PoachOperative  => 1.0 + Math.Abs( rel ) * 0.5,
			FactionActionKind.UndercutBudget  => Math.Max( 0, -rel + 0.2 ) * 1.4,
			FactionActionKind.BankCash        => 0.6 + (1.0 - cashFactor) * 1.5,
			_ => 1.0,
		};

		double feasibility = kind switch
		{
			FactionActionKind.PoachOperative  => cashFactor * 1.2 + standing * 0.3,
			FactionActionKind.SabotageMission => cashFactor + standing,
			FactionActionKind.UndercutBudget  => standing * 1.5,
			FactionActionKind.EscalateToBoard => standing * 1.2,
			_ => 1.0,
		};

		return Math.Max( 0.05, agendaWeight * relationshipBias * feasibility );
	}

	private void ExecuteAction( Faction f, FactionActionKind kind, CorporateState corp )
	{
		switch ( kind )
		{
			case FactionActionKind.PoachOperative:
			{
				var target = PickPoachTarget( corp );
				int cost = 8_000;
				if ( target != null && f.Cash >= cost )
				{
					f.Cash -= cost;
					_consequences.Enqueue( new CorporateConsequence
					{
						Source = f.Name,
						Description = $"Poached operative {target.Codename} from the player.",
						Apply = c =>
						{
							// Another faction may have poached the same target
							// earlier this cycle — you can't steal a ghost.
							if ( target.Status == OperativeStatus.Defected ) return;
							c.Roster.Remove( target );
							c.PoliticalCapital = Math.Max( 0, c.PoliticalCapital - 1 );
							target.Status = OperativeStatus.Defected;
							target.FactionLoyalty = f.Id;
						},
					} );
				}
				else
				{
					RecordSoftAction( f, FactionActionKind.PoachOperative, "scoped a poach but found no target" );
				}
				break;
			}

			case FactionActionKind.SabotageMission:
			{
				int cost = 6_000;
				if ( f.Cash >= cost )
				{
					f.Cash -= cost;
					_consequences.Enqueue( new CorporateConsequence
					{
						Source = f.Name,
						Description = "Sabotaged player operations behind the scenes.",
						Apply = c =>
						{
							foreach ( var m in c.AvailableContracts ) m.Risk += 5;
							c.Suspicion = Math.Min( 100, c.Suspicion + 4 );
						},
					} );
				}
				else
				{
					RecordSoftAction( f, FactionActionKind.SabotageMission, "lacked cash to fund sabotage" );
				}
				break;
			}

			case FactionActionKind.ProposeAlliance:
			{
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = f.Name,
					Description = "Proposed alliance with the player division.",
					Apply = c =>
					{
						f.RelationshipToPlayer = Math.Min( 100, f.RelationshipToPlayer + 6 );
						c.PoliticalCapital += 1;
					},
				} );
				break;
			}

			case FactionActionKind.EscalateToBoard:
			{
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = f.Name,
					Description = "Filed grievance with the board against the player.",
					Apply = c =>
					{
						// Night 7: softened from -5 to -3; balance_test.py showed board confidence
						// bottoming out too aggressively with multiple factions escalating per cycle.
						c.BoardConfidence = Math.Max( 0, c.BoardConfidence - 3 );
						f.Standing = Math.Min( 100, f.Standing + 2 );
					},
				} );
				break;
			}

			case FactionActionKind.UndercutBudget:
			{
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = f.Name,
					Description = "Quietly redirected discretionary budget away from the player.",
					Apply = c =>
					{
						c.Budget = Math.Max( 0, c.Budget - 10_000 );
						f.Cash += 5_000;
					},
				} );
				break;
			}

			case FactionActionKind.BankCash:
			{
				_consequences.Enqueue( new CorporateConsequence
				{
					Source = f.Name,
					Description = "Hoarded resources for a future move.",
					Apply = _ => { f.Cash += 4_000; },
				} );
				break;
			}
		}

		_bus.Publish( new FactionActionTaken( f.Id, kind, kind.ToString() ) );
	}

	private static Operative? PickPoachTarget( CorporateState corp )
	{
		Operative? candidate = null;
		int worstLoyalty = int.MaxValue;
		foreach ( var op in corp.Roster )
		{
			if ( !op.Active ) continue;
			if ( op.Psychology.Loyalty < worstLoyalty )
			{
				worstLoyalty = op.Psychology.Loyalty;
				candidate = op;
			}
		}
		// Only poach if the operative is genuinely wobbly.
		return candidate != null && candidate.Psychology.Loyalty < 35 ? candidate : null;
	}

	private void RecordSoftAction( Faction f, FactionActionKind kind, string note )
	{
		_consequences.Enqueue( new CorporateConsequence
		{
			Source = f.Name,
			Description = $"{kind}: {note}.",
			Apply = _ => { },
		} );
	}

	private void QueueRelationshipShift( Faction f, int delta, string reason )
	{
		_consequences.Enqueue( new CorporateConsequence
		{
			Source = f.Name,
			Description = $"Relationship {(delta >= 0 ? "+" : "")}{delta} ({reason})",
			Apply = _ =>
			{
				f.RelationshipToPlayer = Math.Clamp( f.RelationshipToPlayer + delta, -100, 100 );
				if ( delta < 0 ) f.Standing = Math.Min( 100, f.Standing + 1 );
			},
		} );
	}
}
