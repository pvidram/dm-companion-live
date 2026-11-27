using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SrdFormulaTests
{
    [Fact]
    public void Every_srd_hit_dice_formula_is_a_valid_normalised_HpFormula()
    {
        var catalog = SrdCatalog.LoadEmbedded();
        var monsters = Enumerable.Range(1, 17).SelectMany(page => catalog.Search(null, SrdLanguage.En, page).Items);

        monsters.Should().OnlyContain(m => HpFormula.Parse(m.HpFormula).ToString() == m.HpFormula);
    }
}
