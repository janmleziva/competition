using Competition.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class SqliteSchemaUpgraderTests
{
    [Fact]
    public async Task ApplyCompetitionUpgrades_RemovesObsoleteResultsLockWithoutLosingData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA foreign_keys = ON;
                CREATE TABLE CompetitionDisciplines (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CompetitionEditionId INTEGER NOT NULL,
                    DisciplineId INTEGER NOT NULL,
                    "Order" INTEGER NOT NULL,
                    AreResultsLocked INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX IX_CompetitionDisciplines_CompetitionEditionId_Order
                    ON CompetitionDisciplines (CompetitionEditionId, "Order");
                CREATE TABLE DisciplineTeams (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CompetitionDisciplineId INTEGER NOT NULL,
                    FOREIGN KEY (CompetitionDisciplineId) REFERENCES CompetitionDisciplines (Id)
                );
                INSERT INTO CompetitionDisciplines
                    (Id, CompetitionEditionId, DisciplineId, "Order", AreResultsLocked)
                    VALUES (7, 2, 5, 3, 1);
                INSERT INTO DisciplineTeams (Id, CompetitionDisciplineId) VALUES (11, 7);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await using var db = new CompetitionDbContext(
            new DbContextOptionsBuilder<CompetitionDbContext>().UseSqlite(connection).Options);

        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);
        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);

        await using var verification = connection.CreateCommand();
        verification.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM pragma_table_info('CompetitionDisciplines') WHERE name = 'AreResultsLocked'),
                (SELECT COUNT(*) FROM CompetitionDisciplines
                    WHERE Id = 7 AND CompetitionEditionId = 2 AND DisciplineId = 5 AND "Order" = 3),
                (SELECT COUNT(*) FROM DisciplineTeams WHERE Id = 11 AND CompetitionDisciplineId = 7),
                (SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'index' AND name = 'IX_CompetitionDisciplines_CompetitionEditionId_Order');
            """;
        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO CompetitionDisciplines
                (CompetitionEditionId, DisciplineId, "Order")
                VALUES (2, 6, 4);
            """;
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }

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

    [Fact]
    public async Task ApplyCompetitionUpgrades_AddsAndBackfillsPhaseSetRules()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE CompetitionDisciplines (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    UsesSetScores INTEGER NOT NULL,
                    SetsToWin INTEGER NULL
                );
                CREATE TABLE DisciplinePhases (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    CompetitionDisciplineId INTEGER NOT NULL
                );
                CREATE TABLE DisciplineTeams (Id INTEGER NOT NULL PRIMARY KEY);
                INSERT INTO CompetitionDisciplines (Id, UsesSetScores, SetsToWin) VALUES (1, 1, 2), (2, 0, NULL);
                INSERT INTO DisciplinePhases (Id, CompetitionDisciplineId) VALUES (10, 1), (20, 2);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await using var db = new CompetitionDbContext(
            new DbContextOptionsBuilder<CompetitionDbContext>().UseSqlite(connection).Options);

        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);
        SqliteSchemaUpgrader.ApplyCompetitionUpgrades(db);

        await using var verification = connection.CreateCommand();
        verification.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM pragma_table_info('DisciplinePhases') WHERE name IN ('SetRule', 'SetCount')),
                (SELECT SetRule FROM DisciplinePhases WHERE Id = 10),
                (SELECT SetCount FROM DisciplinePhases WHERE Id = 10),
                (SELECT COUNT(*) FROM DisciplinePhases WHERE Id = 20 AND SetRule IS NULL AND SetCount IS NULL);
            """;
        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal("SetsToWin", reader.GetString(1));
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
    }
}
