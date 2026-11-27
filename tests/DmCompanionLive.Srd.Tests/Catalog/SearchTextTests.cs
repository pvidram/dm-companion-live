using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SearchTextTests
{
    [Theory]
    [InlineData("  Dragón   ROJO ", "dragon rojo")]
    [InlineData("Ñu", "nu")]
    [InlineData("", "")]
    public void Normalising_removes_accents_case_and_extra_spaces(string text, string expected) =>
        SearchText.Normalize(text).Should().Be(expected);
}
