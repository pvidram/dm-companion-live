namespace DmCompanionLive.Domain.Combat;

/// <summary>The 15 conditions of the SRD 5.2.1.</summary>
[Flags]
public enum Condition
{
    None = 0,
    Blinded = 1 << 0,
    Charmed = 1 << 1,
    Deafened = 1 << 2,
    Exhaustion = 1 << 3,
    Frightened = 1 << 4,
    Grappled = 1 << 5,
    Incapacitated = 1 << 6,
    Invisible = 1 << 7,
    Paralyzed = 1 << 8,
    Petrified = 1 << 9,
    Poisoned = 1 << 10,
    Prone = 1 << 11,
    Restrained = 1 << 12,
    Stunned = 1 << 13,
    Unconscious = 1 << 14,
}
