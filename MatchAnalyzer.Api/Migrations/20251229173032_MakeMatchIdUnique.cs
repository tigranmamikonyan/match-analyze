using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeMatchIdUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Matches_MatchId",
                table: "Matches",
                column: "MatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_MatchId",
                table: "Matches");
        }
    }
}
