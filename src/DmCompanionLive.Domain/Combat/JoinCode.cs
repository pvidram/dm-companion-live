using System.Security.Cryptography;

namespace DmCompanionLive.Domain.Combat;

public static class JoinCode
{
    /// <summary>Uppercase letters and digits without the ambiguous 0/O and 1/I.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Normalizes <paramref name="input"/> and tells whether it can be a join code at all.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = (input ?? string.Empty).Trim().ToUpperInvariant();
        return code.Length == Length && code.All(Alphabet.Contains);
    }

    public static string Normalize(string input) => input.Trim().ToUpperInvariant();
}
