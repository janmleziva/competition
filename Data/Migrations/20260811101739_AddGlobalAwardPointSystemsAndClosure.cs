using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalAwardPointSystemsAndClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId",
                table: "RankingPointRules");

            migrationBuilder.RenameColumn(
                name: "CompetitionDisciplineId",
                table: "RankingPointRules",
                newName: "AwardPointSystemId");

            migrationBuilder.RenameIndex(
                name: "IX_RankingPointRules_CompetitionDisciplineId_Rank",
                table: "RankingPointRules",
                newName: "IX_RankingPointRules_AwardPointSystemId_Rank");

            migrationBuilder.AddColumn<long>(
                name: "AwardPointSystemId",
                table: "CompetitionDisciplines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "CompetitionDisciplines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "CompetitionDisciplines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AwardPointSystems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardPointSystems", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO AwardPointSystems (Name)
                SELECT DISTINCT CONCAT(N'Migrated point system #', AwardPointSystemId)
                FROM RankingPointRules;

                UPDATE discipline
                SET AwardPointSystemId = system.Id
                FROM CompetitionDisciplines discipline
                INNER JOIN AwardPointSystems system
                    ON system.Name = CONCAT(N'Migrated point system #', discipline.Id)
                WHERE discipline.AwardPointSystemId IS NULL;

                UPDATE pointRule
                SET AwardPointSystemId = system.Id
                FROM RankingPointRules pointRule
                INNER JOIN AwardPointSystems system
                    ON system.Name = CONCAT(N'Migrated point system #', pointRule.AwardPointSystemId);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDisciplines_AwardPointSystemId",
                table: "CompetitionDisciplines",
                column: "AwardPointSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardPointSystems_Name",
                table: "AwardPointSystems",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompetitionDisciplines_AwardPointSystems_AwardPointSystemId",
                table: "CompetitionDisciplines",
                column: "AwardPointSystemId",
                principalTable: "AwardPointSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RankingPointRules_AwardPointSystems_AwardPointSystemId",
                table: "RankingPointRules",
                column: "AwardPointSystemId",
                principalTable: "AwardPointSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompetitionDisciplines_AwardPointSystems_AwardPointSystemId",
                table: "CompetitionDisciplines");

            migrationBuilder.DropForeignKey(
                name: "FK_RankingPointRules_AwardPointSystems_AwardPointSystemId",
                table: "RankingPointRules");

            // A global system can be shared by several disciplines, so it cannot be represented
            // losslessly by the former per-discipline foreign key during a downgrade.
            migrationBuilder.Sql("DELETE FROM RankingPointRules;");

            migrationBuilder.DropTable(
                name: "AwardPointSystems");

            migrationBuilder.DropIndex(
                name: "IX_CompetitionDisciplines_AwardPointSystemId",
                table: "CompetitionDisciplines");

            migrationBuilder.DropColumn(
                name: "AwardPointSystemId",
                table: "CompetitionDisciplines");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "CompetitionDisciplines");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "CompetitionDisciplines");

            migrationBuilder.RenameColumn(
                name: "AwardPointSystemId",
                table: "RankingPointRules",
                newName: "CompetitionDisciplineId");

            migrationBuilder.RenameIndex(
                name: "IX_RankingPointRules_AwardPointSystemId_Rank",
                table: "RankingPointRules",
                newName: "IX_RankingPointRules_CompetitionDisciplineId_Rank");

            migrationBuilder.AddForeignKey(
                name: "FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId",
                table: "RankingPointRules",
                column: "CompetitionDisciplineId",
                principalTable: "CompetitionDisciplines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
