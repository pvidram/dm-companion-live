using System.Text;

namespace DmCompanionLive.SrdImport;

/// <summary>Minimal RFC 4180 reader: quoted fields may hold commas, doubled quotes and line breaks.</summary>
public static class Csv
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(TextReader reader)
    {
        var rows = ReadRows(reader).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0];
        return rows.Skip(1).Select(row =>
        {
            if (row.Count != header.Count)
            {
                throw new FormatException($"Expected {header.Count} fields, found {row.Count}.");
            }

            return (IReadOnlyDictionary<string, string>)header.Zip(row).ToDictionary(p => p.First, p => p.Second);
        }).ToList();
    }

    private static IEnumerable<List<string>> ReadRows(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var pending = false;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var ch = (char)next;
            pending = true;
            if (inQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (reader.Peek() == '"')
                {
                    field.Append('"');
                    reader.Read();
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
                pending = false;
            }
            else if (ch != '\r')
            {
                field.Append(ch);
            }
        }

        if (inQuotes)
        {
            throw new FormatException("Unterminated quoted field.");
        }

        if (pending)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
