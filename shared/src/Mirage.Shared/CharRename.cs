namespace Mirage.Shared;

/// <summary>Why a rename was refused, or <see cref="Ok"/>.</summary>
public enum CharRenameResult
{
    Ok = 0,
    BadChars,
    TooShort,
    TooLong,
    NoCharacter,
    Unchanged,
    Online,
    Taken,
}

/// <summary>
/// Whether a character may take a name. Pure, so the whole decision table can be exercised without a server:
/// the only thing left in the handler is looking up whether the name is already spoken for.
///
/// <para>Split in two because the order matters. <see cref="CheckName"/> costs nothing and runs first;
/// <see cref="CheckTarget"/> needs the account read off disk and who is online, and the taken check needs the
/// name registry — so a name that was never valid never reaches any of them.</para>
/// </summary>
public static class CharRename
{
    /// <summary>The name on its own terms: does it read like a name at all.</summary>
    public static CharRenameResult CheckName(string name)
    {
        if (!NameRules.HasValidChars(name)) return CharRenameResult.BadChars;
        return NameRules.CheckLength(name, Constants.MinFieldLength, Constants.NameLength) switch
        {
            NameLengthResult.TooShort => CharRenameResult.TooShort,
            NameLengthResult.TooLong => CharRenameResult.TooLong,
            _ => CharRenameResult.Ok,
        };
    }

    /// <summary>The character it would land on. <paramref name="isOnline"/> refuses the rename outright: the
    /// name is live identity — it is on the map, in party lists, in somebody else's open trade window and in
    /// their chat scrollback — and moving it out from under all that is not worth the one case it serves. An
    /// operator who means it can kick first.</summary>
    public static CharRenameResult CheckTarget(string currentName, string newName, bool isOnline)
    {
        if (currentName.Trim().Length == 0) return CharRenameResult.NoCharacter;
        if (currentName.Trim() == newName.Trim()) return CharRenameResult.Unchanged;
        if (isOnline) return CharRenameResult.Online;
        return CharRenameResult.Ok;
    }

    /// <summary>Whether two names are the same identity — case and underscores ignored, the way the registry
    /// keys them. A character respelling its OWN name collides with itself, so the taken check has to let
    /// that one through.</summary>
    public static bool SameIdentity(string a, string b) => NameRules.Key(a.Trim()) == NameRules.Key(b.Trim());
}
