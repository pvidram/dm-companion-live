using System.Text.Json;
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: SrdImport <criaturas.json> <monsters.csv> <output.json>");
    return 1;
}

IReadOnlyList<PackEntry> spanish;
await using (var pack = File.OpenRead(args[0]))
{
    spanish = SpanishPack.Read(pack);
}

IReadOnlyList<IReadOnlyDictionary<string, string>> english;
using (var csv = new StreamReader(args[1]))
{
    english = Csv.Read(csv);
}

var monsters = MonsterMerger.Merge(spanish, english, Console.Error);
await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(monsters, SrdJson.Options) + "\n");
Console.WriteLine($"Wrote {monsters.Count} monsters to {args[2]}.");
return 0;
