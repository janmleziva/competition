using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleLockAndSetScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScheduleLocked",
                table: "CompetitionDisciplines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UsesSetScores",
                table: "CompetitionDisciplines",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsScheduleLocked",
                table: "CompetitionDisciplines");

            migrationBuilder.DropColumn(
                name: "UsesSetScores",
                table: "CompetitionDisciplines");
        }
    }
}
