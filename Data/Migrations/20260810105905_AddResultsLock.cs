using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResultsLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AreResultsLocked",
                table: "CompetitionDisciplines",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreResultsLocked",
                table: "CompetitionDisciplines");
        }
    }
}
