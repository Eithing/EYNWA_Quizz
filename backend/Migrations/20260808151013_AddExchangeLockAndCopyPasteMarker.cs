using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeLockAndCopyPasteMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExchangeUsedForThemeSubRoundId",
                table: "GameSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFromCopyPasteJoker",
                table: "Answers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeUsedForThemeSubRoundId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "IsFromCopyPasteJoker",
                table: "Answers");
        }
    }
}
