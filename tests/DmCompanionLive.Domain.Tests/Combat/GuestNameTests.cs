using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class GuestNameTests
{
    [Fact]
    public void Names_are_trimmed()
    {
        GuestName.Validate("  Ana ").Should().Be("Ana");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_are_rejected(string? name)
    {
        var act = () => GuestName.Validate(name);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Names_longer_than_the_limit_are_rejected()
    {
        var act = () => GuestName.Validate(new string('a', GuestName.MaxLength + 1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }
}
