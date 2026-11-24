using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DmCompanionLive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealTimeCombat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastTurnRound",
                table: "combatants",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "combat_log",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Parameters = table.Column<string>(type: "jsonb", nullable: false),
                    VisibleToPlayers = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combat_log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_combat_log_combat_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "combat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_combat_sessions_TableId",
                table: "combat_sessions",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_combat_log_SessionId_Version",
                table: "combat_log",
                columns: new[] { "SessionId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_combat_sessions_dm_users_OwnerId",
                table: "combat_sessions",
                column: "OwnerId",
                principalTable: "dm_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_combat_sessions_tables_TableId",
                table: "combat_sessions",
                column: "TableId",
                principalTable: "tables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tables_dm_users_OwnerId",
                table: "tables",
                column: "OwnerId",
                principalTable: "dm_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_combat_sessions_dm_users_OwnerId",
                table: "combat_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_combat_sessions_tables_TableId",
                table: "combat_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_tables_dm_users_OwnerId",
                table: "tables");

            migrationBuilder.DropTable(
                name: "combat_log");

            migrationBuilder.DropIndex(
                name: "IX_combat_sessions_TableId",
                table: "combat_sessions");

            migrationBuilder.DropColumn(
                name: "LastTurnRound",
                table: "combatants");
        }
    }
}
