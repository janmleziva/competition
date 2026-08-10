using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDisciplineParticipantAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisciplineParticipantAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionDisciplineId = table.Column<long>(type: "bigint", nullable: false),
                    CompetitionEntryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineParticipantAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisciplineParticipantAssignments_CompetitionDisciplines_CompetitionDisciplineId",
                        column: x => x.CompetitionDisciplineId,
                        principalTable: "CompetitionDisciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineParticipantAssignments_CompetitionEntries_CompetitionEntryId",
                        column: x => x.CompetitionEntryId,
                        principalTable: "CompetitionEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineParticipantAssignments_CompetitionDisciplineId_CompetitionEntryId",
                table: "DisciplineParticipantAssignments",
                columns: new[] { "CompetitionDisciplineId", "CompetitionEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineParticipantAssignments_CompetitionEntryId",
                table: "DisciplineParticipantAssignments",
                column: "CompetitionEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisciplineParticipantAssignments");
        }
    }
}
