using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class HitPointsTests
{
    [Fact]
    public void New_session_starts_in_lobby_with_full_hp_characters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));

        session.Status.Should().Be(SessionStatus.Lobby);
        session.Version.Should().Be(0);
        session.Combatants.Should().HaveCount(2);
        session.Combatants.Should().OnlyContain(c => c.Kind == CombatantKind.Pc && c.CurrentHp == c.MaxHp);
    }

    [Fact]
    public void Damage_consumes_temporary_hp_first()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();

        session.SetTempHp(Dm, lyra.Id, 5, Now);
        session.ApplyDamage(Dm, lyra.Id, 8, Now);

        lyra.TempHp.Should().Be(0);
        lyra.CurrentHp.Should().Be(17);
    }

    [Fact]
    public void Hp_never_drops_below_zero_and_a_pc_at_zero_is_unconscious()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();

        session.ApplyDamage(Dm, lyra.Id, 50, Now);

        lyra.CurrentHp.Should().Be(0);
        lyra.Conditions.Should().HaveFlag(Condition.Unconscious);
        lyra.IsDefeated.Should().BeFalse("a PC at 0 HP still takes turns");
    }

    [Fact]
    public void Healing_is_capped_at_max_and_wakes_an_unconscious_pc()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        session.ApplyDamage(Dm, lyra.Id, 25, Now);

        session.Heal(Dm, lyra.Id, 100, Now);

        lyra.CurrentHp.Should().Be(20);
        lyra.Conditions.Should().NotHaveFlag(Condition.Unconscious);
    }

    [Fact]
    public void Extreme_amounts_do_not_overflow_hp()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        session.ApplyDamage(Dm, lyra.Id, 5, Now);

        session.Heal(Dm, lyra.Id, int.MaxValue, Now);
        lyra.CurrentHp.Should().Be(lyra.MaxHp);

        session.ApplyDamage(Dm, lyra.Id, int.MaxValue, Now);
        lyra.CurrentHp.Should().Be(0);
    }

    [Fact]
    public void Events_about_a_combatant_carry_its_kind()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);

        session.Heal(Dm, lyra.Id, 1, Now);
        session.Heal(Dm, goblin, 1, Now);

        session.UncommittedEvents[^2].Parameters.Should().Contain("kind", "Pc");
        session.UncommittedEvents[^1].Parameters.Should().Contain("kind", "Monster");
    }

    [Fact]
    public void A_monster_at_zero_hp_is_defeated()
    {
        var session = NewSession();
        var goblinId = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);

        session.ApplyDamage(Dm, goblinId, 7, Now);

        session.Combatants.Single(c => c.Id == goblinId).IsDefeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Damage_and_healing_must_be_positive(int amount)
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var id = session.Combatants.Single().Id;

        var damage = () => session.ApplyDamage(Dm, id, amount, Now);
        var heal = () => session.Heal(Dm, id, amount, Now);

        damage.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        heal.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Invalid_seeds_are_rejected()
    {
        var noHp = () => NewSession(new PcSeed("Lyra", 0, 15));
        var noName = () => NewSession(new PcSeed("  ", 10, 15));

        noHp.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        noName.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Every_change_increments_the_version_and_records_an_event()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var id = session.Combatants.Single().Id;

        session.ApplyDamage(Dm, id, 3, Now);
        session.Heal(Dm, id, 1, Now);

        session.Version.Should().Be(2);
        session.UncommittedEvents.Select(e => e.Type).Should().Equal("DamageApplied", "Healed");
        session.UncommittedEvents[0].Parameters.Should().Contain("amount", "3").And.Contain("name", "Lyra");
        session.UncommittedEvents[0].ActorName.Should().Be("DM");

        session.ClearUncommittedEvents();
        session.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void Temporary_hp_cannot_be_negative_and_replaces_rather_than_stacks()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var negative = () => session.SetTempHp(Dm, lyra, -1, Now);
        session.SetTempHp(Dm, lyra, 5, Now);
        session.SetTempHp(Dm, lyra, 3, Now);

        negative.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        session.Combatants.Single().TempHp.Should().Be(3);
    }
}
