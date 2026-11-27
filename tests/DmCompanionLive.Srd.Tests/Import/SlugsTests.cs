using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class SlugsTests
{
    [Theory]
    [InlineData("Aboleth", "aboleth")]
    [InlineData("Adult Red Dragon", "adult-red-dragon")]
    [InlineData("Will-o'-Wisp", "will-o-wisp")]
    [InlineData("  Swarm of Rats ", "swarm-of-rats")]
    public void Slugs_are_lower_case_kebab_from_the_english_name(string name, string slug) =>
        Slugs.From(name).Should().Be(slug);
}
