namespace DmCompanionLive.Domain.Tests;

public class NamesTests
{
    [Fact]
    public void A_name_is_trimmed() => Names.Validate("  Lyra ").Should().Be("Lyra");

    [Fact]
    public void Eighty_characters_are_allowed() => Names.Validate(new string('a', 80)).Should().HaveLength(80);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_name_is_invalid(string? name)
    {
        var act = () => Names.Validate(name);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Eighty_one_characters_are_invalid()
    {
        var act = () => Names.Validate(new string('a', 81));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }
}
