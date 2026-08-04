using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixThemeSubRoundCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds");

            migrationBuilder.AddForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds",
                column: "ParentRoundId",
                principalTable: "Rounds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds");

            migrationBuilder.AddForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds",
                column: "ParentRoundId",
                principalTable: "Rounds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
