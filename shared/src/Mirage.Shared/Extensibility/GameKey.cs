namespace Mirage.Shared.Extensibility;

/// <summary>
/// The keys a game may bind, and the one place that says which those are.
///
/// <para><b>Most of the keyboard is already spoken for.</b> WASD moves, Shift runs, F picks up, 1-4 are
/// the action bar, and a dozen letters open one of Core's own windows or edit the line being typed. A
/// game binding one of those would take it away silently — the key would simply stop doing what the
/// player learned it did, with nothing anywhere reporting a conflict.</para>
///
/// <para>So a game chooses from this list and nothing else. A key outside it is refused when it is
/// declared, with the list in the message, rather than accepted and quietly ignored by the client.</para>
///
/// <para><b>Letters only, deliberately.</b> The digits neighbor the action bar, and a verb one key
/// along from a row of item slots is a verb the player will press by accident.</para>
/// </summary>
public static class GameKey
{
    /// <summary>What a game may bind, in the order a reader would scan them.
    ///
    /// <para><b>E is on this list and is the one Core itself uses</b> — it reaches for whatever the
    /// player is facing, opening a shop or starting a conversation. A game that binds E takes that over,
    /// the same way declaring anything else replaces what Core would have done. A game that wants both
    /// keeps E and offers its own verb on the square menu.</para></summary>
    public static readonly IReadOnlyList<string> Offered =
        ["B", "C", "E", "J", "K", "N", "P", "Q", "R", "T", "U", "Y", "Z"];

    /// <summary>Whether a game may bind this. Blank is true: almost everything a game declares wants
    /// no key at all, so it cannot be the answer that fails.</summary>
    public static bool IsOffered(string? key) =>
        string.IsNullOrEmpty(key) || Offered.Contains(key, StringComparer.Ordinal);

    /// <summary>The list as a sentence, for the refusal that names it. A message saying only "that key
    /// is not allowed" leaves the author guessing at a set they cannot see.</summary>
    public static string Listed => string.Join(", ", Offered);
}
