using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmCompanionLive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonsterTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_monsters",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SrdSlug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorite_monsters", x => new { x.OwnerId, x.SrdSlug });
                    table.ForeignKey(
                        name: "FK_favorite_monsters_dm_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "dm_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monster_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MaxHp = table.Column<int>(type: "integer", nullable: false),
                    Ac = table.Column<int>(type: "integer", nullable: false),
                    InitiativeBonus = table.Column<int>(type: "integer", nullable: false),
                    HpFormula = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monster_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monster_templates_dm_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "dm_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monster_templates_OwnerId",
                table: "monster_templates",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_monsters");

            migrationBuilder.DropTable(
                name: "monster_templates");
        }
    }
}
