using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>The spell list: prepare, cast, forget, and the hotkey bindings that fire them.</summary>
public sealed partial class PacketHandler
{
    //  Spell handlers
    // ===========================================================================

    private void HandleSpellsRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;
        // Spell array is 1-based; send slots 1..MaxPlayerSpells
        _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = p.Spell[1..], PreparedSpell = p.PreparedSpell });
    }

    private void HandleCast(int index, CastPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _spells.CastSpell(index, p.Spell, forceSelf: p.Self);
    }

    // ── Action bar ───────────────────────────────────────────────────────────
    // Slots hold an item/spell NUMBER, so the bar survives the bag and the book being reordered under it
    // (see PlayerHotkey). The server never trusts the incoming binding: a slot may only name something
    // that actually exists, and a spell only one this character has learned.

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
        if (_pm[index].Char.Dead) return;   // a corpse can't rearrange its bar, matching prepared-spell

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

        switch (kind)
        {
            case HotkeyKind.Item:
                // Bound whether or not the bag currently holds one — an empty slot draws grayed rather than
                // unbinding itself, so drinking your last potion doesn't silently clear the key.
                if (p.Num < 1 || p.Num > _world.Limits.Items || string.IsNullOrWhiteSpace(_world.Items[p.Num]?.Name)) return;
                bound = new PlayerHotkey(HotkeyKind.Item, p.Num);
                break;

            case HotkeyKind.Spell:
                if (p.Num < 1 || p.Num > _world.Limits.Spells || _world.Spells[p.Num] is not { } sp
                    || string.IsNullOrWhiteSpace(sp.Name)) return;
                // SubHp belongs to Q and the prepared slot, not to the bar — it is the caster's weapon,
                // swung on the attack beat, and giving it both homes would make "which key casts this"
                // ambiguous. Every other spell type is bar-only. See HandleSetPreparedSpell for the
                // mirror of this rule.
                if (sp.Type == SpellType.SubHp) return;
                // Unlike an item, a spell must be KNOWN to be bound: an item can be re-acquired from any
                // shop or drop, but a spell you have not learned is not a thing you are temporarily out of.
                if (!KnowsSpell(chr, p.Num)) return;
                bound = new PlayerHotkey(HotkeyKind.Spell, p.Num);
                break;
        }

        chr.Hotkeys[p.Slot] = bound;
        _dispatcher.SendTo(index, BuildHotkeysPacket(chr));
    }

    private static bool KnowsSpell(PlayerRecord chr, int spellNum)
    {
        for (int i = 1; i < chr.Spell.Length; i++)
            if (chr.Spell[i] == spellNum) return true;
        return false;
    }
    private void HandleSetPreparedSpell(int index, SetPreparedSpellPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't manage spells
        if (p.Slot < 0 || p.Slot > Constants.MaxPlayerSpells)
        {
            HackingAttempt(index, "Invalid PreparedSpell slot");
            return;
        }
        var chr = _pm[index].Char;
        if (p.Slot > 0 && chr.Spell[p.Slot] <= 0) return;
        // Only a SubHp spell can be prepared — the prepared slot IS the caster's weapon, which is why
        // CombatSystem reads it for the damage estimate and the guild-war caster tier. Every other type
        // lives on the action bar instead; HandleSetHotkey enforces the other half of the split.
        if (p.Slot > 0 && _world.Spells[chr.Spell[p.Slot]]?.Type != SpellType.SubHp) return;
        chr.PreparedSpell = p.Slot;
        // Echo PlayerSpellsPacket: SpellPanel tracks its own slot for UI purposes, so this is the only thing
        // that refreshes the authoritative PreparedSpell on the client's PlayerRecord (read by e.g. the
        // StatsPanel M-DMG breakdown).
        _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = chr.Spell[1..], PreparedSpell = chr.PreparedSpell });
    }

    private void HandleForgetSpell(int index, ForgetSpellPacket p)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (sp.Char.Dead) return;  // a corpse can't manage spells
        if (!SlotValidation.IsValidSpellSlot(p.Slot))
        {
            HackingAttempt(index, "Invalid ForgetSpell slot");
            return;
        }
        var chr = sp.Char;
        if (chr.Spell[p.Slot] <= 0) return;

        if (sp.IsInCombat(Environment.TickCount64))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_StudyCombat, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        var spellRec = _world.Spells[chr.Spell[p.Slot]];
        string spellName = spellRec?.TrimmedName ?? "the spell";

        chr.Spell[p.Slot] = 0;
        // Auto-unprepare if the forgotten slot was the prepared one — otherwise PreparedSpell would
        // dangle and the client UI would briefly show "Prepared" on the now-empty row.
        if (chr.PreparedSpell == p.Slot) chr.PreparedSpell = 0;

        _dispatcher.SendTo(index, new PlayerSpellsPacket
        {
            Spells = chr.Spell[1..],
            PreparedSpell = chr.PreparedSpell
        });
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_ForgotSpell, new ChatMetadata(GameColor.Yellow, ChatChannel.System), ("Spell", spellName));
    }
}
