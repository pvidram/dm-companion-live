using System.Text.Json;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SrdJsonTests
{
    [Fact]
    public void An_entry_with_missing_members_does_not_load()
    {
        var act = () => JsonSerializer.Deserialize<List<SrdMonster>>("""[{"slug":"x"}]""", SrdJson.Options);

        act.Should().Throw<JsonException>();
    }
}
