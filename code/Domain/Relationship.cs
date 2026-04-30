namespace CWC.Domain;

public enum RelationshipKind
{
	Acquaintance,
	Friend,
	Rival,
	Mentor,        // From mentors To
	Protege,       // From admires/depends on To
	Lover,
	Creditor,      // To owes From
	Debtor,        // From owes To
	Confidant,
}

/// <summary>
/// Directed edge between two operatives. Asymmetric — A's view of B can
/// differ from B's view of A. Score is the strength of the bond (-100..100).
/// </summary>
public sealed class Relationship
{
	public int FromId { get; set; }
	public int ToId { get; set; }
	public RelationshipKind Kind { get; set; }
	public int Score { get; set; } = 0;
}
