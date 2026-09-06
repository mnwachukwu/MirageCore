namespace Mirage.Shared.Records;

/// <summary>
/// One action-bar slot: what it points at, and which item or spell.
///
/// <para><b>Bound by NUMBER, not by position.</b> An item hotkey holds an item number and a spell hotkey
/// a spell number — never an inventory or spellbook slot index. Those indices move underneath the player
/// constantly (drink the last potion in slot 3 and everything below it shifts; forget a spell and the
/// book compacts), so a position-bound bar would silently start firing the wrong thing. The number is
/// resolved to a live slot at the moment of use, which is exactly what the old potion hotkeys already did
/// when they scanned the bag for the first matching type.</para>
///
/// <para>A struct with <see cref="HotkeyKind.None"/> for "empty" rather than a nullable class, so the bar
/// is always four real slots and no drawing or input path has a null to forget about.</para>
/// </summary>
public readonly record struct PlayerHotkey(HotkeyKind Kind, short Num)
{
    public static readonly PlayerHotkey Empty = new(HotkeyKind.None, 0);

    /// <summary>Whether anything is bound here. A slot whose Num is 0 is treated as empty whatever its
    /// Kind says, so a half-written record can't render as a bound-but-broken icon.</summary>
    public bool IsBound => Kind != HotkeyKind.None && Num > 0;

    /// <summary>A fresh, all-empty bar, 1-based to match <c>Inv</c> and <c>Spell</c> (index 0 unused).</summary>
    public static PlayerHotkey[] NewBar() => new PlayerHotkey[Constants.MaxHotkeys + 1];

    /// <summary>Grow/shrink a loaded bar to the current <see cref="Constants.MaxHotkeys"/>, preserving what
    /// fits. Characters saved before the bar existed (or before it was widened) load with a short or null
    /// array; without this every read site would need its own bounds check.</summary>
    public static PlayerHotkey[] Normalize(PlayerHotkey[]? loaded)
    {
        var bar = NewBar();
        if (loaded is null) return bar;
        int n = Math.Min(loaded.Length, bar.Length);
        for (int i = 1; i < n; i++)
            if (loaded[i].IsBound) bar[i] = loaded[i];
        return bar;
    }
}
