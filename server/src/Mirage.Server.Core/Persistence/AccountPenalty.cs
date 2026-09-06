namespace Mirage.Server.Core.Persistence;

/// <summary>
/// One live kick or mute found on an account file, as the persistence layer sees it — no notion of who
/// is online, which is game-thread state the caller adds.
/// </summary>
/// <param name="Login">The account the penalty is on.</param>
/// <param name="Kind">Kick or mute.</param>
/// <param name="ExpiresUtc">Unix seconds it runs until.</param>
public readonly record struct AccountPenalty(string Login, PenaltyKind Kind, long ExpiresUtc);

public enum PenaltyKind
{
    Kick,
    Mute,
}
