namespace Mirage.Shared.Extensibility;

/// <summary>
/// The action bar: how many slots a game gives the player, and nothing else.
///
/// <para>🔴 <b>Opt in, and the engine has no opinion about what goes on it.</b> Declare none and there is
/// no bar — no row of empty boxes in the sidebar, no keys claimed, nothing to explain to a player of a
/// game that has no use for one. A world of conversations and walking wants no action bar, and one that
/// shipped anyway would be chrome nobody asked for.</para>
///
/// <para><b>What a slot holds is a declaration the game already made</b> — a verb it declared, or a
/// record of a family it declared — so the bar adds no vocabulary of its own. An engine that knew what
/// a potion was would be an engine with an opinion about what its games are made of.</para>
/// </summary>
public static class HotkeyBar
{
    /// <summary>No bar at all, which is what a game that says nothing gets.</summary>
    public const int None = 0;

    /// <summary>The most slots a game may ask for.
    ///
    /// <para>⚠ A ceiling rather than a preference: the client draws the bar as one row in the sidebar
    /// strip, and a row wider than that strip is a row that runs off the screen. A game asking for more
    /// is refused by name rather than quietly clamped, so it learns at load rather than from a
    /// screenshot.</para></summary>
    public const int Max = 12;

    /// <summary>The keys that fire the first slots without the mouse: 1 through 9, then 0.
    ///
    /// <para>A game asking for more slots than there are digits gets them — they are reachable by
    /// clicking, and there is no eleventh digit to bind.</para></summary>
    public const int Keyed = 10;

    public static bool IsOffered(int slots) => slots >= None && slots <= Max;
}
