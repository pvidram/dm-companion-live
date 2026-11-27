using DmCompanionLive.Domain;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Api.Features.Srd;

/// <summary>Which language SRD content is served in.</summary>
public static class Languages
{
    /// <summary>
    /// An explicit <paramref name="lang"/> must be supported (else <c>InvalidValue</c>); without it the first
    /// supported language of <c>Accept-Language</c> (by quality) wins, then English.
    /// </summary>
    public static SrdLanguage Resolve(string? lang, HttpRequest request)
    {
        if (!string.IsNullOrEmpty(lang))
        {
            return TryParse(lang)
                ?? throw new DomainException(DomainErrorCode.InvalidValue, "Supported languages are 'en' and 'es'.");
        }

        foreach (var value in request.GetTypedHeaders().AcceptLanguage.OrderByDescending(v => v.Quality ?? 1))
        {
            if (TryParse(value.Value.Value) is { } language)
            {
                return language;
            }
        }

        return SrdLanguage.En;
    }

    /// <summary>"es", "ES", "es-ES" → Spanish; "en…" → English; anything else → null.</summary>
    public static SrdLanguage? TryParse(string? tag) => tag?.Split('-')[0].ToLowerInvariant() switch
    {
        "en" => SrdLanguage.En,
        "es" => SrdLanguage.Es,
        _ => null,
    };
}
