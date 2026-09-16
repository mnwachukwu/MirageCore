namespace Mirage.Shared.Records;

/// <summary>
/// One action-bar slot: what kind of thing it points at, which declaration, and which one of those.
///
/// <para><b>Bound by NUMBER, not by position.</b> A record hotkey holds a record number — never an
/// inventory or book index. Those indices move underneath the player constantly (drink the last potion
/// in slot 3 and everything below it shifts), so a position-bound bar would silently start firing the
/// wrong thing. The number is resolved at the moment of use.</para>
///
/// <para><b>What the two kinds mean is the GAME's, not Core's.</b> A verb slot invokes a declared
/// action; a record slot invokes the action that record's family named, carrying the number as the
/// subject. Neither says anything about potions or spells, because an engine that knew what a potion
/// was would be an engine with an opinion about what its games are made of.</para>
///
/// <para>A struct with <see cref="HotkeyKind.None"/> for "empty" rather than a nullable class, so the bar
/// is always real slots and no drawing or input path has a null to forget about.</para>
/// </summary>
public readonly record struct PlayerHotkey(HotkeyKind Kind, string Id, short Num)
{
    public static readonly PlayerHotkey Empty = new(HotkeyKind.None, string.Empty, 0);

    /// <summary>Whether anything is bound here. A slot naming no declaration is treated as empty
    /// whatever its Kind says, so a half-written record cannot render as a bound-but-broken icon.</summary>
    public bool IsBound => Kind != HotkeyKind.None && Id.Length > 0;

    /// <summary>A fresh, all-empty bar of <paramref name="slots"/> slots, 1-based to match <c>Inv</c>
    /// (index 0 unused).</summary>
    public static PlayerHotkey[] NewBar(int slots) => Filled(new PlayerHotkey[Math.Max(slots, 0) + 1]);

    /// <summary>Grow or shrink a loaded bar to <paramref name="slots"/>, keeping what fits.
    ///
    /// <para>⚠ How many slots there are is a GAME's decision, and it can change between sessions — a
    /// world that widens its bar, or a character saved under a different game entirely. Without this
    /// every read site would need its own bounds check, and a bar that shrank would throw on the first
    /// draw.</para></summary>
    public static PlayerHotkey[] Normalize(PlayerHotkey[]? loaded, int slots)
    {
        var bar = NewBar(slots);
        if (loaded is null) return bar;

        int n = Math.Min(loaded.Length, bar.Length);
        for (int i = 1; i < n; i++)
            if (loaded[i].IsBound) bar[i] = loaded[i];
        return bar;
    }

    // A default-constructed struct carries a null Id, and every read of one would have to allow for
    // that. Filled once here rather than guarded everywhere.
    private static PlayerHotkey[] Filled(PlayerHotkey[] bar)
    {
        for (int i = 0; i < bar.Length; i++) bar[i] = Empty;
        return bar;
    }
}
