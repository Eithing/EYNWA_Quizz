using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMeFirstConsumedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MeFirstConsumedThisQuestion",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeFirstConsumedThisQuestion",
                table: "GameSessions");
        }
    }
}
