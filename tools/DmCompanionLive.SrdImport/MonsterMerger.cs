using System.Globalization;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

public static class MonsterMerger
{
    /// <summary>
    /// Joins both sources by English name. Numbers come from the Spanish pack; the English row gives the
    /// English stat block. Any creature missing from one side stops the import.
    /// </summary>
    public static IReadOnlyList<SrdMonster> Merge(
        IReadOnlyList<PackEntry> spanish,
        IReadOnlyList<IReadOnlyDictionary<string, string>> english,
        TextWriter warnings)
    {
        var englishBySlug = english.ToDictionary(r => Slugs.From(r["name"]), StringComparer.Ordinal);
        var spanishSlugs = spanish.Select(e => Slugs.From(e.NameEn)).ToList();
        if (spanishSlugs.Distinct(StringComparer.Ordinal).Count() != spanishSlugs.Count)
        {
            throw new InvalidOperationException("Two Spanish entries share an English name.");
        }

        var withoutEnglish = spanish.Where(e => !englishBySlug.ContainsKey(Slugs.From(e.NameEn))).Select(e => e.NameEn).ToList();
        var withoutSpanish = english.Where(r => !spanishSlugs.Contains(Slugs.From(r["name"]))).Select(r => r["name"]).ToList();
        if (withoutEnglish.Count > 0 || withoutSpanish.Count > 0)
        {
            throw new InvalidOperationException(
                $"The sources do not match. Without English: [{string.Join(", ", withoutEnglish)}]. "
                + $"Without Spanish: [{string.Join(", ", withoutSpanish)}].");
        }

        return spanish
            .Select(entry =>
            {
                var slug = Slugs.From(entry.NameEn);
                var row = englishBySlug[slug];
                var creature = entry.Creature;
                Check(warnings, entry.NameEn, "AC", creature.Ac, row["ac"]);
                Check(warnings, entry.NameEn, "HP", creature.Hp, row["hp_number"]);
                Check(warnings, entry.NameEn, "initiative", creature.InitiativeBonus, row["initiative"]);
                return new SrdMonster(
                    slug,
                    new LocalizedText(row["name"], entry.Name),
                    creature.TypeKey,
                    creature.SizeKeys,
                    creature.Cr,
                    creature.Ac,
                    creature.Hp,
                    creature.HitDice,
                    creature.InitiativeBonus,
                    creature.Modifiers[1],
                    creature.Abilities,
                    new LocalizedStatBlock(EnglishStatBlock.Parse(row), SpanishPack.ToStatBlock(entry)));
            })
            .OrderBy(m => m.Slug, StringComparer.Ordinal)
            .ToList();
    }

    private static void Check(TextWriter warnings, string monster, string field, int spanish, string english)
    {
        if (!int.TryParse(english, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value != spanish)
        {
            warnings.WriteLine($"{monster}: {field} is {spanish} in Spanish and '{english}' in English; using {spanish}.");
        }
    }
}
