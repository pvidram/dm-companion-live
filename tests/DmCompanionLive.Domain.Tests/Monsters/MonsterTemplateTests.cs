using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Monsters;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Monsters;

public class MonsterTemplateTests
{
    private static readonly MonsterTemplateData Veteran = new("  Bandido veterano ", 30, 14, 3, "4d8 + 12", " Lleva una ballesta. ");

    [Fact]
    public void A_template_is_created_with_trimmed_text_and_a_normalised_formula()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        template.OwnerId.Should().Be(DmId);
        template.Name.Should().Be("Bandido veterano");
        (template.MaxHp, template.Ac, template.InitiativeBonus).Should().Be((30, 14, 3));
        template.HpFormula.Should().Be("4d8+12");
        template.Notes.Should().Be("Lleva una ballesta.");
    }

    [Fact]
    public void Formula_and_notes_are_optional()
    {
        var template = MonsterTemplate.Create(DmId, new MonsterTemplateData("Lobo", 11, 12, 2, "  ", null), Now);

        template.HpFormula.Should().BeNull();
        template.Notes.Should().BeEmpty();
    }

    // Keyed by a readable name so the theory data stays serializable for xUnit.
    private static readonly Dictionary<string, MonsterTemplateData> InvalidData = new()
    {
        ["blank name"] = Veteran with { Name = " " },
        ["long name"] = Veteran with { Name = new string('a', 81) },
        ["zero hp"] = Veteran with { MaxHp = 0 },
        ["negative ac"] = Veteran with { Ac = -1 },
        ["bonus above 30"] = Veteran with { InitiativeBonus = 31 },
        ["bonus below -30"] = Veteran with { InitiativeBonus = -31 },
        ["bad formula"] = Veteran with { HpFormula = "2d7" },
        ["long notes"] = Veteran with { Notes = new string('a', 2001) },
    };

    public static TheoryData<string> InvalidCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (var key in InvalidData.Keys)
            {
                cases.Add(key);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_data_is_rejected(string invalidCase)
    {
        var act = () => MonsterTemplate.Create(DmId, InvalidData[invalidCase], Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void An_update_replaces_every_field_and_a_rejected_one_changes_nothing()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        template.Update(new MonsterTemplateData("Capitán bandido", 52, 15, 3, null, "Jefe."));
        var act = () => template.Update(Veteran with { MaxHp = 0 });

        act.Should().Throw<DomainException>();
        (template.Name, template.MaxHp, template.HpFormula, template.Notes).Should().Be(("Capitán bandido", 52, (string?)null, "Jefe."));
    }

    [Fact]
    public void A_template_becomes_a_monster_seed_that_remembers_it()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        var seed = template.ToSeed(isHidden: true);

        seed.Should().BeEquivalentTo(new MonsterSeed(
            "Bandido veterano", 30, 14, DexModifier: 3, IsHidden: true, Source: MonsterSource.Template(template.Id),
            InitiativeBonus: 3, HpFormula: HpFormula.Parse("4d8+12")));
    }

    [Fact]
    public void A_favourite_needs_a_slug()
    {
        var act = () => FavoriteMonster.Create(DmId, " ", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        FavoriteMonster.Create(DmId, "owlbear", Now).SrdSlug.Should().Be("owlbear");
    }
}
