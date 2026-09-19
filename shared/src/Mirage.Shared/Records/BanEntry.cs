namespace Mirage.Shared.Records;

/// <summary>
/// One line of <c>banlist.json</c>. Keyed by account login, because a ban outlives any character on it.
/// </summary>
public sealed record BanEntry
{
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Unix seconds when the ban was applied.</summary>
    public long BannedAtUtc { get; init; }
}
