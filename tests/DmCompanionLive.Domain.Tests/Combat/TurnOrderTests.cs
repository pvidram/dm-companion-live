using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class TurnOrderTests
{
    [Fact]
    public void Turn_order_is_initiative_desc_then_dex_desc_then_insertion_with_unset_last()
    {
        var session = NewSession(
            new PcSeed("Lyra", 20, 15, DexModifier: 2),
            new PcSeed("Torvald", 30, 18, DexModifier: 0),
            new PcSeed("Varek", 25, 16, DexModifier: 2),
            new PcSeed("Nim", 18, 14, DexModifier: 4));
        var (lyra, torvald, varek, _) = Ids(session);

        session.SetInitiative(Dm, lyra, 15, Now);
        session.SetInitiative(Dm, torvald, 15, Now);
        session.SetInitiative(Dm, varek, 15, Now);

        session.TurnOrder.Select(c => c.Name).Should().Equal("Lyra", "Varek", "Torvald", "Nim");
    }

    [Fact]
    public void Rolling_monster_initiative_only_rolls_monsters_without_one()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15, DexModifier: 2), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13, DexModifier: 1), Now);
        session.SetInitiative(Dm, orc, 9, Now);

        session.RollMonsterInitiative(Dm, new FixedDice(11), Now);

        Find(session, goblin).Initiative.Should().Be(13);
        Find(session, orc).Initiative.Should().Be(9);
        session.Combatants.Single(c => c.Kind == CombatantKind.Pc).Initiative.Should().BeNull();
    }

    [Fact]
    public void Start_begins_round_one_with_the_highest_initiative()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var (lyra, torvald, _, _) = Ids(session);
        session.SetInitiative(Dm, lyra, 8, Now);
        session.SetInitiative(Dm, torvald, 17, Now);

        session.Start(Dm, Now);

        session.Status.Should().Be(SessionStatus.Active);
        session.Round.Should().Be(1);
        session.CurrentTurnId.Should().Be(torvald);
    }

    [Fact]
    public void Next_turn_wraps_to_a_new_round()
    {
        var session = StartedSessionWithTwoPcs(out var first, out var second);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(second);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(first);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Next_turn_skips_defeated_monsters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13), Now);
        session.SetInitiative(Dm, lyra, 20, Now);
        session.SetInitiative(Dm, goblin, 15, Now);
        session.SetInitiative(Dm, orc, 10, Now);
        session.Start(Dm, Now);
        session.ApplyDamage(Dm, goblin, 7, Now);

        session.NextTurn(Dm, Now);

        session.CurrentTurnId.Should().Be(orc);
    }

    [Fact]
    public void Next_turn_requires_an_active_session()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.NextTurn(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionNotActive);
    }

    [Fact]
    public void Start_requires_someone_able_to_act()
    {
        var session = NewSession();

        var act = () => session.Start(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NoCombatants);
        session.Status.Should().Be(SessionStatus.Lobby);
    }

    [Fact]
    public void Start_twice_is_an_invalid_state()
    {
        var session = StartedSessionWithTwoPcs(out _, out _);

        var act = () => session.Start(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidState);
    }

    [Fact]
    public void Lowering_the_initiative_of_who_just_acted_neither_skips_nor_repeats_turns()
    {
        var session = StartedSessionWithThreePcs(out var a, out var b, out var c);
        session.NextTurn(Dm, Now);
        session.SetInitiative(Dm, b, 5, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(a);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Raising_the_initiative_of_who_has_not_acted_keeps_their_turn_this_round()
    {
        var session = StartedSessionWithThreePcs(out _, out _, out var c);
        session.NextTurn(Dm, Now);
        session.SetInitiative(Dm, c, 25, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void A_monster_added_mid_round_acts_in_the_same_round()
    {
        var session = StartedSessionWithTwoPcs(out var first, out var second);
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        session.SetInitiative(Dm, goblin, 15, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(goblin);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(second);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(first);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Next_turn_fails_when_every_combatant_is_defeated()
    {
        var session = NewSession();
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13), Now);
        session.Start(Dm, Now);
        session.ApplyDamage(Dm, goblin, 7, Now);
        session.ApplyDamage(Dm, orc, 15, Now);

        var act = () => session.NextTurn(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NoCombatants);
    }

    [Fact]
    public void A_single_combatant_gets_a_turn_every_round()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        session.Start(Dm, Now);

        session.NextTurn(Dm, Now);
        session.NextTurn(Dm, Now);

        session.CurrentTurnId.Should().Be(session.Combatants.Single().Id);
        session.Round.Should().Be(3);
    }

    private static CombatSession StartedSessionWithThreePcs(out Guid a, out Guid b, out Guid c)
    {
        var session = NewSession(new PcSeed("Ana", 20, 15), new PcSeed("Bo", 20, 15), new PcSeed("Cy", 20, 15));
        (a, b, c, _) = Ids(session);
        session.SetInitiative(Dm, a, 20, Now);
        session.SetInitiative(Dm, b, 15, Now);
        session.SetInitiative(Dm, c, 10, Now);
        session.Start(Dm, Now);
        return session;
    }

    private static CombatSession StartedSessionWithTwoPcs(out Guid first, out Guid second)
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        (first, second, _, _) = Ids(session);
        session.SetInitiative(Dm, first, 18, Now);
        session.SetInitiative(Dm, second, 12, Now);
        session.Start(Dm, Now);
        return session;
    }

    private static (Guid, Guid, Guid, Guid) Ids(CombatSession session)
    {
        var ids = session.Combatants.Select(c => c.Id).Concat(Enumerable.Repeat(Guid.Empty, 4)).ToArray();
        return (ids[0], ids[1], ids[2], ids[3]);
    }

    private static Combatant Find(CombatSession session, Guid id) => session.Combatants.Single(c => c.Id == id);
}
