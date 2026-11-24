using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

internal sealed class FixedDice(params int[] rolls) : IDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    public int D20() => _rolls.Dequeue();
}
