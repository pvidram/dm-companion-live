using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class HpFormulaTests
{
    [Theory]
    [InlineData("2d8+2", 2, 8, 2, "2d8+2")]
    [InlineData("20d10 + 40", 20, 10, 40, "20d10+40")]
    [InlineData("1d4 − 1", 1, 4, -1, "1d4-1")]
    [InlineData("3D6", 3, 6, 0, "3d6")]
    [InlineData("100d20-1000", 100, 20, -1000, "100d20-1000")]
    public void Formulas_are_parsed_and_normalised(string text, int count, int sides, int modifier, string normalised)
    {
        var formula = HpFormula.Parse(text);

        (formula.Count, formula.Sides, formula.Modifier).Should().Be((count, sides, modifier));
        formula.ToString().Should().Be(normalised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("d6")]
    [InlineData("0d6")]
    [InlineData("101d6")]
    [InlineData("2d7")]
    [InlineData("2d6+1001")]
    [InlineData("2d6+")]
    [InlineData("2d6*2")]
    [InlineData("abc")]
    [InlineData("٢d8")]
    [InlineData("2d6\n")]
    public void Anything_else_is_an_invalid_value(string? text)
    {
        var act = () => HpFormula.Parse(text);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void A_roll_adds_every_die_and_the_modifier() =>
        HpFormula.Parse("2d8+2").Roll(new FixedDice(3, 5)).Should().Be(10);

    [Fact]
    public void A_roll_is_never_below_one() =>
        HpFormula.Parse("1d4-3").Roll(new FixedDice(1)).Should().Be(1);
}
