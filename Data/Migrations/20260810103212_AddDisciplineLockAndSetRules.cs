using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations;

public partial class AddDisciplineLockAndSetRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "CompetitionDisciplines",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsLocked",
            table: "CompetitionDisciplines",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "SetsToWin",
            table: "CompetitionDisciplines",
            type: "int",
            nullable: true);

        migrationBuilder.Sql("UPDATE CompetitionDisciplines SET SetsToWin = 2 WHERE UsesSetScores = 1");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CompetitionDisciplines_SetsToWin",
            table: "CompetitionDisciplines",
            sql: "SetsToWin IS NULL OR SetsToWin > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CompetitionDisciplines_SetsToWin",
            table: "CompetitionDisciplines");

        migrationBuilder.DropColumn(name: "Description", table: "CompetitionDisciplines");
        migrationBuilder.DropColumn(name: "IsLocked", table: "CompetitionDisciplines");
        migrationBuilder.DropColumn(name: "SetsToWin", table: "CompetitionDisciplines");
    }
}
