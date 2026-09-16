using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The "Assign to hotkey ▸ 1 / 2 / 3 …" submenu, built once here and hung off whichever right-click menu
/// is offering it — the inventory's, a game panel's list, a verb on the HUD. Those otherwise share
/// nothing, and the plumbing behind the submenu is the same every time.
///
/// <para>Each row names what that slot currently holds, so rebinding is a decision made with the whole bar
/// visible rather than a guess followed by a look. Binding something already on the bar is not blocked:
/// the server simply ends up with it in two places, which is a legitimate thing to want and not worth a
/// rule.</para>
///
/// <para>⚠ Empty when the game declared no bar. A menu offering to assign a hotkey in a world with
/// nowhere to put one is a menu entry that does nothing, so the caller asks <see cref="IsOffered"/> before
/// adding the parent row.</para>
/// </summary>
public static class HotkeyAssignMenu
{
    /// <summary>Whether there is a bar to assign to at all.</summary>
    public static bool IsOffered(ClientState state) => state?.HotkeySlots > 0;

    /// <summary>Slots for binding one of Core's own items.</summary>
    public static List<ContextMenu.Item> ForItem(ClientState state, ClientPacketSender sender, int itemNum)
        => Rows(state, sender, HotkeyKind.Record, CoreRecordFamilies.Items, itemNum);

    /// <summary>Slots for binding one record of a game's own family.</summary>
    public static List<ContextMenu.Item> ForRecord(ClientState state, ClientPacketSender sender,
                                                   string familyId, int num)
        => Rows(state, sender, HotkeyKind.Record, familyId, num);

    /// <summary>Slots for binding a declared verb, optionally carrying a subject number that reaches the
    /// game's handler as the picked id.</summary>
    public static List<ContextMenu.Item> ForVerb(ClientState state, ClientPacketSender sender,
                                                 string actionId, int num = 0)
        => Rows(state, sender, HotkeyKind.Verb, actionId, num);

    private static List<ContextMenu.Item> Rows(ClientState state, ClientPacketSender sender,
                                               HotkeyKind kind, string id, int num)
    {
        ArgumentNullException.ThrowIfNull(state);

        int slots = state.HotkeySlots;
        var rows = new List<ContextMenu.Item>(Math.Max(slots, 0));

        for (int slot = 1; slot <= slots; slot++)
        {
            int captured = slot;   // the loop variable would otherwise be shared by every closure
            string bound = Describe(HotkeyBarPanel.At(state.Hotkeys, slot));
            string label = bound.Length == 0
                ? ClientStrings.Format(ClientStrings.HotkeyBar_AssignSlot, ("Slot", slot))
                : ClientStrings.Format(ClientStrings.HotkeyBar_AssignSlotBound, ("Slot", slot), ("Bound", bound));
            rows.Add(new ContextMenu.Item(label,
                () => sender.SendSetHotkey(captured, kind, id, num)));
        }
        return rows;
    }

    /// <summary>What a slot currently holds, for the submenu labels; empty for one holding nothing. Names
    /// the thing rather than saying "item" or "verb", since the point is to tell the player what they are
    /// about to overwrite — and the caption arrived with the binding, so a game's own records read as
    /// themselves here without this client knowing what they are.</summary>
    private static string Describe(PlayerHotkeysPacket.Slot hk) =>
        HotkeyBarPanel.IsBound(hk) ? ClientStrings.GetOrFallback(hk.Caption, hk.Caption) : "";
}
