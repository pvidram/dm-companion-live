namespace DmCompanionLive.Api.Auth;

/// <summary>Claim types and values this API issues.</summary>
public static class AppClaims
{
    public const string Role = "role";
    public const string DmRole = "dm";
    public const string GuestRole = "guest";
    public const string GuestId = "guest_id";
    public const string SessionId = "session_id";
}
