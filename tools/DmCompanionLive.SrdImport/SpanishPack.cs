using System.Text.Json;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

public sealed record PackFile(IReadOnlyList<PackEntry> Entries);

public sealed record PackEntry(string Name, string NameEn, PackCreature Creature);

public sealed record PackCreature(
    int Ac,
    int Hp,
    string HitDice,
    int InitiativeBonus,
    IReadOnlyList<int> Abilities,
    IReadOnlyList<int> Modifiers,
    string Cr,
    IReadOnlyList<string> SizeKeys,
    string TypeKey,
    string TypeLine,
    IReadOnlyList<PackField> Fields,
    IReadOnlyList<PackSection> Sections);

public sealed record PackField(string Label, IReadOnlyList<PackRun> Value);

public sealed record PackRun(string Text);

public sealed record PackSection(string Key, IReadOnlyList<PackItem> Items);

public sealed record PackItem(string Name, IReadOnlyList<PackBlock> Blocks);

public sealed record PackBlock(string Kind, IReadOnlyList<PackRun> Text);

/// <summary>Reads the Spanish creature pack of dm-companion (generated from compendio-srd).</summary>
public static class SpanishPack
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // AC, initiative and HP are shown from the numbers, not repeated as text.
    private static readonly HashSet<string> NumericFields = new(StringComparer.Ordinal) { "CA", "Iniciativa", "PG" };

    private static readonly Dictionary<string, StatBlockSectionKind> SectionKinds = new(StringComparer.Ordinal)
    {
        ["rasgos"] = StatBlockSectionKind.Traits,
        ["acciones"] = StatBlockSectionKind.Actions,
        ["adicionales"] = StatBlockSectionKind.BonusActions,
        ["reacciones"] = StatBlockSectionKind.Reactions,
        ["legendarias"] = StatBlockSectionKind.LegendaryActions,
    };

    public static IReadOnlyList<PackEntry> Read(Stream stream) =>
        (JsonSerializer.Deserialize<PackFile>(stream, Options) ?? throw new FormatException("The pack is empty.")).Entries;

    public static StatBlock ToStatBlock(PackEntry entry)
    {
        var creature = entry.Creature;
        var details = creature.Fields
            .Where(f => !NumericFields.Contains(f.Label))
            .Select(f => new StatBlockDetail(f.Label, Join(f.Value)))
            .ToList();
        var sections = creature.Sections
            .Select(s => new StatBlockSection(
                SectionKinds.TryGetValue(s.Key, out var kind)
                    ? kind
                    : throw new FormatException($"{entry.NameEn}: unknown section '{s.Key}'."),
                s.Items.Select(i => new StatBlockEntry(i.Name, string.Join('\n', i.Blocks.Select(Text)))).ToList()))
            .ToList();
        return new StatBlock(creature.TypeLine, details, sections);
    }

    private static string Text(PackBlock block) =>
        block.Kind == "ListItem" ? "• " + Join(block.Text) : Join(block.Text);

    private static string Join(IEnumerable<PackRun> runs) => string.Concat(runs.Select(r => r.Text)).Trim();
}
