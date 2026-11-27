using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

internal static class TestMonsters
{
    private static readonly StatBlock Empty = new(string.Empty, [], []);

    public static SrdMonster Create(string slug, string en, string es) => new(
        slug, new LocalizedText(en, es), "Monstrosity", ["Large"], "3", 13, 59, "7d10+21", 1, 1,
        [20, 12, 17, 3, 12, 7], new LocalizedStatBlock(Empty, Empty));
}
