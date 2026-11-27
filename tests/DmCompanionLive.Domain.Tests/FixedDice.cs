using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

/// <summary>Returns the given rolls in order, whatever die is rolled.</summary>
internal sealed class FixedDice(params int[] rolls) : IDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    public int Roll(int sides) => _rolls.Dequeue();
}
