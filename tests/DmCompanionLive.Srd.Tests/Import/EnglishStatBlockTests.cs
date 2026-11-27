using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class EnglishStatBlockTests
{
    private const string GoblinWarrior = """
        Goblin Warrior
        Small Fey (Goblinoid), Chaotic Neutral
        AC 15		                   Initiative +2 (12)
        HP 10 (3d6)
        Speed 30 ft.
        MOD SAVE            MOD SAVE           MOD SAVE
        Str 8 −1 −1          Dex 15 +2 +2        Con 10 +0 +0
        Int 10 +0 +0         Wis 8 −1 −1         Cha 8 −1 −1
        Skills Stealth +6
        Gear Leather Armor, Scimitar, Shield, Shortbow
        Senses Darkvision 60 ft.; Passive Perception 9
        Languages Common, Goblin
        CR 1/4 (XP 50; PB +2)
        Actions
        Scimitar. Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6
        + 2) Slashing damage, plus 2 (1d4) Slashing damage if
        the attack roll had Advantage.
        Shortbow. Ranged Attack Roll: +4, range 80/320 ft.
        Hit: 5 (1d6 + 2) Piercing damage, plus 2 (1d4) Piercing
        damage if the attack roll had Advantage.
        Bonus Actions
        Nimble Escape. The goblin takes the Disengage or
        Hide action.
        """;

    private const string Aboleth = """
        Aboleth
        Large Aberration, Lawful Evil
        AC 17
        HP 150 (20d10 + 40)
        CR 10 (XP 5,900, or 7,200 in lair; PB +4)
        Traits
        Legendary Resistance (3/Day, or 4/Day in Lair). If
        the aboleth fails a saving throw, it can choose to suc-
        ceed instead.
        Legendary Actions
        Legendary Action Uses: 3 (4 in Lair). Immediately after
        another creature’s turn, the aboleth can expend a use.
        Lash. The aboleth makes one Tentacle attack.
        """;

    private static Dictionary<string, string> Row(string name, string fullText, string speed = "NA") => new()
    {
        ["name"] = name,
        ["full_text"] = fullText,
        ["speed"] = speed,
        ["skills"] = name == "Goblin Warrior" ? "Stealth +6" : "NA",
        ["resistances"] = "NA",
        ["vulnerabilities"] = "NA",
        ["immunities"] = "NA",
        ["gear"] = name == "Goblin Warrior" ? "Leather Armor, Scimitar, Shield, Shortbow" : "NA",
        ["senses"] = name == "Goblin Warrior" ? "Darkvision 60 ft.; Passive Perception 9" : "NA",
        ["languages"] = name == "Goblin Warrior" ? "Common, Goblin" : "NA",
    };

    [Fact]
    public void Subtitle_details_and_sections_are_read_from_the_text_and_columns()
    {
        var block = EnglishStatBlock.Parse(Row("Goblin Warrior", GoblinWarrior, "Speed 30 ft."));

        block.Subtitle.Should().Be("Small Fey (Goblinoid), Chaotic Neutral");
        block.Details.Should().Equal(
            new StatBlockDetail("Speed", "30 ft."),
            new StatBlockDetail("Skills", "Stealth +6"),
            new StatBlockDetail("Gear", "Leather Armor, Scimitar, Shield, Shortbow"),
            new StatBlockDetail("Senses", "Darkvision 60 ft.; Passive Perception 9"),
            new StatBlockDetail("Languages", "Common, Goblin"),
            new StatBlockDetail("CR", "1/4 (XP 50; PB +2)"));
        block.Sections.Select(s => s.Kind).Should().Equal(StatBlockSectionKind.Actions, StatBlockSectionKind.BonusActions);
        block.Sections[0].Entries.Should().Equal(
            new StatBlockEntry(
                "Scimitar",
                "Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6 + 2) Slashing damage, plus 2 (1d4) Slashing damage if the attack roll had Advantage."),
            new StatBlockEntry(
                "Shortbow",
                "Ranged Attack Roll: +4, range 80/320 ft. Hit: 5 (1d6 + 2) Piercing damage, plus 2 (1d4) Piercing damage if the attack roll had Advantage."));
        block.Sections[1].Entries.Should().Equal(
            new StatBlockEntry("Nimble Escape", "The goblin takes the Disengage or Hide action."));
    }

    [Fact]
    public void Hyphenated_line_breaks_are_joined_and_untitled_text_keeps_no_name()
    {
        var block = EnglishStatBlock.Parse(Row("Aboleth", Aboleth));

        block.Details.Should().Equal(new StatBlockDetail("CR", "10 (XP 5,900, or 7,200 in lair; PB +4)"));
        block.Sections[0].Entries.Should().Equal(new StatBlockEntry(
            "Legendary Resistance (3/Day, or 4/Day in Lair)",
            "If the aboleth fails a saving throw, it can choose to succeed instead."));
        block.Sections[1].Entries.Should().Equal(
            new StatBlockEntry(
                null,
                "Legendary Action Uses: 3 (4 in Lair). Immediately after another creature’s turn, the aboleth can expend a use."),
            new StatBlockEntry("Lash", "The aboleth makes one Tentacle attack."));
    }

    [Fact]
    public void A_stat_block_without_a_cr_line_is_rejected()
    {
        var act = () => EnglishStatBlock.Parse(Row("Broken", "Broken\nTiny Beast\nActions\nBite. It bites."));

        act.Should().Throw<FormatException>().WithMessage("*Broken*");
    }
}
