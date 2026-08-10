using Competition.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Competition.Data.Migrations;

[DbContext(typeof(CompetitionDbContext))]
[Migration("20260807160000_RenameClassificationPlayingSystem")]
public partial class RenameClassificationPlayingSystem : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE [CompetitionDisciplines] SET [PlayingSystem] = 'GroupsThenClassificationMatches' WHERE [PlayingSystem] = 'ClassificationMatches'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE [CompetitionDisciplines] SET [PlayingSystem] = 'ClassificationMatches' WHERE [PlayingSystem] = 'GroupsThenClassificationMatches'");
    }
}
