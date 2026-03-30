using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class added_tournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TournamentId",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TournamentName",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TournamentStageId",
                table: "Matches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TournamentId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TournamentName",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TournamentStageId",
                table: "Matches");
        }
    }
}
