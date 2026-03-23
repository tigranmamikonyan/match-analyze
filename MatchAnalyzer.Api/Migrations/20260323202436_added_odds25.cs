using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class added_odds25 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Over25Odds",
                table: "Matches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Under25Odds",
                table: "Matches",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Over25Odds",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Under25Odds",
                table: "Matches");
        }
    }
}
