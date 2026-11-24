using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class JoinCodeTests
{
    [Fact]
    public void Codes_have_six_unambiguous_characters()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => JoinCode.New()).ToList();

        codes.Should().OnlyContain(c => c.Length == JoinCode.Length && c.All(JoinCode.Alphabet.Contains));
        string.Concat(codes).Should().NotContainAny("0", "O", "1", "I");
        codes.Distinct().Should().HaveCountGreaterThan(190);
    }

    [Fact]
    public void Alphabet_gives_over_a_billion_combinations() =>
        Math.Pow(JoinCode.Alphabet.Length, JoinCode.Length).Should().BeGreaterThan(1_000_000_000);

    [Fact]
    public void Normalize_trims_and_uppercases() =>
        JoinCode.Normalize("  abc234 ").Should().Be("ABC234");
}
