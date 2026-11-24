using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class SessionLifecycleTests
{
    [Fact]
    public void Every_command_is_rejected_once_the_combat_has_ended()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);
        session.End(Dm, Now);

        var commands = new Dictionary<string, Action>
        {
            ["AddMonster"] = () => session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now),
            ["ApplyDamage"] = () => session.ApplyDamage(Dm, lyra, 1, Now),
            ["Heal"] = () => session.Heal(Dm, lyra, 1, Now),
            ["SetTempHp"] = () => session.SetTempHp(Dm, lyra, 1, Now),
            ["SetInitiative"] = () => session.SetInitiative(player, lyra, 10, Now),
            ["RollMonsterInitiative"] = () => session.RollMonsterInitiative(Dm, new FixedDice(10), Now),
            ["Start"] = () => session.Start(Dm, Now),
            ["NextTurn"] = () => session.NextTurn(Dm, Now),
            ["Claim"] = () => session.Claim(Guest("Bea"), lyra, Now),
            ["SetConditions"] = () => session.SetConditions(Dm, lyra, Condition.Prone, Now),
            ["Reveal"] = () => session.Reveal(Dm, lyra, Now),
            ["Remove"] = () => session.Remove(Dm, lyra, Now),
            ["End"] = () => session.End(Dm, Now),
        };

        foreach (var (name, command) in commands)
        {
            command.Should().Throw<DomainException>(name).Which.Code.Should().Be(DomainErrorCode.SessionEnded, name);
        }
    }

    [Fact]
    public void A_session_expires_after_twelve_hours_without_activity()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var later = Now + CombatSession.InactivityLimit + TimeSpan.FromSeconds(1);

        var act = () => session.ApplyDamage(Dm, lyra, 1, later);

        session.IsExpiredAt(Now + CombatSession.InactivityLimit).Should().BeFalse();
        session.IsExpiredAt(later).Should().BeTrue();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionExpired);
    }

    [Fact]
    public void Activity_keeps_a_session_alive_and_an_ended_session_never_expires()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        session.ApplyDamage(Dm, lyra, 1, Now + TimeSpan.FromHours(11));
        session.IsExpiredAt(Now + TimeSpan.FromHours(20)).Should().BeFalse();

        session.End(Dm, Now + TimeSpan.FromHours(20));
        session.IsExpiredAt(Now + TimeSpan.FromDays(10)).Should().BeFalse();
    }

    [Fact]
    public void Times_are_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.FromHours(1));

        var session = CombatSession.Create(Guid.NewGuid(), DmId, "ABC234", [new PcSeed("Lyra", 20, 15)], local);
        session.Start(Dm, local);

        session.LastActivityAt.Offset.Should().Be(TimeSpan.Zero);
        session.LastActivityAt.Should().Be(local);
        session.UncommittedEvents.Single().At.Offset.Should().Be(TimeSpan.Zero);
    }
}
