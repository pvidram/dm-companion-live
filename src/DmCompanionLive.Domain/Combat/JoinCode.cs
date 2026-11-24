using System.Security.Cryptography;

namespace DmCompanionLive.Domain.Combat;

public static class JoinCode
{
    /// <summary>Uppercase letters and digits without the ambiguous 0/O and 1/I.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static string Normalize(string input) => input.Trim().ToUpperInvariant();
}
