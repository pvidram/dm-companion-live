namespace DmCompanionLive.Domain;

public enum DomainErrorCode
{
    NotAllowed,
    NotYourCharacter,
    SessionEnded,
    SessionNotActive,
    InvalidState,
    CombatantNotFound,
    AlreadyClaimed,
    AlreadyHasCharacter,
    InvalidValue,
    NoCombatants,
}

public sealed class DomainException(DomainErrorCode code, string message) : Exception(message)
{
    public DomainErrorCode Code { get; } = code;
}
