using System.Text;
using System.Text.RegularExpressions;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

/// <summary>
/// Reads the English stat block of a TidyTuesday row. <c>full_text</c> comes from the SRD PDF: lines are
/// wrapped (sometimes mid-word with a hyphen) and each trait or action starts with "Name. ".
/// </summary>
public static class EnglishStatBlock
{
    private static readonly Dictionary<string, StatBlockSectionKind> Headings = new(StringComparer.Ordinal)
    {
        ["Traits"] = StatBlockSectionKind.Traits,
        ["Actions"] = StatBlockSectionKind.Actions,
        ["Bonus Actions"] = StatBlockSectionKind.BonusActions,
        ["Reactions"] = StatBlockSectionKind.Reactions,
        ["Legendary Actions"] = StatBlockSectionKind.LegendaryActions,
    };

    private static readonly (string Column, string Label)[] DetailColumns =
    [
        ("speed", "Speed"),
        ("skills", "Skills"),
        ("resistances", "Resistances"),
        ("vulnerabilities", "Vulnerabilities"),
        ("immunities", "Immunities"),
        ("gear", "Gear"),
        ("senses", "Senses"),
        ("languages", "Languages"),
    ];

    // "Scimitar. Melee Attack Roll: …" — a capitalised name without '.' or ':' followed by more text.
    private static readonly Regex EntryStart = new(
        @"^(?<name>[A-Z][^.:]{0,80})\.\s+(?<rest>\S.*)$", RegexOptions.CultureInvariant);

    public static StatBlock Parse(IReadOnlyDictionary<string, string> row)
    {
        var name = row["name"];
        var lines = row["full_text"]
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var crIndex = lines.FindIndex(l => l.StartsWith("CR ", StringComparison.Ordinal));
        if (lines.Count < 2 || crIndex < 0)
        {
            throw new FormatException($"{name}: the stat block has no CR line.");
        }

        var details = new List<StatBlockDetail>();
        foreach (var (column, label) in DetailColumns)
        {
            var value = row[column].Trim();
            if (value is "" or "NA")
            {
                continue;
            }

            if (column == "speed" && value.StartsWith("Speed ", StringComparison.Ordinal))
            {
                value = value["Speed ".Length..];
            }

            details.Add(new StatBlockDetail(label, value));
        }

        details.Add(new StatBlockDetail("CR", lines[crIndex]["CR ".Length..]));
        return new StatBlock(lines[1], details, ParseSections(lines.Skip(crIndex + 1), name));
    }

    private static List<StatBlockSection> ParseSections(IEnumerable<string> lines, string monster)
    {
        var sections = new List<StatBlockSection>();
        List<StatBlockEntry>? entries = null;
        string? entryName = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (entries is not null && text.Length > 0)
            {
                entries.Add(new StatBlockEntry(entryName, text.ToString()));
            }

            entryName = null;
            text.Clear();
        }

        foreach (var line in lines)
        {
            if (Headings.TryGetValue(line, out var kind))
            {
                Flush();
                entries = [];
                sections.Add(new StatBlockSection(kind, entries));
                continue;
            }

            if (entries is null)
            {
                throw new FormatException($"{monster}: text before the first section: '{line}'.");
            }

            var start = EntryStart.Match(line);
            if (start.Success && (text.Length == 0 || text[text.Length - 1] == '.'))
            {
                Flush();
                entryName = start.Groups["name"].Value;
                text.Append(start.Groups["rest"].Value);
            }
            else
            {
                Append(text, line);
            }
        }

        Flush();
        return sections;
    }

    /// <summary>Joins a wrapped line; "suc-" + "ceed" becomes "succeed".</summary>
    private static void Append(StringBuilder text, string line)
    {
        if (text.Length == 0)
        {
            text.Append(line);
        }
        else if (text[text.Length - 1] == '-' && char.IsLower(line[0]))
        {
            text.Length--;
            text.Append(line);
        }
        else
        {
            text.Append(' ').Append(line);
        }
    }
}
