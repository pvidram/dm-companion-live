using DmCompanionLive.Infrastructure.Srd;
using static DmCompanionLive.Srd.Tests.Catalog.TestMonsters;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SrdCatalogTests
{
    [Fact]
    public void Search_ignores_case_and_accents_and_matches_either_language()
    {
        var catalog = new SrdCatalog([
            Create("adult-red-dragon", "Adult Red Dragon", "Dragón rojo adulto"),
            Create("owlbear", "Owlbear", "Oso lechuza"),
        ]);

        catalog.Search("DRAGON ROJO", SrdLanguage.Es, 1).Items.Select(m => m.Slug).Should().Equal("adult-red-dragon");
        catalog.Search("oso", SrdLanguage.En, 1).Items.Select(m => m.Slug).Should().Equal("owlbear");
        catalog.Search("kraken", SrdLanguage.En, 1).Items.Should().BeEmpty();
    }

    [Fact]
    public void Exact_matches_come_first_then_prefixes_then_substrings()
    {
        var catalog = new SrdCatalog([
            Create("hobgoblin", "Hobgoblin", "Hobgoblin"),
            Create("goblin-boss", "Goblin Boss", "Jefe goblin"),
            Create("goblin", "Goblin", "Goblin"),
        ]);

        catalog.Search("goblin", SrdLanguage.En, 1).Items.Select(m => m.Slug)
            .Should().Equal("goblin", "goblin-boss", "hobgoblin");
    }

    [Fact]
    public void Ties_are_ordered_by_the_name_in_the_requested_language()
    {
        var catalog = new SrdCatalog([
            Create("wolf", "Wolf", "Lobo"),
            Create("brown-bear", "Brown Bear", "Oso pardo"),
        ]);

        catalog.Search(null, SrdLanguage.En, 1).Items.Select(m => m.Slug).Should().Equal("brown-bear", "wolf");
        catalog.Search(null, SrdLanguage.Es, 1).Items.Select(m => m.Slug).Should().Equal("wolf", "brown-bear");
    }

    [Fact]
    public void Results_come_in_pages_of_twenty()
    {
        var catalog = new SrdCatalog(Enumerable.Range(0, 45).Select(i => Create($"m{i:00}", $"M{i:00}", $"M{i:00}")));

        var third = catalog.Search(string.Empty, SrdLanguage.En, 3);

        third.Total.Should().Be(45);
        third.Page.Should().Be(3);
        third.Items.Select(m => m.Slug).Should().Equal("m40", "m41", "m42", "m43", "m44");
        catalog.Search(string.Empty, SrdLanguage.En, 4).Items.Should().BeEmpty();
    }

    [Fact]
    public void Find_returns_the_monster_or_null()
    {
        var catalog = new SrdCatalog([Create("owlbear", "Owlbear", "Oso lechuza")]);

        catalog.Find("owlbear")!.Name.Es.Should().Be("Oso lechuza");
        catalog.Find("kraken").Should().BeNull();
    }

    [Fact]
    public void Duplicate_slugs_are_rejected()
    {
        var act = () => new SrdCatalog([Create("owlbear", "Owlbear", "Oso lechuza"), Create("owlbear", "Other", "Otro")]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_embedded_catalogue_has_every_srd_monster_in_both_languages()
    {
        var catalog = SrdCatalog.LoadEmbedded();

        catalog.Count.Should().Be(330);
        var all = catalog.Search(null, SrdLanguage.En, 1).Total;
        all.Should().Be(330);
        var goblin = catalog.Find("goblin-warrior")!;
        goblin.Name.Should().Be(new LocalizedText("Goblin Warrior", "Guerrero goblin"));
        (goblin.Ac, goblin.HpAverage, goblin.HpFormula, goblin.InitiativeBonus).Should().Be((15, 10, "3d6", 2));
        goblin.StatBlock.En.Sections.Should().Contain(s => s.Kind == StatBlockSectionKind.Actions);
        goblin.StatBlock.Es.Sections.Should().Contain(s => s.Kind == StatBlockSectionKind.Actions);
        catalog.Search("aboleth", SrdLanguage.Es, 1).Items[0].Slug.Should().Be("aboleth");
    }

    [Fact]
    public void Every_embedded_monster_has_usable_numbers_and_names()
    {
        var catalog = SrdCatalog.LoadEmbedded();
        var monsters = Enumerable.Range(1, 17).SelectMany(page => catalog.Search(null, SrdLanguage.En, page).Items).ToList();

        monsters.Should().HaveCount(330);
        monsters.Should().OnlyContain(m =>
            m.Ac > 0 && m.HpAverage > 0 && m.Name.En.Length > 0 && m.Name.Es.Length > 0
            && m.StatBlock.En.Subtitle.Length > 0 && m.StatBlock.Es.Subtitle.Length > 0
            && m.Abilities.Count == 6);
    }
}
