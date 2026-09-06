using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class split_goals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoalMinutes",
                table: "Matches",
                newName: "HomeTeamGoals");

            migrationBuilder.AddColumn<string[]>(
                name: "AwayTeamGoals",
                table: "Matches",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayTeamGoals",
                table: "Matches");

            migrationBuilder.RenameColumn(
                name: "HomeTeamGoals",
                table: "Matches",
                newName: "GoalMinutes");
        }
    }
}
