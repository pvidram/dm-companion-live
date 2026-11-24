using DmCompanionLive.Domain.Users;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Users;

public class DmUserTests
{
    [Fact]
    public void Creates_a_user_with_a_trimmed_display_name()
    {
        var user = DmUser.Create("google:123", "  Pau ", Now);

        user.Id.Should().NotBeEmpty();
        user.DisplayName.Should().Be("Pau");
        user.ExternalId.Should().Be("google:123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_display_name(string name)
    {
        var act = () => DmUser.Create("dev:x", name, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("google")]
    [InlineData(":123")]
    [InlineData("google:")]
    public void Rejects_an_external_id_that_is_not_provider_colon_subject(string externalId)
    {
        var act = () => DmUser.Create(externalId, "Ana", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Rejects_an_overlong_external_id()
    {
        var act = () => DmUser.Create("google:" + new string('1', DmUser.MaxExternalIdLength), "Ana", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Creation_time_is_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.FromHours(1));

        DmUser.Create("dev:ana", "Ana", local).CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }
}
