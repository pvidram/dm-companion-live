using DmCompanionLive.Api.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Auth;

public sealed class GuestTokensTests
{
    // Data Protection checks expiry against the real clock, so the fake clock starts at real "now".
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly IDataProtectionProvider _keys = new EphemeralDataProtectionProvider();

    [Fact]
    public void An_issued_token_reads_back_the_same_ticket()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var ticket = new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana");

        tokens.Read(tokens.Issue(ticket)).Should().Be(ticket);
    }

    [Fact]
    public void Tampered_or_foreign_tokens_are_rejected()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var token = tokens.Issue(new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana"));

        tokens.Read(token[..^4] + "AAAA").Should().BeNull();
        tokens.Read("not-a-token").Should().BeNull();
        new GuestTokens(new EphemeralDataProtectionProvider(), _clock).Read(token).Should().BeNull();
    }

    [Fact]
    public void Tokens_expire_after_their_lifetime()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var token = tokens.Issue(new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana"));

        _clock.Advance(GuestTokens.Lifetime + TimeSpan.FromMinutes(1));

        tokens.Read(token).Should().BeNull();
    }
}
