using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmCompanionLive.Infrastructure.Srd;

public enum SrdLanguage
{
    En,
    Es,
}

/// <summary>Language-neutral section of a stat block; clients translate the title.</summary>
public enum StatBlockSectionKind
{
    Traits,
    Actions,
    BonusActions,
    Reactions,
    LegendaryActions,
}

public sealed record LocalizedText(string En, string Es)
{
    public string For(SrdLanguage language) => language == SrdLanguage.Es ? Es : En;
}

/// <summary>A labelled line such as "Senses" or "Idiomas", in the stat block's own language.</summary>
public sealed record StatBlockDetail(string Label, string Text);

/// <summary>A trait or action. <see cref="Name"/> is null for untitled text (e.g. the legendary actions intro).</summary>
public sealed record StatBlockEntry(string? Name, string Text);

public sealed record StatBlockSection(StatBlockSectionKind Kind, IReadOnlyList<StatBlockEntry> Entries);

/// <summary>The descriptive part of a stat block in one language, as plain text.</summary>
public sealed record StatBlock(
    string Subtitle,
    IReadOnlyList<StatBlockDetail> Details,
    IReadOnlyList<StatBlockSection> Sections);

public sealed record LocalizedStatBlock(StatBlock En, StatBlock Es)
{
    public StatBlock For(SrdLanguage language) => language == SrdLanguage.Es ? Es : En;
}

/// <summary>
/// An SRD 5.2.1 monster. Numbers are language-neutral; <see cref="Type"/> and <see cref="Sizes"/> are
/// English keys (e.g. "Aberration", "Large") that clients translate.
/// </summary>
public sealed record SrdMonster(
    string Slug,
    LocalizedText Name,
    string Type,
    IReadOnlyList<string> Sizes,
    string Cr,
    int Ac,
    int HpAverage,
    string HpFormula,
    int InitiativeBonus,
    int DexModifier,
    IReadOnlyList<int> Abilities,
    LocalizedStatBlock StatBlock);

/// <summary>The format of <c>srd-monsters.json</c>, shared by the importer and the catalogue.</summary>
public static class SrdJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        // Keep accents readable in the committed file; it is data, never HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
