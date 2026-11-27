using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

internal static class MonsterNames
{
    /// <summary>
    /// Names for <paramref name="count"/> new monsters called <paramref name="baseName"/>: the plain name for a
    /// single monster nobody else uses, otherwise "Base N" continuing after the highest N in use (a plain
    /// "Base" counts as 1). Numeric suffixes above <see cref="MaxSuffix"/> are treated as unrelated names, so a
    /// huge suffix can never overflow the numbering. The caller decides which names to pass: see
    /// <see cref="CombatSession.AddMonsters"/> for why hidden combatants are sometimes left out.
    /// </summary>
    public const int MaxSuffix = 1_000_000;

    public static IReadOnlyList<string> Next(string baseName, int count, IEnumerable<string> existing)
    {
        var highest = 0;
        foreach (var name in existing)
        {
            if (name == baseName)
            {
                highest = Math.Max(highest, 1);
            }
            else if (name.StartsWith(baseName + " ", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(baseName.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number <= MaxSuffix)
            {
                highest = Math.Max(highest, number);
            }
        }

        return count == 1 && highest == 0
            ? [baseName]
            : Enumerable.Range(highest + 1, count).Select(n => $"{baseName} {n}").ToList();
    }
}
