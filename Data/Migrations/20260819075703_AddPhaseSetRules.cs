using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations;

[DbContext(typeof(CompetitionDbContext))]
[Migration("20260819075703_AddPhaseSetRules")]
public partial class AddPhaseSetRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SetCount",
            table: "DisciplinePhases",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SetRule",
            table: "DisciplinePhases",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE phase
            SET phase.SetRule = CASE
                    WHEN discipline.UsesSetScores = 1 AND discipline.SetsToWin IS NOT NULL THEN N'SetsToWin'
                    ELSE NULL
                END,
                phase.SetCount = CASE
                    WHEN discipline.UsesSetScores = 1 AND discipline.SetsToWin IS NOT NULL THEN discipline.SetsToWin
                    ELSE NULL
                END
            FROM DisciplinePhases AS phase
            INNER JOIN CompetitionDisciplines AS discipline
                ON discipline.Id = phase.CompetitionDisciplineId;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DisciplinePhases_SetRule",
            table: "DisciplinePhases",
            sql: "(SetRule IS NULL AND SetCount IS NULL) OR (SetRule IS NOT NULL AND SetCount > 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_DisciplinePhases_SetRule",
            table: "DisciplinePhases");

        migrationBuilder.DropColumn(
            name: "SetCount",
            table: "DisciplinePhases");

        migrationBuilder.DropColumn(
            name: "SetRule",
            table: "DisciplinePhases");
    }
}
