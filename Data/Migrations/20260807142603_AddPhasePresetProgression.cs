using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhasePresetProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "PhaseGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AwaySourceGroupId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AwaySourceMatchId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwaySourceRank",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HomeSourceGroupId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HomeSourceMatchId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeSourceRank",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PhaseGroups_Capacity",
                table: "PhaseGroups",
                sql: "Capacity IS NULL OR Capacity > 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Matches_AwaySourceRank",
                table: "Matches",
                sql: "AwaySourceRank IS NULL OR AwaySourceRank > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Matches_HomeSourceRank",
                table: "Matches",
                sql: "HomeSourceRank IS NULL OR HomeSourceRank > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PhaseGroups_Capacity",
                table: "PhaseGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Matches_AwaySourceRank",
                table: "Matches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Matches_HomeSourceRank",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "PhaseGroups");

            migrationBuilder.DropColumn(
                name: "AwaySourceGroupId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "AwaySourceMatchId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "AwaySourceRank",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeSourceGroupId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeSourceMatchId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeSourceRank",
                table: "Matches");
        }
    }
}
