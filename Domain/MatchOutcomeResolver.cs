namespace Competition.Domain;

public enum MatchOutcome
{
    Draw,
    HomeWin,
    AwayWin
}

public static class MatchOutcomeResolver
{
    public static MatchOutcome Resolve(Match match)
    {
        if (match.HomeScore is null || match.AwayScore is null)
        {
            return MatchOutcome.Draw;
        }

        var mainScoreComparison = match.HomeScore.Value.CompareTo(match.AwayScore.Value);
        if (mainScoreComparison != 0)
        {
            return mainScoreComparison > 0 ? MatchOutcome.HomeWin : MatchOutcome.AwayWin;
        }

        if (match.DisciplinePhase.SetRule != SetRuleType.FixedSets || match.SetScores.Count == 0)
        {
            return MatchOutcome.Draw;
        }

        var subscoreComparison = match.SetScores.Sum(x => x.HomeScore)
            .CompareTo(match.SetScores.Sum(x => x.AwayScore));
        return subscoreComparison switch
        {
            > 0 => MatchOutcome.HomeWin,
            < 0 => MatchOutcome.AwayWin,
            _ => MatchOutcome.Draw
        };
    }
}
