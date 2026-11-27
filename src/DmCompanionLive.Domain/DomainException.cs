namespace DmCompanionLive.Domain;

public enum DomainErrorCode
{
    NotAllowed,
    NotYourCharacter,
    SessionEnded,
    SessionNotActive,
    InvalidState,
    CombatantNotFound,
    CharacterNotFound,
    AlreadyClaimed,
    AlreadyHasCharacter,
    InvalidValue,
    NoCombatants,
    SessionExpired,
    Conflict,
    MonsterNotFound,
}

public sealed class DomainException(DomainErrorCode code, string message) : Exception(message)
{
    public DomainErrorCode Code { get; } = code;
}
