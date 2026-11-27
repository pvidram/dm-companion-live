using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmCompanionLive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonsterSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "SourceRef", table: "combatants", newName: "SourceId");

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "combatants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "InitiativeBonus",
                table: "combatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Plan 2 rolled monster initiative with the Dex modifier, and stored sources as "srd:<slug>" or "template:<id>".
            migrationBuilder.Sql("""
                UPDATE combatants SET "InitiativeBonus" = "DexModifier";
                UPDATE combatants SET "SourceKind" = 'Srd', "SourceId" = substr("SourceId", 5) WHERE "SourceId" LIKE 'srd:%';
                UPDATE combatants SET "SourceKind" = 'Template', "SourceId" = substr("SourceId", 10) WHERE "SourceId" LIKE 'template:%';
                UPDATE combatants SET "SourceId" = NULL WHERE "SourceKind" = 'None';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE combatants SET "SourceId" = 'srd:' || "SourceId" WHERE "SourceKind" = 'Srd';
                UPDATE combatants SET "SourceId" = 'template:' || "SourceId" WHERE "SourceKind" = 'Template';
                """);
            migrationBuilder.DropColumn(name: "InitiativeBonus", table: "combatants");
            migrationBuilder.DropColumn(name: "SourceKind", table: "combatants");
            migrationBuilder.RenameColumn(name: "SourceId", table: "combatants", newName: "SourceRef");
        }
    }
}
