using Competition.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class SqliteSchemaUpgraderTests
{
    [Fact]
    public async Task ApplyCompetitionUpgrades_AddsBonusTablesToExistingDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE CompetitionDisciplines (Id INTEGER NOT NULL PRIMARY KEY);
                CREATE TABLE DisciplineTeams (Id INTEGER NOT NULL PRIMARY KEY);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await using var db = new CompetitionDbContext(
            new DbContextOptionsBuilder<CompetitionDbContext>().UseSqlite(connection).Options);

        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);
        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);

        await using var verification = connection.CreateCommand();
        verification.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('DisciplineBonusPointRules', 'DisciplineBonusAwards');";
        Assert.Equal(2L, (long)(await verification.ExecuteScalarAsync())!);
    }
}
