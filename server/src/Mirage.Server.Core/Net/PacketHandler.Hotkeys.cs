using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>The action bar: what each slot is bound to, what firing one does, and the echo that confirms
/// a rebinding.
///
/// <para>🔴 <b>Core knows nothing about what is on the bar.</b> A slot holds a verb this game declared or
/// a record of a family it declared, and firing one invokes the verb — the game's own, running where every
/// other rule runs. The engine's only opinion is that a slot names a NUMBER rather than a position, so
/// using the last of something leaves the key bound and drawn grayed rather than silently clearing itself.
/// </para>
///
/// <para><b>The described slot is for the client's benefit alone.</b> A client holds no record schema and
/// no copy of a game's records, so it could not draw a spellbook slot from an id and a number. The server
/// says what to draw; the client draws boxes.</para></summary>
public sealed partial class PacketHandler
{
    internal PlayerHotkeysPacket BuildHotkeysPacket(PlayerRecord p) => HotkeyDescription.Bar(_world, p);

    private GameAction? Declared(string actionId) => _declaredActions.All
        .FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal));

    private RecordFamily? Family(string familyId) => _world.Families.Family(familyId);

    private void HandleSetHotkey(int index, SetHotkeyPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        if (p.Slot < 1 || p.Slot > _world.HotkeyBarSlots)
        {
            HackingAttempt(index, "Invalid hotkey slot");
            return;
        }

        if (!Enum.IsDefined(typeof(HotkeyKind), p.Kind))
        {
            HackingAttempt(index, "Invalid hotkey kind");
            return;
        }

        var chr = _pm[index].Char;
        var kind = (HotkeyKind)p.Kind;
        var bound = PlayerHotkey.Empty;

        // 🔴 Eligibility is asked HERE, not only where the menu was drawn. A client that offered to bind
        // something the game never said was bindable is a client somebody modified, and the alternative
        // is a bar slot a game has no rule for.
        switch (kind)
        {
            case HotkeyKind.Verb when Declared(p.Id) is { Hotkeyable: true }:
                bound = new PlayerHotkey(HotkeyKind.Verb, p.Id, p.Num);
                break;

            // Bound whether or not the player currently holds one — an empty slot draws grayed rather
            // than unbinding itself, so using your last of something doesn't silently clear the key.
            case HotkeyKind.Record when Family(p.Id) is { Hotkeyable: true } && p.Num >= 1:
                bound = new PlayerHotkey(HotkeyKind.Record, p.Id, p.Num);
                break;

            case HotkeyKind.None:
                break;

            default:
                return;   // nothing declared under that id, or not bindable: leave the bar alone
        }

        chr.Hotkeys[p.Slot] = bound;
        _dispatcher.SendTo(index, BuildHotkeysPacket(chr));
    }

    /// <summary>Fire a slot: read what the SERVER has bound there, and invoke what it names.
    ///
    /// <para>The square is the one the player is facing, so a verb on the bar reaches the same place a
    /// verb on a key does.</para></summary>
    private void HandleUseHotkey(int index, UseHotkeyPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        var chr = _pm[index].Char;
        if (p.Slot < 1 || p.Slot > _world.HotkeyBarSlots || p.Slot >= chr.Hotkeys.Length) return;

        var hk = chr.Hotkeys[p.Slot];
        if (!hk.IsBound) return;

        if (hk.Kind == HotkeyKind.Verb)
        {
            // A subject number reaches the handler as the picked id, exactly as a row picked off one of
            // the game's own panels does — so a verb needs no second shape for the bar.
            Invoke(index, hk.Id, p.MapNum, p.X, p.Y, hk.Num > 0 ? hk.Num.ToString(Culture) : "");
            return;
        }

        var family = Family(hk.Id);
        if (family is null || !family.Hotkeyable) return;

        if (family.HotkeyAction.Length > 0)
        {
            Invoke(index, family.HotkeyAction, p.MapNum, p.X, p.Y, hk.Num.ToString(Culture));
            return;
        }

        // A hotkeyable family naming no verb can only be one of Core's own, and Items is the only one:
        // firing it does what using it from the bag does. A game's family is refused at load without an
        // action, so there is no third case to fall through to.
        if (string.Equals(hk.Id, CoreRecordFamilies.Items, StringComparison.Ordinal))
            UseHeldItem(index, hk.Num);
    }

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Use the first of that item number the bag holds.
    ///
    /// <para>Silent when it holds none: the client draws that slot grayed and says so before it ever
    /// sends, because it is the one that can answer without a round trip.</para></summary>
    private void UseHeldItem(int index, int itemNum)
    {
        var chr = _pm[index].Char;
        if (chr.Downed) return;   // a corpse uses nothing, exactly as the bag path says

        for (int slot = 1; slot <= Constants.MaxInv; slot++)
        {
            if (chr.Inv[slot].Num != itemNum) continue;
            _items.UseItem(index, slot);
            return;
        }
    }
}
