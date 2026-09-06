namespace Mirage.Shared;

/// <summary>
/// File and folder naming rules that hold on EVERY platform the game ships to, rather than on the one asking.
///
/// <para>🔴 <see cref="Path.GetInvalidFileNameChars"/> answers for the CURRENT OS, and the two answers are
/// nothing alike: Windows rejects <c>&lt; &gt; : " / \ | ? *</c> plus every control character, while Linux
/// and macOS reject only <c>/</c> and NUL. Code that asks it therefore enforces whatever the authoring
/// machine happened to allow.</para>
///
/// <para>These names are CONTENT. A world folder, an exported map PNG and a per-account config file all
/// travel between machines — cloned, zipped, synced — so a name Linux accepts and Windows refuses is a file
/// that cannot be checked out or unpacked there at all, and the failure lands on someone who never chose the
/// name. The strictest platform's rules are written out here as literals so every platform gives the same
/// answer, and <c>PortableFileNameTests</c> holds production code to using them.</para>
/// </summary>
public static class PortableFileName
{
    /// <summary>Characters no supported platform accepts. The Windows set, which contains the POSIX one.</summary>
    public static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Device names DOS reserved. Windows still refuses them in any casing and at any extension, so
    /// <c>NUL</c>, <c>nul</c> and <c>nul.png</c> are all unusable there while POSIX takes them happily.</summary>
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>True when <paramref name="name"/> is usable as a single file or folder name everywhere. A
    /// bare name only — a value containing a separator is rejected rather than treated as a path.</summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name is "." or "..") return false;
        // Windows silently drops a trailing dot or space, so the name that lands is not the name asked for.
        if (name[^1] is '.' or ' ') return false;
        foreach (char c in name)
        {
            if (c < ' ' || Array.IndexOf(InvalidChars, c) >= 0) return false;
        }
        return !IsReserved(name);
    }

    /// <summary>Whether the stem before the first dot is a reserved device name.</summary>
    public static bool IsReserved(string name)
    {
        int dot = name.IndexOf('.');
        string stem = dot < 0 ? name : name[..dot];
        foreach (string reserved in ReservedNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="name"/> rewritten into something <see cref="IsValid"/> accepts, for the callers that
    /// suggest a name rather than validate one. Every rejected character becomes
    /// <paramref name="replacement"/>, which keeps the length and so keeps word boundaries visible.
    /// </summary>
    public static string Sanitize(string name, char replacement = '_')
    {
        if (string.IsNullOrEmpty(name)) return replacement.ToString();

        var buf = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            buf[i] = c < ' ' || Array.IndexOf(InvalidChars, c) >= 0 ? replacement : c;
        }

        string s = new string(buf).TrimEnd('.', ' ');
        if (s.Length == 0) return replacement.ToString();
        return IsReserved(s) ? s + replacement : s;
    }
}
