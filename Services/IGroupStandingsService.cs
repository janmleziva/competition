namespace Competition.Services;

public interface IGroupStandingsService
{
    Task<IReadOnlyList<GroupStandingTable>> GetForDisciplineAsync(
        long editionId,
        long competitionDisciplineId,
        CancellationToken cancellationToken = default);

    Task<FinalStandingTable?> GetFinalStandingsAsync(
        long editionId,
        long competitionDisciplineId,
        CancellationToken cancellationToken = default);
}

public sealed record GroupStandingTable(
    long GroupId,
    string GroupName,
    bool ShowsSubscore,
    IReadOnlyList<GroupStandingRow> Rows);

public sealed record GroupStandingRow(
    int Position,
    long TeamId,
    string TeamName,
    int Seed,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int TablePoints,
    int ScoreFor,
    int ScoreAgainst,
    int SubscoreFor,
    int SubscoreAgainst)
{
    public int ScoreDifference => ScoreFor - ScoreAgainst;
    public int SubscoreDifference => SubscoreFor - SubscoreAgainst;
    public double ScoreRatio => StandingRatios.Calculate(ScoreFor, ScoreAgainst);
    public double SubscoreRatio => StandingRatios.Calculate(SubscoreFor, SubscoreAgainst);
}

public sealed record FinalStandingTable(
    bool ShowsSubscore,
    IReadOnlyList<FinalStandingRow> Rows);

public sealed record FinalStandingRow(
    int Position,
    long TeamId,
    string TeamName,
    int Seed,
    string DecidedBy,
    int ScoreFor,
    int ScoreAgainst,
    int SubscoreFor,
    int SubscoreAgainst)
{
    public int ScoreDifference => ScoreFor - ScoreAgainst;
    public int SubscoreDifference => SubscoreFor - SubscoreAgainst;
    public double ScoreRatio => StandingRatios.Calculate(ScoreFor, ScoreAgainst);
    public double SubscoreRatio => StandingRatios.Calculate(SubscoreFor, SubscoreAgainst);
}

public static class StandingRatios
{
    public static double Calculate(int scoreFor, int scoreAgainst) => scoreAgainst switch
    {
        0 when scoreFor > 0 => double.PositiveInfinity,
        0 => 1d,
        _ => (double)scoreFor / scoreAgainst
    };
}
