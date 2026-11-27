using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class AddMonstersTests
{
    private static readonly MonsterSeed Goblin = new(
        "Goblin Warrior", 10, 15, DexModifier: 2, Source: MonsterSource.Srd("goblin-warrior"),
        InitiativeBonus: 2, HpFormula: HpFormula.Parse("3d6"));

    private static IEnumerable<string> MonsterNamesIn(CombatSession session) =>
        session.Combatants.Where(c => c.Kind == CombatantKind.Monster).Select(c => c.Name);

    [Fact]
    public void A_single_monster_keeps_its_name()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior");
    }

    [Fact]
    public void Several_monsters_are_numbered_after_the_highest_existing_number()
    {
        var session = NewSession();
        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);
        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior", "Goblin Warrior 2", "Goblin Warrior 3", "Goblin Warrior 4");
    }

    [Fact]
    public void Numbering_starts_at_one_and_ignores_longer_names_with_the_same_start()
    {
        var session = NewSession();
        session.AddMonster(Dm, new MonsterSeed("Goblin Warrior Boss", 21, 17), Now);

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior Boss", "Goblin Warrior 1", "Goblin Warrior 2");
    }

    [Fact]
    public void Visible_monsters_are_numbered_only_against_visible_names()
    {
        var session = NewSession();
        var hidden = Goblin with { IsHidden = true };
        session.AddMonsters(Dm, hidden, 2, hpDice: null, Now);

        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);
        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal(
            "Goblin Warrior 1", "Goblin Warrior 2", "Goblin Warrior", "Goblin Warrior 2", "Goblin Warrior 3");
    }

    [Fact]
    public void Hidden_monsters_are_numbered_against_every_name()
    {
        var session = NewSession();
        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        session.AddMonsters(Dm, Goblin with { IsHidden = true }, 2, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior", "Goblin Warrior 2", "Goblin Warrior 3");
    }

    [Fact]
    public void A_huge_numeric_suffix_is_ignored_when_numbering()
    {
        var session = NewSession();
        session.AddMonster(Dm, new MonsterSeed("Goblin Warrior 2147483647", 7, 15), Now);

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior 2147483647", "Goblin Warrior 1", "Goblin Warrior 2");
    }

    [Fact]
    public void Monsters_get_average_hp_unless_dice_are_given()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);
        session.AddMonsters(Dm, Goblin, 2, new FixedDice(1, 1, 1, 6, 6, 6), Now);

        session.Combatants.Select(c => (c.MaxHp, c.CurrentHp)).Should().Equal((10, 10), (10, 10), (3, 3), (18, 18));
    }

    [Fact]
    public void Without_a_formula_dice_are_ignored()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { HpFormula = null }, 1, new FixedDice(), Now);

        session.Combatants.Single().MaxHp.Should().Be(10);
    }

    [Fact]
    public void Monsters_keep_their_source_and_initiative_bonus()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { DexModifier = -1, InitiativeBonus = 7 }, 1, hpDice: null, Now);

        var goblin = session.Combatants.Single();
        (goblin.SourceKind, goblin.SourceId).Should().Be((MonsterSourceKind.Srd, "goblin-warrior"));
        (goblin.InitiativeBonus, goblin.DexModifier).Should().Be((7, -1));
    }

    [Fact]
    public void Without_an_initiative_bonus_the_dex_modifier_is_used()
    {
        var session = NewSession();

        session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13, DexModifier: 1), Now);

        var orc = session.Combatants.Single();
        orc.InitiativeBonus.Should().Be(1);
        (orc.SourceKind, orc.SourceId).Should().Be((MonsterSourceKind.None, (string?)null));
    }

    [Fact]
    public void Monster_initiative_rolls_add_the_initiative_bonus_not_the_dex_modifier()
    {
        var session = NewSession();
        session.AddMonsters(Dm, Goblin with { DexModifier = -1, InitiativeBonus = 7 }, 1, hpDice: null, Now);

        session.RollMonsterInitiative(Dm, new FixedDice(10), Now);

        session.Combatants.Single().Initiative.Should().Be(17);
    }

    [Fact]
    public void One_command_bumps_the_version_once_and_logs_one_entry_without_hit_points()
    {
        var session = NewSession();
        var before = session.Version;

        var ids = session.AddMonsters(Dm, Goblin, 3, new FixedDice(1, 2, 3, 4, 5, 6, 1, 2, 3), Now);

        session.Version.Should().Be(before + 1);
        var entry = session.UncommittedEvents.Should().ContainSingle().Subject;
        entry.Type.Should().Be("MonstersAdded");
        entry.VisibleToPlayers.Should().BeTrue();
        entry.Parameters.Keys.Should().BeEquivalentTo("combatantId", "name", "kind", "baseName", "count", "combatantIds");
        entry.Parameters["baseName"].Should().Be("Goblin Warrior");
        entry.Parameters["count"].Should().Be("3");
        entry.Parameters["combatantIds"].Should().Be(string.Join(',', ids));
    }

    [Fact]
    public void Hidden_monsters_are_logged_out_of_players_sight()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { IsHidden = true }, 2, hpDice: null, Now);

        session.UncommittedEvents.Single().VisibleToPlayers.Should().BeFalse();
        session.Combatants.Should().OnlyContain(c => c.IsHidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void One_to_twenty_monsters_can_be_added_at_a_time(int count)
    {
        var session = NewSession();

        var act = () => session.AddMonsters(Dm, Goblin, count, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void An_out_of_range_initiative_bonus_is_invalid()
    {
        var session = NewSession();

        var act = () => session.AddMonsters(Dm, Goblin with { InitiativeBonus = 31 }, 1, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void A_rejected_command_adds_nothing()
    {
        var session = NewSession();
        var longName = new string('x', 79);

        var act = () => session.AddMonsters(Dm, Goblin with { Name = longName }, 2, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        session.Combatants.Should().BeEmpty();
        session.Version.Should().Be(0);
    }

    [Fact]
    public void Only_the_dm_of_the_session_adds_monsters()
    {
        var session = NewSession();

        var act = () => session.AddMonsters(OtherDm, Goblin, 2, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }
}
