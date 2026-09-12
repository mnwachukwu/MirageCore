using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>The action bar: what each slot is bound to, and the echo that confirms a rebinding.
///
/// <para>A slot names a record by NUMBER, never by a position in the bag — so using the last of
/// something leaves the key bound and drawn grayed, rather than silently clearing itself.</para></summary>
public sealed partial class PacketHandler
{
    internal static PlayerHotkeysPacket BuildHotkeysPacket(PlayerRecord p)
    {
        var kinds = new byte[Constants.MaxHotkeys];
        var nums = new short[Constants.MaxHotkeys];
        for (int i = 1; i <= Constants.MaxHotkeys && i < p.Hotkeys.Length; i++)
        {
            kinds[i - 1] = (byte)p.Hotkeys[i].Kind;
            nums[i - 1] = p.Hotkeys[i].Num;
        }

        return new PlayerHotkeysPacket { Kinds = kinds, Nums = nums };
    }

    private void HandleSetHotkey(int index, SetHotkeyPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        if (p.Slot < 1 || p.Slot > Constants.MaxHotkeys)
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

        if (kind == HotkeyKind.Item)
        {
            // Bound whether or not the bag currently holds one — an empty slot draws grayed rather than
            // unbinding itself, so using your last of something doesn't silently clear the key.
            if (p.Num < 1 || p.Num > _world.Limits.Items
                || string.IsNullOrWhiteSpace(_world.Items[p.Num]?.Name))
            {
                return;
            }

            bound = new PlayerHotkey(HotkeyKind.Item, p.Num);
        }

        chr.Hotkeys[p.Slot] = bound;
        _dispatcher.SendTo(index, BuildHotkeysPacket(chr));
    }
}
