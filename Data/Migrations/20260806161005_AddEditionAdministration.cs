using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEditionAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationToken",
                table: "CompetitionEditions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CompetitionEditions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEditions_CreationToken",
                table: "CompetitionEditions",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEditions_IsActive",
                table: "CompetitionEditions",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompetitionEditions_CreationToken",
                table: "CompetitionEditions");

            migrationBuilder.DropIndex(
                name: "IX_CompetitionEditions_IsActive",
                table: "CompetitionEditions");

            migrationBuilder.DropColumn(
                name: "CreationToken",
                table: "CompetitionEditions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CompetitionEditions");
        }
    }
}
