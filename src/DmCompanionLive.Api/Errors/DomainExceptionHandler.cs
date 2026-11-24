using DmCompanionLive.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace DmCompanionLive.Api.Errors;

/// <summary>Maps domain errors to ProblemDetails with a stable, localizable "code".</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusFor(domain.Code);
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = domain.Code.ToString(),
                Detail = domain.Message,
                Status = httpContext.Response.StatusCode,
                Extensions = { ["code"] = domain.Code.ToString() },
            },
        });
    }

    public static int StatusFor(DomainErrorCode code) => code switch
    {
        DomainErrorCode.NotAllowed or DomainErrorCode.NotYourCharacter => StatusCodes.Status403Forbidden,
        DomainErrorCode.CombatantNotFound or DomainErrorCode.CharacterNotFound => StatusCodes.Status404NotFound,
        DomainErrorCode.InvalidValue => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict,
    };
}
