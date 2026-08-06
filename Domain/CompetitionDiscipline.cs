namespace Competition.Domain;

public sealed class CompetitionDiscipline
{
    public long Id { get; set; }
    public long CompetitionEditionId { get; set; }
    public CompetitionEdition CompetitionEdition { get; set; } = null!;
    public long DisciplineId { get; set; }
    public Discipline Discipline { get; set; } = null!;
    public PlayingSystemType PlayingSystem { get; set; }
    public int TeamSize { get; set; } = 1;
    public int Order { get; set; }

    public ICollection<DisciplineTeam> Teams { get; } = [];
    public ICollection<DisciplinePhase> Phases { get; } = [];
    public ICollection<RankingPointRule> RankingPointRules { get; } = [];
    public ICollection<DisciplineStanding> FinalStandings { get; } = [];
}
