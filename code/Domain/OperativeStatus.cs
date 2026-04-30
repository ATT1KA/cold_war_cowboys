namespace CWC.Domain;

public enum OperativeStatus
{
	Active,
	Injured,        // recovers next cycle
	Compromised,    // exposed; can't take certain mission types
	Defected,       // gone — usually cause of low loyalty + stressor
	Dead,
}
