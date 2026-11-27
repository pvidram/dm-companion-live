using System.Globalization;
using System.Text;

namespace DmCompanionLive.SrdImport;

public static class Slugs
{
    /// <summary>"Will-o'-Wisp" → "will-o-wisp": lower-case ASCII letters and digits joined by single hyphens.</summary>
    public static string From(string englishName)
    {
        var builder = new StringBuilder();
        foreach (var ch in englishName.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark
                && builder.Length > 0
                && builder[builder.Length - 1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
