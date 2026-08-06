using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCompetitionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionEditions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEditions", x => x.Id);
                    table.CheckConstraint("CK_CompetitionEditions_DateRange", "EndDate >= StartDate");
                });

            migrationBuilder.CreateTable(
                name: "Competitors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Disciplines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disciplines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionEditionId = table.Column<long>(type: "bigint", nullable: false),
                    CompetitorId = table.Column<long>(type: "bigint", nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEntries", x => x.Id);
                    table.CheckConstraint("CK_CompetitionEntries_Seed", "Seed > 0");
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_CompetitionEditions_CompetitionEditionId",
                        column: x => x.CompetitionEditionId,
                        principalTable: "CompetitionEditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Competitors_CompetitorId",
                        column: x => x.CompetitorId,
                        principalTable: "Competitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionDisciplines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionEditionId = table.Column<long>(type: "bigint", nullable: false),
                    DisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    PlayingSystem = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TeamSize = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionDisciplines", x => x.Id);
                    table.CheckConstraint("CK_CompetitionDisciplines_Order", "\"Order\" > 0");
                    table.CheckConstraint("CK_CompetitionDisciplines_TeamSize", "TeamSize > 0");
                    table.ForeignKey(
                        name: "FK_CompetitionDisciplines_CompetitionEditions_CompetitionEditionId",
                        column: x => x.CompetitionEditionId,
                        principalTable: "CompetitionEditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionDisciplines_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplinePhases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    PointsForWin = table.Column<int>(type: "int", nullable: false),
                    PointsForDraw = table.Column<int>(type: "int", nullable: false),
                    PointsForLoss = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplinePhases", x => x.Id);
                    table.CheckConstraint("CK_DisciplinePhases_Order", "\"Order\" > 0");
                    table.CheckConstraint("CK_DisciplinePhases_Points", "PointsForWin >= 0 AND PointsForDraw >= 0 AND PointsForLoss >= 0");
                    table.ForeignKey(
                        name: "FK_DisciplinePhases_CompetitionDisciplines_CompetitionDisciplineId",
                        column: x => x.CompetitionDisciplineId,
                        principalTable: "CompetitionDisciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplineTeams",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineTeams", x => x.Id);
                    table.UniqueConstraint("AK_DisciplineTeams_CompetitionDisciplineId_Id", x => new { x.CompetitionDisciplineId, x.Id });
                    table.CheckConstraint("CK_DisciplineTeams_Seed", "Seed > 0");
                    table.ForeignKey(
                        name: "FK_DisciplineTeams_CompetitionDisciplines_CompetitionDisciplineId",
                        column: x => x.CompetitionDisciplineId,
                        principalTable: "CompetitionDisciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RankingPointRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingPointRules", x => x.Id);
                    table.CheckConstraint("CK_RankingPointRules_Points", "Points >= 0");
                    table.CheckConstraint("CK_RankingPointRules_Rank", "Rank > 0");
                    table.ForeignKey(
                        name: "FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId",
                        column: x => x.CompetitionDisciplineId,
                        principalTable: "CompetitionDisciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhaseGroups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisciplinePhaseId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhaseGroups", x => x.Id);
                    table.UniqueConstraint("AK_PhaseGroups_DisciplinePhaseId_Id", x => new { x.DisciplinePhaseId, x.Id });
                    table.CheckConstraint("CK_PhaseGroups_Order", "\"Order\" > 0");
                    table.ForeignKey(
                        name: "FK_PhaseGroups_DisciplinePhases_DisciplinePhaseId",
                        column: x => x.DisciplinePhaseId,
                        principalTable: "DisciplinePhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplineStandings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    DisciplineTeamId = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    PointsAwarded = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineStandings", x => x.Id);
                    table.CheckConstraint("CK_DisciplineStandings_Points", "PointsAwarded >= 0");
                    table.CheckConstraint("CK_DisciplineStandings_Rank", "Rank > 0");
                    table.ForeignKey(
                        name: "FK_DisciplineStandings_CompetitionDisciplines_CompetitionDisciplineId",
                        column: x => x.CompetitionDisciplineId,
                        principalTable: "CompetitionDisciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineStandings_DisciplineTeams_DisciplineTeamId",
                        column: x => x.DisciplineTeamId,
                        principalTable: "DisciplineTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplineTeamMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    DisciplineTeamId = table.Column<long>(type: "bigint", nullable: false),
                    CompetitionEntryId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineTeamMembers", x => x.Id);
                    table.CheckConstraint("CK_DisciplineTeamMembers_Order", "\"Order\" > 0");
                    table.ForeignKey(
                        name: "FK_DisciplineTeamMembers_CompetitionEntries_CompetitionEntryId",
                        column: x => x.CompetitionEntryId,
                        principalTable: "CompetitionEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineTeamMembers_DisciplineTeams_CompetitionDisciplineId_DisciplineTeamId",
                        columns: x => new { x.CompetitionDisciplineId, x.DisciplineTeamId },
                        principalTable: "DisciplineTeams",
                        principalColumns: new[] { "CompetitionDisciplineId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisciplinePhaseId = table.Column<long>(type: "bigint", nullable: false),
                    PhaseGroupId = table.Column<long>(type: "bigint", nullable: true),
                    HomeTeamId = table.Column<long>(type: "bigint", nullable: true),
                    AwayTeamId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.CheckConstraint("CK_Matches_CompletedHasScore", "Status <> 'Completed' OR (HomeScore IS NOT NULL AND AwayScore IS NOT NULL)");
                    table.CheckConstraint("CK_Matches_DifferentTeams", "HomeTeamId IS NULL OR AwayTeamId IS NULL OR HomeTeamId <> AwayTeamId");
                    table.CheckConstraint("CK_Matches_Order", "\"Order\" > 0");
                    table.CheckConstraint("CK_Matches_Score", "(HomeScore IS NULL AND AwayScore IS NULL) OR (HomeScore >= 0 AND AwayScore >= 0)");
                    table.ForeignKey(
                        name: "FK_Matches_DisciplinePhases_DisciplinePhaseId",
                        column: x => x.DisciplinePhaseId,
                        principalTable: "DisciplinePhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_DisciplineTeams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "DisciplineTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_DisciplineTeams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "DisciplineTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_PhaseGroups_DisciplinePhaseId_PhaseGroupId",
                        columns: x => new { x.DisciplinePhaseId, x.PhaseGroupId },
                        principalTable: "PhaseGroups",
                        principalColumns: new[] { "DisciplinePhaseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhaseGroupTeams",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisciplinePhaseId = table.Column<long>(type: "bigint", nullable: false),
                    PhaseGroupId = table.Column<long>(type: "bigint", nullable: false),
                    DisciplineTeamId = table.Column<long>(type: "bigint", nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhaseGroupTeams", x => x.Id);
                    table.CheckConstraint("CK_PhaseGroupTeams_Seed", "Seed > 0");
                    table.ForeignKey(
                        name: "FK_PhaseGroupTeams_DisciplineTeams_DisciplineTeamId",
                        column: x => x.DisciplineTeamId,
                        principalTable: "DisciplineTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhaseGroupTeams_PhaseGroups_DisciplinePhaseId_PhaseGroupId",
                        columns: x => new { x.DisciplinePhaseId, x.PhaseGroupId },
                        principalTable: "PhaseGroups",
                        principalColumns: new[] { "DisciplinePhaseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchSetScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<long>(type: "bigint", nullable: false),
                    SetNumber = table.Column<int>(type: "int", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: false),
                    AwayScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSetScores", x => x.Id);
                    table.CheckConstraint("CK_MatchSetScores_Score", "HomeScore >= 0 AND AwayScore >= 0");
                    table.CheckConstraint("CK_MatchSetScores_SetNumber", "SetNumber > 0");
                    table.ForeignKey(
                        name: "FK_MatchSetScores_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDisciplines_CompetitionEditionId_DisciplineId",
                table: "CompetitionDisciplines",
                columns: new[] { "CompetitionEditionId", "DisciplineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDisciplines_CompetitionEditionId_Order",
                table: "CompetitionDisciplines",
                columns: new[] { "CompetitionEditionId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDisciplines_DisciplineId",
                table: "CompetitionDisciplines",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_CompetitionEditionId_CompetitorId",
                table: "CompetitionEntries",
                columns: new[] { "CompetitionEditionId", "CompetitorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_CompetitionEditionId_Seed",
                table: "CompetitionEntries",
                columns: new[] { "CompetitionEditionId", "Seed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_CompetitorId",
                table: "CompetitionEntries",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitors_LastName_FirstName",
                table: "Competitors",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplinePhases_CompetitionDisciplineId_Order",
                table: "DisciplinePhases",
                columns: new[] { "CompetitionDisciplineId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disciplines_Name",
                table: "Disciplines",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineStandings_CompetitionDisciplineId_DisciplineTeamId",
                table: "DisciplineStandings",
                columns: new[] { "CompetitionDisciplineId", "DisciplineTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineStandings_CompetitionDisciplineId_Rank",
                table: "DisciplineStandings",
                columns: new[] { "CompetitionDisciplineId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineStandings_DisciplineTeamId",
                table: "DisciplineStandings",
                column: "DisciplineTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTeamMembers_CompetitionDisciplineId_CompetitionEntryId",
                table: "DisciplineTeamMembers",
                columns: new[] { "CompetitionDisciplineId", "CompetitionEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTeamMembers_CompetitionDisciplineId_DisciplineTeamId",
                table: "DisciplineTeamMembers",
                columns: new[] { "CompetitionDisciplineId", "DisciplineTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTeamMembers_CompetitionEntryId",
                table: "DisciplineTeamMembers",
                column: "CompetitionEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTeamMembers_DisciplineTeamId_Order",
                table: "DisciplineTeamMembers",
                columns: new[] { "DisciplineTeamId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTeams_CompetitionDisciplineId_Seed",
                table: "DisciplineTeams",
                columns: new[] { "CompetitionDisciplineId", "Seed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_AwayTeamId",
                table: "Matches",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_DisciplinePhaseId_Order",
                table: "Matches",
                columns: new[] { "DisciplinePhaseId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_DisciplinePhaseId_PhaseGroupId",
                table: "Matches",
                columns: new[] { "DisciplinePhaseId", "PhaseGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HomeTeamId",
                table: "Matches",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSetScores_MatchId_SetNumber",
                table: "MatchSetScores",
                columns: new[] { "MatchId", "SetNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroups_DisciplinePhaseId_Name",
                table: "PhaseGroups",
                columns: new[] { "DisciplinePhaseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroups_DisciplinePhaseId_Order",
                table: "PhaseGroups",
                columns: new[] { "DisciplinePhaseId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroupTeams_DisciplinePhaseId_DisciplineTeamId",
                table: "PhaseGroupTeams",
                columns: new[] { "DisciplinePhaseId", "DisciplineTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroupTeams_DisciplinePhaseId_PhaseGroupId",
                table: "PhaseGroupTeams",
                columns: new[] { "DisciplinePhaseId", "PhaseGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroupTeams_DisciplineTeamId",
                table: "PhaseGroupTeams",
                column: "DisciplineTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGroupTeams_PhaseGroupId_Seed",
                table: "PhaseGroupTeams",
                columns: new[] { "PhaseGroupId", "Seed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankingPointRules_CompetitionDisciplineId_Rank",
                table: "RankingPointRules",
                columns: new[] { "CompetitionDisciplineId", "Rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisciplineStandings");

            migrationBuilder.DropTable(
                name: "DisciplineTeamMembers");

            migrationBuilder.DropTable(
                name: "MatchSetScores");

            migrationBuilder.DropTable(
                name: "PhaseGroupTeams");

            migrationBuilder.DropTable(
                name: "RankingPointRules");

            migrationBuilder.DropTable(
                name: "CompetitionEntries");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Competitors");

            migrationBuilder.DropTable(
                name: "DisciplineTeams");

            migrationBuilder.DropTable(
                name: "PhaseGroups");

            migrationBuilder.DropTable(
                name: "DisciplinePhases");

            migrationBuilder.DropTable(
                name: "CompetitionDisciplines");

            migrationBuilder.DropTable(
                name: "CompetitionEditions");

            migrationBuilder.DropTable(
                name: "Disciplines");
        }
    }
}
