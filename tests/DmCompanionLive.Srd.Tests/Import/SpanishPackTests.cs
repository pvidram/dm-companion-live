using System.Text;
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class SpanishPackTests
{
    internal const string Pack = """
        {
          "header": { "source": "compendio-srd", "lang": "es" },
          "entries": [
            {
              "key": "srd:criaturas/guerrero-goblin",
              "name": "Guerrero goblin",
              "nameEn": "Goblin Warrior",
              "creature": {
                "ac": 15, "hp": 10, "hitDice": "3d6", "initiativeBonus": 2,
                "abilities": [8, 15, 10, 10, 8, 8], "modifiers": [-1, 2, 0, 0, -1, -1],
                "cr": "1/4", "sizeKeys": ["Small"], "typeKey": "Fey",
                "typeLine": "Feérico pequeño (goblinoide), caótico neutral",
                "fields": [
                  { "label": "CA", "value": [{ "text": "15", "style": "None" }] },
                  { "label": "Iniciativa", "value": [{ "text": "+2 (12)", "style": "None" }] },
                  { "label": "PG", "value": [{ "text": "10 (3d6)", "style": "None" }] },
                  { "label": "Velocidad", "value": [{ "text": "9 m", "style": "None" }] },
                  { "label": "VD", "value": [{ "text": "1/4 (50 PX; BC +2)", "style": "None" }] }
                ],
                "sections": [
                  {
                    "key": "acciones", "title": "Acciones",
                    "items": [
                      {
                        "name": "Cimitarra",
                        "blocks": [
                          { "kind": "Paragraph", "text": [
                            { "text": "Tirada de ataque cuerpo a cuerpo:", "style": "Italic" },
                            { "text": " +4, alcance 1,5 m.", "style": "None" } ] },
                          { "kind": "ListItem", "text": [ { "text": "Ventaja: +2 (1d4).", "style": "None" } ] }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

    internal static IReadOnlyList<PackEntry> Entries() =>
        SpanishPack.Read(new MemoryStream(Encoding.UTF8.GetBytes(Pack)));

    [Fact]
    public void The_pack_is_read_with_its_numbers_and_english_name()
    {
        var entry = Entries().Should().ContainSingle().Subject;

        entry.Name.Should().Be("Guerrero goblin");
        entry.NameEn.Should().Be("Goblin Warrior");
        entry.Creature.Ac.Should().Be(15);
        entry.Creature.HitDice.Should().Be("3d6");
        entry.Creature.Modifiers[1].Should().Be(2);
    }

    [Fact]
    public void The_stat_block_skips_numeric_fields_and_flattens_runs_to_plain_text()
    {
        var block = SpanishPack.ToStatBlock(Entries()[0]);

        block.Subtitle.Should().Be("Feérico pequeño (goblinoide), caótico neutral");
        block.Details.Should().Equal(
            new StatBlockDetail("Velocidad", "9 m"),
            new StatBlockDetail("VD", "1/4 (50 PX; BC +2)"));
        block.Sections.Should().ContainSingle().Which.Kind.Should().Be(StatBlockSectionKind.Actions);
        block.Sections[0].Entries.Should().Equal(new StatBlockEntry(
            "Cimitarra",
            "Tirada de ataque cuerpo a cuerpo: +4, alcance 1,5 m.\n• Ventaja: +2 (1d4)."));
    }
}
