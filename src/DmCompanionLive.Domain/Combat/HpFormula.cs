using System.Globalization;
using System.Text.RegularExpressions;

namespace DmCompanionLive.Domain.Combat;

/// <summary>A hit dice formula such as "2d8+2": up to 100 dice of d4–d20 and a modifier of at most ±1000.</summary>
public sealed record HpFormula
{
    public const int MaxDice = 100;
    public const int MaxModifier = 1000;

    private static readonly int[] AllowedSides = [4, 6, 8, 10, 12, 20];
    private static readonly Regex Pattern = new(@"^([0-9]{1,3})d([0-9]{1,2})(?:([+-])([0-9]{1,4}))?\z", RegexOptions.CultureInvariant);

    private HpFormula(int count, int sides, int modifier)
    {
        Count = count;
        Sides = sides;
        Modifier = modifier;
    }

    public int Count { get; }
    public int Sides { get; }
    public int Modifier { get; }

    /// <summary>Accepts spaces, upper-case "D" and the Unicode minus sign used by the SRD.</summary>
    public static HpFormula Parse(string? text)
    {
        var compact = (text ?? string.Empty)
            .Replace('−', '-')
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var match = Pattern.Match(compact);
        if (!match.Success)
        {
            throw Invalid();
        }

        var count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var sides = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var modifier = match.Groups[3].Success
            ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * (match.Groups[3].Value == "-" ? -1 : 1)
            : 0;
        if (count is < 1 or > MaxDice || !AllowedSides.Contains(sides) || Math.Abs(modifier) > MaxModifier)
        {
            throw Invalid();
        }

        return new HpFormula(count, sides, modifier);
    }

    /// <summary>Rolls every die and adds the modifier; never less than 1.</summary>
    public int Roll(IDiceRoller dice)
    {
        var total = Modifier;
        for (var i = 0; i < Count; i++)
        {
            total += dice.Roll(Sides);
        }

        return Math.Max(1, total);
    }

    public override string ToString() => Modifier switch
    {
        0 => $"{Count}d{Sides}",
        > 0 => $"{Count}d{Sides}+{Modifier}",
        _ => $"{Count}d{Sides}{Modifier}",
    };

    private static DomainException Invalid() => new(
        DomainErrorCode.InvalidValue,
        $"Hit dice must look like 2d8+2: 1 to {MaxDice} dice of d4, d6, d8, d10, d12 or d20 and a modifier up to ±{MaxModifier}.");
}
