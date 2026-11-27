namespace DmCompanionLive.Domain.Combat;

public interface IDiceRoller
{
    /// <summary>A uniformly random number from 1 to <paramref name="sides"/>.</summary>
    int Roll(int sides);

    int D20() => Roll(20);
}
