namespace Mirage.Shared.Records;

/// <summary>
/// One line of <c>banlist.json</c>. Keyed by account login, because a ban outlives any character on it.
///
/// <para><see cref="BannedAtUtc"/> was added after the first bans were written, so it defaults to 0 and
/// an entry from an older file reads as "date unknown" rather than failing to load. Anything shown to an
/// operator has to tolerate that.</para>
/// </summary>
public sealed record BanEntry
{
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Unix seconds when the ban was applied; 0 for entries written before this was recorded.</summary>
    public long BannedAtUtc { get; init; }
}
