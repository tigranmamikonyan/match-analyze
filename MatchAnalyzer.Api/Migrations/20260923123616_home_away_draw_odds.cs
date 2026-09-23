using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class home_away_draw_odds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AwayOdds",
                table: "Matches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DrawOdds",
                table: "Matches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HomeOdds",
                table: "Matches",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayOdds",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DrawOdds",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeOdds",
                table: "Matches");
        }
    }
}
