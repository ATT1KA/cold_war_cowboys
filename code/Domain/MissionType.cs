namespace CWC.Domain;

public enum MissionType
{
	Extraction,
	Sabotage,
	Surveillance,
	Assassination,
	DataTheft,
	CounterIntel,
}

public enum MissionStatus
{
	Available,    // on the board
	Active,       // accepted, ops assigned
	Completed,    // resolved successfully (any non-failure tier)
	Failed,
	Expired,      // deadline passed
}
