namespace Competition.Domain;

public enum PlayingSystemType
{
    RoundRobin,
    Knockout,
    RoundRobinThenKnockout,
    ClassificationMatches,
    Custom
}

public enum PhaseType
{
    Group,
    Knockout,
    FinalStanding
}

public enum MatchStatus
{
    Scheduled,
    InProgress,
    Completed
}
