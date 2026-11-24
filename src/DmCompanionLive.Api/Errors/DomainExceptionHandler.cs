using DmCompanionLive.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Errors;

/// <summary>Maps domain errors to ProblemDetails with a stable, localizable "code".</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (CodeFor(exception) is not { } code)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusFor(code);
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = code.ToString(),
                Detail = exception is DomainException
                    ? exception.Message
                    : "Somebody else changed this first. Reload and try again.",
                Status = httpContext.Response.StatusCode,
                Extensions = { ["code"] = code.ToString() },
            },
        });
    }

    /// <summary>Domain errors carry their own code; a lost optimistic-concurrency race is a conflict.</summary>
    public static DomainErrorCode? CodeFor(Exception exception) => exception switch
    {
        DomainException domain => domain.Code,
        DbUpdateConcurrencyException => DomainErrorCode.Conflict,
        _ => null,
    };

    public static int StatusFor(DomainErrorCode code) => code switch
    {
        DomainErrorCode.NotAllowed or DomainErrorCode.NotYourCharacter => StatusCodes.Status403Forbidden,
        DomainErrorCode.CombatantNotFound or DomainErrorCode.CharacterNotFound => StatusCodes.Status404NotFound,
        DomainErrorCode.InvalidValue => StatusCodes.Status400BadRequest,
        DomainErrorCode.SessionExpired => StatusCodes.Status410Gone,
        _ => StatusCodes.Status409Conflict,
    };
}
