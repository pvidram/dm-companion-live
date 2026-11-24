using DmCompanionLive.Domain.Users;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Auth;

/// <summary>First sign-in creates the DM; later sign-ins find it by external id.</summary>
public static class DmUsers
{
    public static async Task<DmUser> FindOrCreateAsync(
        AppDbContext db, string externalId, string displayName, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is not null)
        {
            return user;
        }

        user = DmUser.Create(externalId, displayName, now);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Provider names can be missing or longer than we store.</summary>
    public static string DisplayNameFrom(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "DM"
            : trimmed[..Math.Min(trimmed.Length, DmUser.MaxDisplayNameLength)];
    }
}
