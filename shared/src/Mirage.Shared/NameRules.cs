namespace Mirage.Shared;

/// <summary>The outcome of <see cref="NameRules.CheckLength"/>.</summary>
public enum NameLengthResult { Ok, TooShort, TooLong }

/// <summary>
/// Shared naming rules for player (character), account, and guild names. Names are letters, digits, and
/// underscores. LENGTH and UNIQUENESS are separate concerns:
/// <list type="bullet">
/// <item>LENGTH — the maximum counts EVERY character (underscores included); the minimum counts only
/// alphanumerics, purely to reject names without enough real characters. So "A__" (one alphanumeric) and an
/// all-underscore name are too short, while the overall length cap still bounds the whole string.</item>
/// <item>UNIQUENESS — case- and underscore-insensitive (<see cref="Key"/>), so "The_Gathering" can't be
/// registered alongside "TheGathering". This never affects the length limit.</item>
/// </list>
/// The three name types share this so the rule is identical everywhere; the server is authoritative and
/// re-checks on every create.
/// </summary>
public static class NameRules
{
    /// <summary>Canonical identity key: lowercased with underscores stripped. Two names with the same key
    /// are the same name for uniqueness + lookup (case- and underscore-insensitive). Identity only — not a
    /// length input.</summary>
    public static string Key(string name) => name.Replace("_", "").ToLowerInvariant();

    /// <summary>The alphanumeric character count — how much real content a name has, used ONLY for the
    /// minimum-length check (the maximum uses the full string length). An all-underscore name is 0.</summary>
    public static int EffectiveLength(string name)
    {
        int n = 0;
        foreach (char c in name) if (char.IsLetterOrDigit(c)) n++;
        return n;
    }

    /// <summary>Only letters, digits, and underscores are permitted in a name.</summary>
    public static bool HasValidChars(string name)
    {
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        return true;
    }

    /// <summary>Length validation: the FULL string length must not exceed <paramref name="maxTotal"/>
    /// (underscores count toward this cap), and the ALPHANUMERIC count must be at least
    /// <paramref name="minAlphanumeric"/> (so "A__" / all-underscore is too short). Uniqueness is separate.</summary>
    public static NameLengthResult CheckLength(string name, int minAlphanumeric, int maxTotal)
    {
        if (name.Length > maxTotal) return NameLengthResult.TooLong;
        if (EffectiveLength(name) < minAlphanumeric) return NameLengthResult.TooShort;
        return NameLengthResult.Ok;
    }
}
