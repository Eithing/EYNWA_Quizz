using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsParticipantsAndThemePicker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequiresTargetPlayer",
                table: "Rounds",
                newName: "RestrictsParticipants");

            migrationBuilder.RenameColumn(
                name: "CurrentRoundTargetPlayerId",
                table: "GameSessions",
                newName: "CurrentThemeSubRoundId");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "ScoreAdjustments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "ScoreAdjustments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsThemePicker",
                table: "Rounds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentRoundId",
                table: "Rounds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TeamScoringEnabled",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Answers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThemeStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubRoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRevealed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemeStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThemeStates_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThemeStates_Rounds_SubRoundId",
                        column: x => x.SubRoundId,
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoundParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundParticipants_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoundParticipants_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoundParticipants_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreAdjustments_TeamId",
                table: "ScoreAdjustments",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ParentRoundId",
                table: "Rounds",
                column: "ParentRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Answers_TeamId",
                table: "Answers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundParticipants_PlayerId",
                table: "RoundParticipants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundParticipants_SessionId",
                table: "RoundParticipants",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundParticipants_TeamId",
                table: "RoundParticipants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SessionId",
                table: "Teams",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStates_SessionId",
                table: "ThemeStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStates_SubRoundId",
                table: "ThemeStates",
                column: "SubRoundId");

            migrationBuilder.AddForeignKey(
                name: "FK_Answers_Teams_TeamId",
                table: "Answers",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Teams_TeamId",
                table: "Players",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds",
                column: "ParentRoundId",
                principalTable: "Rounds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScoreAdjustments_Teams_TeamId",
                table: "ScoreAdjustments",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Answers_Teams_TeamId",
                table: "Answers");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_Teams_TeamId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_Rounds_Rounds_ParentRoundId",
                table: "Rounds");

            migrationBuilder.DropForeignKey(
                name: "FK_ScoreAdjustments_Teams_TeamId",
                table: "ScoreAdjustments");

            migrationBuilder.DropTable(
                name: "RoundParticipants");

            migrationBuilder.DropTable(
                name: "ThemeStates");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_ScoreAdjustments_TeamId",
                table: "ScoreAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_Rounds_ParentRoundId",
                table: "Rounds");

            migrationBuilder.DropIndex(
                name: "IX_Players_TeamId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Answers_TeamId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "ScoreAdjustments");

            migrationBuilder.DropColumn(
                name: "IsThemePicker",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "ParentRoundId",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TeamScoringEnabled",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Answers");

            migrationBuilder.RenameColumn(
                name: "RestrictsParticipants",
                table: "Rounds",
                newName: "RequiresTargetPlayer");

            migrationBuilder.RenameColumn(
                name: "CurrentThemeSubRoundId",
                table: "GameSessions",
                newName: "CurrentRoundTargetPlayerId");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "ScoreAdjustments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
