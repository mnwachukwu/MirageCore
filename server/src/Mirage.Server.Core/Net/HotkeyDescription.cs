using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Server.Core.World;

namespace Mirage.Server.Core.Net;

/// <summary>
/// The action bar as the client needs it: how wide, and what to draw in each slot.
///
/// <para>🔴 <b>Each bound slot arrives DESCRIBED.</b> A slot may hold a record of a family the client has
/// never heard of — a game's spellbook, its recipes — and a client holds no record schema and no copy of a
/// game's records. So the server says what to draw and what to call it. The alternative is shipping a
/// game's whole catalogue to every client to render four icons.</para>
///
/// <para>Static and reading only the world, because two paths build this: the join batch, and the echo
/// after every rebinding. A second copy is a second place for a slot to be described differently.</para>
/// </summary>
internal static class HotkeyDescription
{
    public static PlayerHotkeysPacket Bar(GameWorld world, PlayerRecord p)
    {
        int slots = world.HotkeyBarSlots;
        var bound = new PlayerHotkeysPacket.Slot[Math.Max(slots, 0)];

        for (int i = 1; i <= slots; i++)
            bound[i - 1] = Slot(world, i < p.Hotkeys.Length ? p.Hotkeys[i] : PlayerHotkey.Empty);

        return new PlayerHotkeysPacket { Slots = slots, Bound = bound };
    }

    private static PlayerHotkeysPacket.Slot Slot(GameWorld world, PlayerHotkey hk)
    {
        if (!hk.IsBound) return new PlayerHotkeysPacket.Slot((byte)HotkeyKind.None, "", 0, "", "", 0);

        if (hk.Kind == HotkeyKind.Verb)
        {
            var verb = world.Actions.All
                .FirstOrDefault(a => string.Equals(a.Id, hk.Id, StringComparison.Ordinal));

            // A verb nothing declares any more still draws: the id and the slot are the player's, and a
            // box that went blank when a game dropped a verb would look like the bar losing bindings.
            return new PlayerHotkeysPacket.Slot(
                (byte)HotkeyKind.Verb, hk.Id, hk.Num, verb?.LabelKey ?? hk.Id, verb?.Icon ?? "", 0);
        }

        bool isItem = string.Equals(hk.Id, CoreRecordFamilies.Items, StringComparison.Ordinal);

        // Core's own items carry art the client already has loaded, and a player recognizes a
        // picture at a glance. A game's records have none, so they wear their family's glyph.
        int sprite = isItem && hk.Num >= 1 && hk.Num <= world.Limits.Items ? world.Items[hk.Num].Pic : 0;

        return new PlayerHotkeysPacket.Slot(
            (byte)HotkeyKind.Record, hk.Id, hk.Num, NameOf(world, hk.Id, hk.Num, isItem),
            world.Families.Family(hk.Id)?.Icon ?? "", sprite);
    }

    /// <summary>What to call the record in that slot: the item's name for Core's own family, and for a
    /// game's the field its family named as the name. Empty for a number nothing was authored into,
    /// which draws as a bound slot with no caption rather than as an empty one.</summary>
    private static string NameOf(GameWorld world, string familyId, int num, bool isItem)
    {
        if (isItem)
            return num >= 1 && num <= world.Limits.Items ? world.Items[num].TrimmedName : string.Empty;

        var record = world.ModuleRecords.Get(familyId, num);
        if (record is null) return string.Empty;

        string key = world.Families.Family(familyId)?.NameFieldKey ?? "name";
        return key.Length > 0 && record.TryGet(key, out AttributeValue named) ? named.AsText() : string.Empty;
    }
}
