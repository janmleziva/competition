using Competition.Domain;
using Microsoft.EntityFrameworkCore;

namespace Competition.Data;

public sealed class CompetitionDbContext(DbContextOptions<CompetitionDbContext> options)
    : DbContext(options)
{
    public DbSet<CompetitionEdition> CompetitionEditions => Set<CompetitionEdition>();
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<CompetitionEntry> CompetitionEntries => Set<CompetitionEntry>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<CompetitionDiscipline> CompetitionDisciplines => Set<CompetitionDiscipline>();
    public DbSet<DisciplineParticipantAssignment> DisciplineParticipantAssignments => Set<DisciplineParticipantAssignment>();
    public DbSet<DisciplineTeam> DisciplineTeams => Set<DisciplineTeam>();
    public DbSet<DisciplineTeamMember> DisciplineTeamMembers => Set<DisciplineTeamMember>();
    public DbSet<DisciplinePhase> DisciplinePhases => Set<DisciplinePhase>();
    public DbSet<PhaseGroup> PhaseGroups => Set<PhaseGroup>();
    public DbSet<PhaseGroupTeam> PhaseGroupTeams => Set<PhaseGroupTeam>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchSetScore> MatchSetScores => Set<MatchSetScore>();
    public DbSet<AwardPointSystem> AwardPointSystems => Set<AwardPointSystem>();
    public DbSet<RankingPointRule> RankingPointRules => Set<RankingPointRule>();
    public DbSet<DisciplineStanding> DisciplineStandings => Set<DisciplineStanding>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateMatchVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        UpdateMatchVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlite = Database.IsSqlite();

        modelBuilder.Entity<CompetitionEdition>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.City).HasMaxLength(120);
            entity.HasIndex(x => x.CreationToken).IsUnique();
            entity.HasIndex(x => x.IsActive)
                .IsUnique()
                .HasFilter(isSqlite ? "\"IsActive\" = 1" : "[IsActive] = 1");
            entity.ToTable("CompetitionEditions", table =>
                table.HasCheckConstraint("CK_CompetitionEditions_DateRange", "EndDate >= StartDate"));
        });

        modelBuilder.Entity<Competitor>(entity =>
        {
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.HasIndex(x => new { x.LastName, x.FirstName });
        });

        modelBuilder.Entity<CompetitionEntry>(entity =>
        {
            entity.HasIndex(x => new { x.CompetitionEditionId, x.CompetitorId }).IsUnique();
            entity.HasIndex(x => new { x.CompetitionEditionId, x.Seed }).IsUnique();
            entity.ToTable("CompetitionEntries", table =>
                table.HasCheckConstraint("CK_CompetitionEntries_Seed", "Seed > 0"));
            entity.HasOne(x => x.CompetitionEdition)
                .WithMany(x => x.Entries)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Competitor)
                .WithMany(x => x.CompetitionEntries)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Discipline>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<CompetitionDiscipline>(entity =>
        {
            entity.Property(x => x.PlayingSystem).HasConversion<string>().HasMaxLength(40);
            if (!isSqlite)
            {
                entity.Property(x => x.ScheduledAt).HasColumnType("datetime2");
            }
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasIndex(x => new { x.CompetitionEditionId, x.DisciplineId }).IsUnique();
            entity.HasIndex(x => new { x.CompetitionEditionId, x.Order }).IsUnique();
            entity.ToTable("CompetitionDisciplines", table =>
            {
                table.HasCheckConstraint("CK_CompetitionDisciplines_Order", "\"Order\" > 0");
                table.HasCheckConstraint("CK_CompetitionDisciplines_TeamSize", "TeamSize > 0");
                table.HasCheckConstraint("CK_CompetitionDisciplines_SetsToWin", "SetsToWin IS NULL OR SetsToWin > 0");
            });
            entity.HasOne(x => x.CompetitionEdition)
                .WithMany(x => x.Disciplines)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Discipline)
                .WithMany(x => x.CompetitionDisciplines)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AwardPointSystem)
                .WithMany(x => x.CompetitionDisciplines)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AwardPointSystem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<DisciplineParticipantAssignment>(entity =>
        {
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.CompetitionEntryId }).IsUnique();
            entity.HasOne(x => x.CompetitionDiscipline)
                .WithMany(x => x.ParticipantAssignments)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CompetitionEntry)
                .WithMany(x => x.DisciplineAssignments)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DisciplineTeam>(entity =>
        {
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.Seed }).IsUnique();
            entity.HasAlternateKey(x => new { x.CompetitionDisciplineId, x.Id });
            entity.ToTable("DisciplineTeams", table =>
                table.HasCheckConstraint("CK_DisciplineTeams_Seed", "Seed > 0"));
            entity.HasOne(x => x.CompetitionDiscipline)
                .WithMany(x => x.Teams)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DisciplineTeamMember>(entity =>
        {
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.CompetitionEntryId }).IsUnique();
            entity.HasIndex(x => new { x.DisciplineTeamId, x.Order }).IsUnique();
            entity.ToTable("DisciplineTeamMembers", table =>
                table.HasCheckConstraint("CK_DisciplineTeamMembers_Order", "\"Order\" > 0"));
            entity.HasOne(x => x.DisciplineTeam)
                .WithMany(x => x.Members)
                .HasForeignKey(x => new { x.CompetitionDisciplineId, x.DisciplineTeamId })
                .HasPrincipalKey(x => new { x.CompetitionDisciplineId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CompetitionEntry)
                .WithMany(x => x.TeamMemberships)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DisciplinePhase>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.Order }).IsUnique();
            entity.ToTable("DisciplinePhases", table =>
            {
                table.HasCheckConstraint("CK_DisciplinePhases_Order", "\"Order\" > 0");
                table.HasCheckConstraint("CK_DisciplinePhases_Points", "PointsForWin >= 0 AND PointsForDraw >= 0 AND PointsForLoss >= 0");
            });
            entity.HasOne(x => x.CompetitionDiscipline)
                .WithMany(x => x.Phases)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhaseGroup>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.HasIndex(x => new { x.DisciplinePhaseId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.DisciplinePhaseId, x.Order }).IsUnique();
            entity.HasAlternateKey(x => new { x.DisciplinePhaseId, x.Id });
            entity.ToTable("PhaseGroups", table =>
            {
                table.HasCheckConstraint("CK_PhaseGroups_Order", "\"Order\" > 0");
                table.HasCheckConstraint("CK_PhaseGroups_Capacity", "Capacity IS NULL OR Capacity > 1");
            });
            entity.HasOne(x => x.DisciplinePhase)
                .WithMany(x => x.Groups)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhaseGroupTeam>(entity =>
        {
            entity.HasIndex(x => new { x.DisciplinePhaseId, x.DisciplineTeamId }).IsUnique();
            entity.HasIndex(x => new { x.PhaseGroupId, x.Seed }).IsUnique();
            entity.ToTable("PhaseGroupTeams", table =>
                table.HasCheckConstraint("CK_PhaseGroupTeams_Seed", "Seed > 0"));
            entity.HasOne(x => x.PhaseGroup)
                .WithMany(x => x.Teams)
                .HasForeignKey(x => new { x.DisciplinePhaseId, x.PhaseGroupId })
                .HasPrincipalKey(x => new { x.DisciplinePhaseId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DisciplineTeam)
                .WithMany(x => x.GroupAssignments)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Match>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.DisciplinePhaseId, x.Order }).IsUnique();
            entity.ToTable("Matches", table =>
            {
                table.HasCheckConstraint("CK_Matches_Order", "\"Order\" > 0");
                table.HasCheckConstraint("CK_Matches_DifferentTeams", "HomeTeamId IS NULL OR AwayTeamId IS NULL OR HomeTeamId <> AwayTeamId");
                table.HasCheckConstraint("CK_Matches_Score", "(HomeScore IS NULL AND AwayScore IS NULL) OR (HomeScore >= 0 AND AwayScore >= 0)");
                table.HasCheckConstraint("CK_Matches_CompletedHasScore", "Status <> 'Completed' OR (HomeScore IS NOT NULL AND AwayScore IS NOT NULL)");
                table.HasCheckConstraint("CK_Matches_HomeSourceRank", "HomeSourceRank IS NULL OR HomeSourceRank > 0");
                table.HasCheckConstraint("CK_Matches_AwaySourceRank", "AwaySourceRank IS NULL OR AwaySourceRank > 0");
            });
            entity.HasOne(x => x.DisciplinePhase)
                .WithMany(x => x.Matches)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PhaseGroup)
                .WithMany(x => x.Matches)
                .HasForeignKey(x => new { x.DisciplinePhaseId, x.PhaseGroupId })
                .HasPrincipalKey(x => new { x.DisciplinePhaseId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.HomeTeam)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AwayTeam)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MatchSetScore>(entity =>
        {
            entity.HasIndex(x => new { x.MatchId, x.SetNumber }).IsUnique();
            entity.ToTable("MatchSetScores", table =>
            {
                table.HasCheckConstraint("CK_MatchSetScores_SetNumber", "SetNumber > 0");
                table.HasCheckConstraint("CK_MatchSetScores_Score", "HomeScore >= 0 AND AwayScore >= 0");
            });
            entity.HasOne(x => x.Match)
                .WithMany(x => x.SetScores)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RankingPointRule>(entity =>
        {
            entity.HasIndex(x => new { x.AwardPointSystemId, x.Rank }).IsUnique();
            entity.ToTable("RankingPointRules", table =>
            {
                table.HasCheckConstraint("CK_RankingPointRules_Rank", "Rank > 0");
                table.HasCheckConstraint("CK_RankingPointRules_Points", "Points >= 0");
            });
            entity.HasOne(x => x.AwardPointSystem)
                .WithMany(x => x.Rules)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DisciplineStanding>(entity =>
        {
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.DisciplineTeamId }).IsUnique();
            entity.HasIndex(x => new { x.CompetitionDisciplineId, x.Rank });
            entity.ToTable("DisciplineStandings", table =>
            {
                table.HasCheckConstraint("CK_DisciplineStandings_Rank", "Rank > 0");
                table.HasCheckConstraint("CK_DisciplineStandings_Points", "PointsAwarded >= 0");
            });
            entity.HasOne(x => x.CompetitionDiscipline)
                .WithMany(x => x.FinalStandings)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DisciplineTeam)
                .WithMany(x => x.FinalStandingEntries)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void UpdateMatchVersions()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Match>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.UpdatedAtUtc == default)
                {
                    entry.Entity.UpdatedAtUtc = now;
                }

                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = entry.OriginalValues.GetValue<int>(nameof(Match.Version)) + 1;
            }
        }
    }
}
