using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHostTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RandomDrawStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    MinValue = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxValue = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcernedPlayerIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    DrawnValue = table.Column<int>(type: "INTEGER", nullable: true),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RandomDrawStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RandomDrawStates_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrawPollStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AllowMultipleVotes = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResultsRevealed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcernedPlayerIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrawPollStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrawPollStates_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RandomDrawGuesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RandomDrawStateId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    GuessValue = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RandomDrawGuesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RandomDrawGuesses_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RandomDrawGuesses_RandomDrawStates_RandomDrawStateId",
                        column: x => x.RandomDrawStateId,
                        principalTable: "RandomDrawStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrawPollVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StrawPollStateId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    OptionId = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrawPollVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrawPollVotes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StrawPollVotes_StrawPollStates_StrawPollStateId",
                        column: x => x.StrawPollStateId,
                        principalTable: "StrawPollStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RandomDrawGuesses_PlayerId",
                table: "RandomDrawGuesses",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RandomDrawGuesses_RandomDrawStateId_PlayerId",
                table: "RandomDrawGuesses",
                columns: new[] { "RandomDrawStateId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RandomDrawStates_SessionId",
                table: "RandomDrawStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StrawPollStates_SessionId",
                table: "StrawPollStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StrawPollVotes_PlayerId",
                table: "StrawPollVotes",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_StrawPollVotes_StrawPollStateId_PlayerId_OptionId",
                table: "StrawPollVotes",
                columns: new[] { "StrawPollStateId", "PlayerId", "OptionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RandomDrawGuesses");

            migrationBuilder.DropTable(
                name: "StrawPollVotes");

            migrationBuilder.DropTable(
                name: "RandomDrawStates");

            migrationBuilder.DropTable(
                name: "StrawPollStates");
        }
    }
}
