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
}
