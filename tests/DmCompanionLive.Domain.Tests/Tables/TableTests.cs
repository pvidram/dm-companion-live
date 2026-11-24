using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Tables;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Tables;

public class TableTests
{
    [Fact]
    public void A_table_manages_its_characters()
    {
        var table = Table.Create(DmId, "  Costa de Sal ", Now);

        var lyra = table.AddCharacter("Lyra", 20, 15, 2);
        table.AddCharacter("Torvald", 30, 18, 0);
        table.UpdateCharacter(lyra.Id, "Lyra Vientoalto", 22, 15, 3);
        table.RemoveCharacter(table.Characters[1].Id);
        table.Rename("Costa de Sal II");

        table.Name.Should().Be("Costa de Sal II");
        table.Characters.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "Lyra Vientoalto", MaxHp = 22, Ac = 15, DexModifier = 3 });
    }

    [Fact]
    public void Starting_a_session_copies_the_characters_and_the_owner()
    {
        var table = Table.Create(DmId, "Costa de Sal", Now);
        table.AddCharacter("Lyra", 20, 15, 2);

        var session = table.StartSession("ABC234", Now);

        session.TableId.Should().Be(table.Id);
        session.OwnerId.Should().Be(DmId);
        session.Combatants.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "Lyra", Kind = CombatantKind.Pc, MaxHp = 20, DexModifier = 2 });
    }

    [Fact]
    public void Invalid_values_are_rejected()
    {
        var table = Table.Create(DmId, "Costa de Sal", Now);

        var emptyName = () => Table.Create(DmId, " ", Now);
        var badCharacter = () => table.AddCharacter("Lyra", 0, 15, 0);
        var unknown = () => table.RemoveCharacter(Guid.NewGuid());

        emptyName.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        badCharacter.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        unknown.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
    }
}
