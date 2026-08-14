using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Competition.Data;

public static class SqliteSchemaUpgrader
{
    public static void ApplyCompetitionUpgrades(CompetitionDbContext database)
    {
        RemoveObsoleteResultsLockColumn(database);

        database.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "DisciplineBonusPointRules" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DisciplineBonusPointRules" PRIMARY KEY AUTOINCREMENT,
                "CompetitionDisciplineId" INTEGER NOT NULL,
                "Type" TEXT NOT NULL,
                "Points" INTEGER NOT NULL DEFAULT 1,
                CONSTRAINT "CK_DisciplineBonusPointRules_Points" CHECK ("Points" >= 0),
                CONSTRAINT "FK_DisciplineBonusPointRules_CompetitionDisciplines_CompetitionDisciplineId"
                    FOREIGN KEY ("CompetitionDisciplineId") REFERENCES "CompetitionDisciplines" ("Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DisciplineBonusPointRules_CompetitionDisciplineId_Type"
                ON "DisciplineBonusPointRules" ("CompetitionDisciplineId", "Type");

            CREATE TABLE IF NOT EXISTS "DisciplineBonusAwards" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DisciplineBonusAwards" PRIMARY KEY AUTOINCREMENT,
                "CompetitionDisciplineId" INTEGER NOT NULL,
                "DisciplineTeamId" INTEGER NOT NULL,
                "Type" TEXT NOT NULL,
                "PointsAwarded" INTEGER NOT NULL,
                "MetricTotal" INTEGER NOT NULL,
                "MatchCount" INTEGER NOT NULL,
                CONSTRAINT "CK_DisciplineBonusAwards_Points" CHECK ("PointsAwarded" >= 0),
                CONSTRAINT "CK_DisciplineBonusAwards_MatchCount" CHECK ("MatchCount" > 0),
                CONSTRAINT "FK_DisciplineBonusAwards_CompetitionDisciplines_CompetitionDisciplineId"
                    FOREIGN KEY ("CompetitionDisciplineId") REFERENCES "CompetitionDisciplines" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_DisciplineBonusAwards_DisciplineTeams_DisciplineTeamId"
                    FOREIGN KEY ("DisciplineTeamId") REFERENCES "DisciplineTeams" ("Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DisciplineBonusAwards_CompetitionDisciplineId_DisciplineTeamId_Type"
                ON "DisciplineBonusAwards" ("CompetitionDisciplineId", "DisciplineTeamId", "Type");
            CREATE INDEX IF NOT EXISTS "IX_DisciplineBonusAwards_DisciplineTeamId"
                ON "DisciplineBonusAwards" ("DisciplineTeamId");
            """);
    }

    private static void RemoveObsoleteResultsLockColumn(CompetitionDbContext database)
    {
        if (!ColumnExists(database, "CompetitionDisciplines", "AreResultsLocked"))
        {
            return;
        }

        using var transaction = database.Database.BeginTransaction();
        database.Database.ExecuteSqlRaw(
            "ALTER TABLE \"CompetitionDisciplines\" DROP COLUMN \"AreResultsLocked\";");
        transaction.Commit();
    }

    private static bool ColumnExists(
        CompetitionDbContext database,
        string tableName,
        string columnName)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info($tableName) WHERE name = $columnName;";

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "$tableName";
            tableParameter.Value = tableName;
            command.Parameters.Add(tableParameter);

            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "$columnName";
            columnParameter.Value = columnName;
            command.Parameters.Add(columnParameter);

            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }
}
