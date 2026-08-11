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
    public DateTime? ScheduledAt { get; set; }
    public bool UsesSetScores { get; set; }
    public int? SetsToWin { get; set; }
    public string? Description { get; set; }
    public bool IsLocked { get; set; }
    public bool IsScheduleLocked { get; set; }
    public bool AreResultsLocked { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long? AwardPointSystemId { get; set; }
    public AwardPointSystem? AwardPointSystem { get; set; }

    public ICollection<DisciplineParticipantAssignment> ParticipantAssignments { get; } = [];
    public ICollection<DisciplineTeam> Teams { get; } = [];
    public ICollection<DisciplinePhase> Phases { get; } = [];
    public ICollection<DisciplineStanding> FinalStandings { get; } = [];
}
