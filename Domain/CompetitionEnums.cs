namespace Competition.Domain;

public enum PlayingSystemType
{
    RoundRobin,
    GroupsThenClassificationMatches,
    Knockout,
    RoundRobinThenKnockout,
    Custom
}

public enum PhaseType
{
    Group,
    Knockout,
    FinalStanding
}

public enum SetRuleType
{
    SetsToWin,
    FixedSets
}

public enum MatchStatus
{
    Scheduled,
    InProgress,
    Completed
}

public enum BonusPointType
{
    LowestAverageSubscoreAgainst,
    HighestAverageScoreFor,
    HighestAverageScoreDifference
}
