namespace Mirage.Shared.Records;

/// <summary>
/// <c>hwbanlist.json</c>: the machines refused entry, and the salt their keys were hashed with.
///
/// <para>The salt lives in the file rather than in <c>serverconfig.json</c> because it is meaningless
/// without these entries and worthless with them — its only job is making a key SERVER-SPECIFIC, so one
/// operator's list cannot be matched against another's to track a player across worlds. Losing the file
/// loses the bans, which is the same event, so there is nothing to keep the salt for separately. It also
/// keeps a value no operator should hand-edit out of the shell's config page.</para>
/// </summary>
public sealed record HardwareBanList
{
    /// <summary>Base64, 32 random bytes, generated the first time a machine is banned.</summary>
    public string Salt { get; init; } = "";

    public List<HardwareBanEntry> Entries { get; init; } = [];
}

/// <summary>
/// One banned machine.
///
/// <para><see cref="Key"/> is the machine's OS identifier, hashed by the client and then AGAIN with this
/// list's salt, so what sits on disk cannot be replayed at another server or compared against one.
/// Nothing here reveals a hardware fact, and only banned machines are ever written down: an ordinary
/// player's key exists in memory for the length of their session and nowhere else.</para>
///
/// <para><see cref="Login"/> is who was signed in when the ban landed. It is what an operator lifts by,
/// since the key is 64 hex characters and means nothing to a human.</para>
/// </summary>
public sealed record HardwareBanEntry
{
    public string Key { get; init; } = "";
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Unix seconds when the ban was applied.</summary>
    public long BannedAtUtc { get; init; }
}
