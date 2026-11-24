using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class ViewsTests
{
    [Theory]
    [InlineData(10, 10, HpBand.Unhurt)]
    [InlineData(6, 10, HpBand.Wounded)]
    [InlineData(5, 10, HpBand.Bloodied)]
    [InlineData(1, 10, HpBand.Bloodied)]
    [InlineData(0, 10, HpBand.Defeated)]
    public void Hp_bands_follow_the_spec_thresholds(int current, int max, HpBand expected) =>
        CombatViews.BandFor(current, max).Should().Be(expected);

    [Fact]
    public void Players_never_see_hidden_combatants_or_exact_monster_hp()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 10, 15), Now);
        session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.ApplyDamage(Dm, goblin, 6, Now);
        session.ApplyDamage(Dm, lyra, 4, Now);
        var player = Guest();
        session.Claim(player, lyra, Now);

        var view = CombatViews.ForPlayer(session, player.GuestId);

        view.Combatants.Select(c => c.Name).Should().BeEquivalentTo("Lyra", "Goblin");
        var goblinView = view.Combatants.Single(c => c.Name == "Goblin");
        goblinView.CurrentHp.Should().BeNull();
        goblinView.MaxHp.Should().BeNull();
        goblinView.TempHp.Should().BeNull();
        goblinView.Band.Should().Be(HpBand.Bloodied);
        var lyraView = view.Combatants.Single(c => c.Name == "Lyra");
        lyraView.CurrentHp.Should().Be(16);
        lyraView.Band.Should().BeNull();
        lyraView.IsMine.Should().BeTrue();
        lyraView.IsClaimed.Should().BeTrue();
    }

    [Fact]
    public void The_current_turn_of_a_hidden_combatant_is_not_revealed_to_players()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.SetInitiative(Dm, assassin, 25, Now);
        session.SetInitiative(Dm, lyra, 5, Now);
        session.Start(Dm, Now);

        CombatViews.ForPlayer(session, guestId: null).CurrentTurnId.Should().BeNull();
        CombatViews.ForDm(session).CurrentTurnId.Should().Be(assassin);
    }

    [Fact]
    public void The_dm_view_has_everything_in_turn_order()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 16, IsHidden: true), Now);
        session.SetInitiative(Dm, assassin, 25, Now);
        session.SetInitiative(Dm, lyra, 5, Now);

        var view = CombatViews.ForDm(session);

        view.JoinCode.Should().Be("ABC234");
        view.Version.Should().Be(session.Version);
        view.Combatants.Select(c => c.Name).Should().Equal("Assassin", "Lyra");
        view.Combatants[0].Should().BeEquivalentTo(new { CurrentHp = 30, MaxHp = 30, Ac = 16, IsHidden = true });
    }

    [Fact]
    public void Conditions_are_exposed_as_a_list_of_set_flags_in_declaration_order()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.SetConditions(Dm, lyra, Condition.Prone | Condition.Poisoned, Now);

        CombatViews.ForDm(session).Combatants.Single().Conditions
            .Should().Equal(Condition.Poisoned, Condition.Prone);
        CombatViews.ForPlayer(session, guestId: null).Combatants.Single().Conditions
            .Should().Equal(Condition.Poisoned, Condition.Prone);
    }

    [Fact]
    public void No_conditions_is_an_empty_list()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        CombatViews.ForDm(session).Combatants.Single().Conditions.Should().BeEmpty();
    }
}
