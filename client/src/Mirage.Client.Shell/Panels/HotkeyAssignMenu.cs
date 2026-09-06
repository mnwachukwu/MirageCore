using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The "Assign to hotkey ▸ 1 / 2 / 3 / 4" submenu, built once here and hung off whichever right-click
/// menu is offering it — the inventory's and the spellbook's, which otherwise share nothing.
///
/// <para>Each row names what that slot currently holds, so rebinding is a decision made with the whole
/// bar visible rather than a guess followed by a look. Binding an item or spell that is already on the
/// bar is not blocked: the server simply ends up with it in two places, which is a legitimate thing to
/// want and not worth a rule.</para>
/// </summary>
public static class HotkeyAssignMenu
{
    public static List<ContextMenu.Item> BuildFor(ClientState state, ClientPacketSender sender, HotkeyKind kind, int num)
    {
        var rows = new List<ContextMenu.Item>(Constants.MaxHotkeys);
        var bar = state.Me?.Hotkeys;

        for (int slot = 1; slot <= Constants.MaxHotkeys; slot++)
        {
            int captured = slot;   // the loop variable would otherwise be shared by every closure
            var hk = bar is not null && slot < bar.Length ? bar[slot] : PlayerHotkey.Empty;
            string bound = DescribeBinding(state, hk);
            string label = bound.Length == 0
                ? ClientStrings.Format(ClientStrings.HotkeyBar_AssignSlot, ("Slot", slot))
                : ClientStrings.Format(ClientStrings.HotkeyBar_AssignSlotBound, ("Slot", slot), ("Bound", bound));
            rows.Add(new ContextMenu.Item(label, () => sender.SendSetHotkey(captured, kind, num)));
        }
        return rows;
    }

    /// <summary>What a slot currently holds, for the submenu labels; empty string when it holds nothing.
    /// Names the item or spell rather than saying "item"/"spell", since the point is to tell the player
    /// what they are about to overwrite.</summary>
    private static string DescribeBinding(ClientState state, PlayerHotkey hk)
    {
        if (!hk.IsBound) return "";
        return hk.Kind switch
        {
            HotkeyKind.Item when hk.Num < state.Items.Length => state.Items[hk.Num]?.TrimmedName ?? "",
            HotkeyKind.Spell when hk.Num < state.SpellDefs.Length => state.SpellDefs[hk.Num]?.TrimmedName ?? "",
            _ => "",
        };
    }
}
