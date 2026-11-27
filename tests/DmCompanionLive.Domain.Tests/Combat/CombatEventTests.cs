using System.Collections.Immutable;
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class CombatEventTests
{
    [Fact]
    public void Events_about_a_combatant_hidden_at_that_moment_are_not_visible_to_players()
    {
        var session = NewSession();
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.ApplyDamage(Dm, assassin, 5, Now);
        session.Reveal(Dm, assassin, Now);
        session.ApplyDamage(Dm, assassin, 5, Now);
        session.RollMonsterInitiative(Dm, new FixedDice(10), Now);

        session.UncommittedEvents.Select(e => (e.Type, e.VisibleToPlayers)).Should().Equal(
            ("MonstersAdded", false),
            ("DamageApplied", false),
            ("Revealed", true),
            ("DamageApplied", true),
            ("MonsterInitiativeRolled", true));
    }

    [Fact]
    public void Event_parameters_are_immutable()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        session.ApplyDamage(Dm, session.Combatants.Single().Id, 3, Now);

        session.UncommittedEvents.Single().Parameters.Should().BeAssignableTo<IImmutableDictionary<string, string>>();
    }
}
