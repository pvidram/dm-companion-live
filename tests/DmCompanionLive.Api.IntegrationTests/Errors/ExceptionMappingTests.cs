using DmCompanionLive.Api.Errors;
using DmCompanionLive.Domain;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.IntegrationTests.Errors;

public sealed class ExceptionMappingTests
{
    [Fact]
    public void A_lost_concurrency_race_is_a_409_conflict()
    {
        var code = DomainExceptionHandler.CodeFor(new DbUpdateConcurrencyException());

        code.Should().Be(DomainErrorCode.Conflict);
        DomainExceptionHandler.StatusFor(code!.Value).Should().Be(409);
    }

    [Fact]
    public void An_expired_session_is_gone() =>
        DomainExceptionHandler.StatusFor(DomainErrorCode.SessionExpired).Should().Be(410);

    [Fact]
    public void Other_exceptions_are_left_to_the_default_handler() =>
        DomainExceptionHandler.CodeFor(new InvalidOperationException()).Should().BeNull();
}
