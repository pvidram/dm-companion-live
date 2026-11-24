using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

public sealed class RandomDiceRoller : IDiceRoller
{
    public int D20() => Random.Shared.Next(1, 21);
}
