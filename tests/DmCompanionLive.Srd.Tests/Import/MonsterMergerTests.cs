using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class MonsterMergerTests
{
    private static Dictionary<string, string> EnglishRow(string name, string ac = "15") => new()
    {
        ["name"] = name,
        ["ac"] = ac,
        ["hp_number"] = "10",
        ["initiative"] = "2",
        ["speed"] = "Speed 30 ft.",
        ["skills"] = "NA",
        ["resistances"] = "NA",
        ["vulnerabilities"] = "NA",
        ["immunities"] = "NA",
        ["gear"] = "NA",
        ["senses"] = "NA",
        ["languages"] = "NA",
        ["full_text"] = $"{name}\nSmall Fey (Goblinoid), Chaotic Neutral\nCR 1/4 (XP 50; PB +2)\nActions\nScimitar. It slashes.",
    };

    [Fact]
    public void Matching_entries_become_one_bilingual_monster_with_the_spanish_numbers()
    {
        var monster = MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Warrior")], TextWriter.Null)
            .Should().ContainSingle().Subject;

        monster.Slug.Should().Be("goblin-warrior");
        monster.Name.Should().Be(new LocalizedText("Goblin Warrior", "Guerrero goblin"));
        monster.Type.Should().Be("Fey");
        monster.Sizes.Should().Equal("Small");
        monster.Cr.Should().Be("1/4");
        (monster.Ac, monster.HpAverage, monster.HpFormula).Should().Be((15, 10, "3d6"));
        (monster.InitiativeBonus, monster.DexModifier).Should().Be((2, 2));
        monster.Abilities.Should().Equal(8, 15, 10, 10, 8, 8);
        monster.StatBlock.En.Sections[0].Entries[0].Name.Should().Be("Scimitar");
        monster.StatBlock.Es.Sections[0].Entries[0].Name.Should().Be("Cimitarra");
    }

    [Fact]
    public void A_creature_missing_from_either_source_stops_the_import()
    {
        var act = () => MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Boss")], TextWriter.Null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Goblin Warrior*Goblin Boss*");
    }

    [Fact]
    public void Disagreeing_numbers_are_reported_and_the_spanish_value_wins()
    {
        var warnings = new StringWriter();

        var monster = MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Warrior", ac: "16")], warnings)[0];

        monster.Ac.Should().Be(15);
        warnings.ToString().Should().Contain("Goblin Warrior: AC");
    }
}
