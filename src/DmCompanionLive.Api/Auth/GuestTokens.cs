using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DmCompanionLive.Api.Auth;

/// <summary>What a guest token proves: this guest, with this name, joined this session.</summary>
public sealed record GuestTicket(Guid SessionId, Guid GuestId, string Name);

/// <summary>Issues and reads guest tokens signed and encrypted with ASP.NET Core Data Protection.</summary>
public sealed class GuestTokens(IDataProtectionProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("DmCompanionLive.GuestToken.v1").ToTimeLimitedDataProtector();

    public string Issue(GuestTicket ticket) =>
        _protector.Protect(JsonSerializer.Serialize(ticket), clock.GetUtcNow().Add(Lifetime));

    /// <summary>The ticket inside <paramref name="token"/>, or null if it is forged, damaged or expired.</summary>
    public GuestTicket? Read(string token)
    {
        try
        {
            var json = _protector.Unprotect(token, out var expiration);
            return expiration < clock.GetUtcNow() ? null : JsonSerializer.Deserialize<GuestTicket>(json);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
