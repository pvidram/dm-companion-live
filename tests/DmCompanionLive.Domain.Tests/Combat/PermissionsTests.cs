using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class PermissionsTests
{
    [Fact]
    public void Another_dm_cannot_command_the_session()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.AddMonster(OtherDm, new MonsterSeed("Goblin", 7, 15), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void A_guest_cannot_touch_a_character_they_have_not_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var act = () => session.ApplyDamage(Guest(), lyra, 3, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
    }

    [Fact]
    public void A_guest_controls_the_character_they_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        var player = Guest("Ana");

        session.Claim(player, lyra.Id, Now);
        session.ApplyDamage(player, lyra.Id, 5, Now);
        session.Heal(player, lyra.Id, 2, Now);
        session.SetInitiative(player, lyra.Id, 14, Now);
        session.SetConditions(player, lyra.Id, Condition.Prone | Condition.Poisoned, Now);

        lyra.ClaimedBy.Should().Be(player.GuestId);
        lyra.CurrentHp.Should().Be(17);
        lyra.Initiative.Should().Be(14);
        lyra.Conditions.Should().Be(Condition.Prone | Condition.Poisoned);
        session.UncommittedEvents.First().ActorName.Should().Be("Ana");
    }

    [Fact]
    public void Monsters_cannot_be_claimed()
    {
        var session = NewSession();
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);

        var act = () => session.Claim(Guest(), goblin, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
    }

    [Fact]
    public void A_character_claimed_by_someone_else_cannot_be_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Claim(Guest("Ana"), lyra, Now);

        var act = () => session.Claim(Guest("Bea"), lyra, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.AlreadyClaimed);
    }

    [Fact]
    public void A_guest_can_only_hold_one_character_but_reclaiming_their_own_is_fine()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var lyra = session.Combatants[0].Id;
        var torvald = session.Combatants[1].Id;
        var player = Guest("Ana");
        session.Claim(player, lyra, Now);

        var reclaim = () => session.Claim(player, lyra, Now);
        var second = () => session.Claim(player, torvald, Now);

        reclaim.Should().NotThrow();
        second.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.AlreadyHasCharacter);
    }

    [Fact]
    public void The_dm_does_not_claim_characters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.Claim(Dm, session.Combatants.Single().Id, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void Guests_cannot_run_dm_commands()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.Start(Guest(), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void Errors_never_tell_a_guest_whether_a_hidden_combatant_exists()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var hidden = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        var player = Guest();

        var onHidden = () => session.ApplyDamage(player, hidden, 1, Now);
        var onMissing = () => session.ApplyDamage(player, Guid.NewGuid(), 1, Now);
        var claimHidden = () => session.Claim(player, hidden, Now);
        var claimMissing = () => session.Claim(player, Guid.NewGuid(), Now);

        onHidden.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
        onMissing.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
        claimHidden.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
        claimMissing.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
    }

    [Fact]
    public void Guests_cannot_run_any_dm_command()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);
        session.Start(Dm, Now);

        var commands = new Dictionary<string, Action>
        {
            ["AddMonster"] = () => session.AddMonster(player, new MonsterSeed("Goblin", 7, 15), Now),
            ["RollMonsterInitiative"] = () => session.RollMonsterInitiative(player, new FixedDice(10), Now),
            ["Start"] = () => session.Start(player, Now),
            ["NextTurn"] = () => session.NextTurn(player, Now),
            ["Reveal"] = () => session.Reveal(player, lyra, Now),
            ["Remove"] = () => session.Remove(player, lyra, Now),
            ["End"] = () => session.End(player, Now),
        };

        foreach (var (name, command) in commands)
        {
            command.Should().Throw<DomainException>(name).Which.Code.Should().Be(DomainErrorCode.NotAllowed, name);
        }
    }

    [Fact]
    public void Undefined_condition_values_are_rejected()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var act = () => session.SetConditions(Dm, lyra, (Condition)(1 << 15), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        ConditionSet.All.Should().Be(Enum.GetValues<Condition>().Aggregate((all, c) => all | c));
    }
}
