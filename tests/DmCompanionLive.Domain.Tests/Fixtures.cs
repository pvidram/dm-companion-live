using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

internal static class Fixtures
{
    public static readonly Guid DmId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Actor Dm = new Actor.Dm(DmId);
    public static readonly Actor OtherDm = new Actor.Dm(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);

    public static Actor.Guest Guest(string name = "Lyra") => new(Guid.NewGuid(), name);

    public static CombatSession NewSession(params PcSeed[] characters) =>
        CombatSession.Create(Guid.NewGuid(), DmId, "ABC234", characters, Now);
}
