namespace CompetitionTracker.Models;

public record StandingRow(
    int CompetitorId,
    string CompetitorName,
    string? ClubOrTeam,
    decimal TotalPoints,
    IReadOnlyDictionary<string, decimal> DisciplinePoints);
