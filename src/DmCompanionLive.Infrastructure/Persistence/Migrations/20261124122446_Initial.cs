using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmCompanionLive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "combat_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TableId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinCode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    CurrentTurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combat_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dm_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dm_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "combatants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Initiative = table.Column<int>(type: "integer", nullable: true),
                    DexModifier = table.Column<int>(type: "integer", nullable: false),
                    MaxHp = table.Column<int>(type: "integer", nullable: false),
                    CurrentHp = table.Column<int>(type: "integer", nullable: false),
                    TempHp = table.Column<int>(type: "integer", nullable: false),
                    Ac = table.Column<int>(type: "integer", nullable: false),
                    Conditions = table.Column<int>(type: "integer", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ClaimedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combatants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_combatants_combat_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "combat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MaxHp = table.Column<int>(type: "integer", nullable: false),
                    Ac = table.Column<int>(type: "integer", nullable: false),
                    DexModifier = table.Column<int>(type: "integer", nullable: false),
                    TableId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_player_characters_tables_TableId",
                        column: x => x.TableId,
                        principalTable: "tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_combat_sessions_JoinCode",
                table: "combat_sessions",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_combat_sessions_OwnerId",
                table: "combat_sessions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_combatants_SessionId",
                table: "combatants",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_dm_users_ExternalId",
                table: "dm_users",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_characters_TableId",
                table: "player_characters",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_tables_OwnerId",
                table: "tables",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "combatants");

            migrationBuilder.DropTable(
                name: "dm_users");

            migrationBuilder.DropTable(
                name: "player_characters");

            migrationBuilder.DropTable(
                name: "combat_sessions");

            migrationBuilder.DropTable(
                name: "tables");
        }
    }
}
