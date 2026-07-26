using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizParty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentQuestionIndex",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentQuestionStartedAt",
                table: "GameSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentRoundIndex",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "GameSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingPoints",
                table: "Answers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentQuestionIndex",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "CurrentQuestionStartedAt",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "CurrentRoundIndex",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "PendingPoints",
                table: "Answers");
        }
    }
}
