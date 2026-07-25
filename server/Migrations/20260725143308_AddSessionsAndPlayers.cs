using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionsAndPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_QuizSessions_Code",
                table: "QuizSessions");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "QuizSessions",
                newName: "Status");

            migrationBuilder.AddColumn<int>(
                name: "QuizId",
                table: "QuizSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_QuizSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "QuizSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuizStepId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAnswer = table.Column<string>(type: "TEXT", nullable: false),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                    PointsAwarded = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAnswers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerAnswers_QuizSteps_QuizStepId",
                        column: x => x.QuizStepId,
                        principalTable: "QuizSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizSessions_QuizId",
                table: "QuizSessions",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAnswers_PlayerId_QuizStepId",
                table: "PlayerAnswers",
                columns: new[] { "PlayerId", "QuizStepId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAnswers_QuizStepId",
                table: "PlayerAnswers",
                column: "QuizStepId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ClientToken",
                table: "Players",
                column: "ClientToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SessionId",
                table: "Players",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuizSessions_Quizzes_QuizId",
                table: "QuizSessions",
                column: "QuizId",
                principalTable: "Quizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuizSessions_Quizzes_QuizId",
                table: "QuizSessions");

            migrationBuilder.DropTable(
                name: "PlayerAnswers");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropIndex(
                name: "IX_QuizSessions_QuizId",
                table: "QuizSessions");

            migrationBuilder.DropColumn(
                name: "QuizId",
                table: "QuizSessions");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "QuizSessions",
                newName: "Code");

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_QuizSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "QuizSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizSessions_Code",
                table: "QuizSessions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SessionId",
                table: "Teams",
                column: "SessionId");
        }
    }
}
