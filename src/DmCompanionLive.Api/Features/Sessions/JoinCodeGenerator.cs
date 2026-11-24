using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Sessions;

/// <summary>Source of new join codes; replaced in tests to force collisions.</summary>
public interface IJoinCodeGenerator
{
    string New();
}

public sealed class RandomJoinCodeGenerator : IJoinCodeGenerator
{
    public string New() => JoinCode.New();
}
