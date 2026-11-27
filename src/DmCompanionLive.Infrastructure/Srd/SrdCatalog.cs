using System.Text.Json;

namespace DmCompanionLive.Infrastructure.Srd;

public sealed record SrdSearchPage(IReadOnlyList<SrdMonster> Items, int Page, int Total);

/// <summary>The read-only SRD bestiary.</summary>
public interface ISrdCatalog
{
    int Count { get; }

    SrdMonster? Find(string slug);

    /// <summary>
    /// Matches the query against the English and Spanish names, ignoring case and accents: exact matches
    /// first, then prefixes, then substrings; ties by name in <paramref name="language"/>. An empty query
    /// lists everything. Pages start at 1.
    /// </summary>
    SrdSearchPage Search(string? query, SrdLanguage language, int page);
}

/// <summary>
/// The SRD bestiary held in memory. About 330 read-only entries and a single replica make a database
/// table, a seeder and Postgres search extensions unnecessary; the price is no typo tolerance.
/// </summary>
public sealed class SrdCatalog : ISrdCatalog
{
    public const int PageSize = 20;
    public const string ResourceName = "DmCompanionLive.Infrastructure.Srd.srd-monsters.json";

    private readonly IReadOnlyList<Indexed> _monsters;
    private readonly Dictionary<string, SrdMonster> _bySlug;

    public SrdCatalog(IEnumerable<SrdMonster> monsters)
    {
        _monsters = monsters
            .Select(m => new Indexed(m, SearchText.Normalize(m.Name.En), SearchText.Normalize(m.Name.Es)))
            .ToList();
        _bySlug = _monsters.ToDictionary(i => i.Monster.Slug, i => i.Monster, StringComparer.Ordinal);
    }

    public int Count => _monsters.Count;

    /// <summary>Loads the embedded <c>srd-monsters.json</c>; a missing or empty resource is a startup error.</summary>
    public static SrdCatalog LoadEmbedded()
    {
        using var stream = typeof(SrdCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        var monsters = JsonSerializer.Deserialize<List<SrdMonster>>(stream, SrdJson.Options);
        if (monsters is null || monsters.Count == 0)
        {
            throw new InvalidOperationException("The SRD monster catalogue is empty.");
        }

        return new SrdCatalog(monsters);
    }

    public SrdMonster? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public SrdSearchPage Search(string? query, SrdLanguage language, int page)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var normalized = SearchText.Normalize(query ?? string.Empty);
        var matches = _monsters
            .Select(m => (Entry: m, Rank: Rank(m, normalized)))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank)
            .ThenBy(x => language == SrdLanguage.Es ? x.Entry.Es : x.Entry.En, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Monster.Slug, StringComparer.Ordinal)
            .Select(x => x.Entry.Monster)
            .ToList();
        return new SrdSearchPage(matches.Skip((page - 1) * PageSize).Take(PageSize).ToList(), page, matches.Count);
    }

    /// <summary>0 exact, 1 prefix, 2 substring, null no match — the best over both languages.</summary>
    private static int? Rank(Indexed monster, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        int? best = null;
        foreach (var name in new[] { monster.En, monster.Es })
        {
            int? rank = name == query ? 0
                : name.StartsWith(query, StringComparison.Ordinal) ? 1
                : name.Contains(query, StringComparison.Ordinal) ? 2
                : null;
            if (rank < (best ?? int.MaxValue))
            {
                best = rank;
            }
        }

        return best;
    }

    private sealed record Indexed(SrdMonster Monster, string En, string Es);
}
