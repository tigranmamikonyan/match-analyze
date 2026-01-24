using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchAnalyzer.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite05",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite15",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavoriteFH05",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavoriteFH15",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFavorite05",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsFavorite15",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsFavoriteFH05",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsFavoriteFH15",
                table: "Matches");
        }
    }
}
