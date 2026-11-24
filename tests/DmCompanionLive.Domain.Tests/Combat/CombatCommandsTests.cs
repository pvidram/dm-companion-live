using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class CombatCommandsTests
{
    [Fact]
    public void Reveal_makes_a_hidden_monster_visible()
    {
        var session = NewSession();
        var id = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);

        session.Reveal(Dm, id, Now);

        session.Combatants.Single().IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Removing_the_current_combatant_passes_the_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var lyra = session.Combatants[0].Id;
        var torvald = session.Combatants[1].Id;
        session.SetInitiative(Dm, lyra, 18, Now);
        session.SetInitiative(Dm, torvald, 10, Now);
        session.Start(Dm, Now);

        session.Remove(Dm, lyra, Now);

        session.Combatants.Should().ContainSingle(c => c.Id == torvald);
        session.CurrentTurnId.Should().Be(torvald);
    }

    [Fact]
    public void Removing_the_last_combatant_clears_the_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Start(Dm, Now);

        session.Remove(Dm, lyra, Now);

        session.CurrentTurnId.Should().BeNull();
    }

    [Fact]
    public void Nothing_changes_after_the_combat_ends()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Start(Dm, Now);

        session.End(Dm, Now);
        var act = () => session.ApplyDamage(Dm, lyra, 1, Now);

        session.Status.Should().Be(SessionStatus.Ended);
        session.CurrentTurnId.Should().BeNull();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionEnded);
    }

    [Fact]
    public void Only_the_dm_reveals_removes_and_ends()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);

        var reveal = () => session.Reveal(player, lyra, Now);
        var remove = () => session.Remove(player, lyra, Now);
        var end = () => session.End(player, Now);

        reveal.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
        remove.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
        end.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }
}
