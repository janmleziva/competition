using CompetitionTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Data;

public class CompetitionDbContext(DbContextOptions<CompetitionDbContext> options) : DbContext(options)
{
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchResult>()
            .HasIndex(result => new { result.MatchId, result.CompetitorId })
            .IsUnique();

        modelBuilder.Entity<Competitor>().HasData(
            new Competitor { Id = 1, Name = "Example Competitor", ClubOrTeam = "Demo Team" });

        modelBuilder.Entity<Discipline>().HasData(
            new Discipline { Id = 1, Name = "Example Discipline", ScoringNotes = "Higher points rank first." });
    }
}
