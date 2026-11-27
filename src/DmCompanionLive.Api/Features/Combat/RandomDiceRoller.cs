using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

public sealed class RandomDiceRoller : IDiceRoller
{
    public int Roll(int sides) => Random.Shared.Next(1, sides + 1);
}
