using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJokers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AloneInTheWorldPlayerId",
                table: "GameSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AloneInTheWorldTeamId",
                table: "GameSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeFirstHolderPlayerId",
                table: "GameSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeFirstHolderTeamId",
                table: "GameSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeFirstQuestionsRemaining",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CopyPasteAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CopierPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyPasteAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CopyPasteAssignments_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CopyPasteAssignments_Players_CopierPlayerId",
                        column: x => x.CopierPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CopyPasteAssignments_Players_TargetPlayerId",
                        column: x => x.TargetPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CopyPasteAssignments_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JokerGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    Charges = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedRoundIdsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JokerGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JokerGrants_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JokerGrants_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JokerGrants_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JokerUsageEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ActorPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JokerUsageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JokerUsageEvents_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JokerUsageEvents_Players_ActorPlayerId",
                        column: x => x.ActorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JokerUsageEvents_Players_TargetPlayerId",
                        column: x => x.TargetPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JokerUsageEvents_Teams_ActorTeamId",
                        column: x => x.ActorTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QcmFiftyFiftyReveals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    HiddenOptionIdsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcmFiftyFiftyReveals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcmFiftyFiftyReveals_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QcmFiftyFiftyReveals_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QcmFiftyFiftyReveals_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopyPasteAssignments_CopierPlayerId",
                table: "CopyPasteAssignments",
                column: "CopierPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPasteAssignments_QuestionId_CopierPlayerId",
                table: "CopyPasteAssignments",
                columns: new[] { "QuestionId", "CopierPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopyPasteAssignments_SessionId",
                table: "CopyPasteAssignments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPasteAssignments_TargetPlayerId",
                table: "CopyPasteAssignments",
                column: "TargetPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerGrants_PlayerId",
                table: "JokerGrants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerGrants_SessionId",
                table: "JokerGrants",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerGrants_TeamId",
                table: "JokerGrants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerUsageEvents_ActorPlayerId",
                table: "JokerUsageEvents",
                column: "ActorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerUsageEvents_ActorTeamId",
                table: "JokerUsageEvents",
                column: "ActorTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerUsageEvents_SessionId",
                table: "JokerUsageEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_JokerUsageEvents_TargetPlayerId",
                table: "JokerUsageEvents",
                column: "TargetPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_QcmFiftyFiftyReveals_PlayerId",
                table: "QcmFiftyFiftyReveals",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_QcmFiftyFiftyReveals_QuestionId_PlayerId",
                table: "QcmFiftyFiftyReveals",
                columns: new[] { "QuestionId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcmFiftyFiftyReveals_SessionId",
                table: "QcmFiftyFiftyReveals",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyPasteAssignments");

            migrationBuilder.DropTable(
                name: "JokerGrants");

            migrationBuilder.DropTable(
                name: "JokerUsageEvents");

            migrationBuilder.DropTable(
                name: "QcmFiftyFiftyReveals");

            migrationBuilder.DropColumn(
                name: "AloneInTheWorldPlayerId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "AloneInTheWorldTeamId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "MeFirstHolderPlayerId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "MeFirstHolderTeamId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "MeFirstQuestionsRemaining",
                table: "GameSessions");
        }
    }
}
