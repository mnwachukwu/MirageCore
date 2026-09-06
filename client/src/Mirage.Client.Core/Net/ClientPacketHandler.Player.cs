using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>The local character: stats, vitals, inventory, equipped gear and spell list.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Player state ──────────────────────────────────────────────────────────

    private void HandleSendInventory(SendInventoryPacket p)
    {
        foreach (var slot in p.Slots)
        {
            if (!SlotValidation.IsValidInvSlot(slot.Slot)) continue;
            var inv = _state.Me.Inv[slot.Slot];
            inv.Num = slot.Num;
            inv.Quantity = slot.Quantity;
            inv.Dur = slot.Dur;
        }
        InventoryChanged?.Invoke();
    }

    private void HandleInventoryUpdate(InventoryUpdatePacket p)
    {
        if (!SlotValidation.IsValidInvSlot(p.Slot)) return;
        var inv = _state.Me.Inv[p.Slot];
        inv.Num = p.Num;
        inv.Quantity = p.Quantity;
        inv.Dur = p.Dur;
        InventoryChanged?.Invoke();
    }

    private void HandleEquippedGear(EquippedGearPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.ArmorSlot = p.Armor;
        player.WeaponSlot = p.Weapon;
        player.HelmetSlot = p.Helmet;
        player.ShieldSlot = p.Shield;
    }

    private void HandlePlayerSpells(PlayerSpellsPacket p)
    {
        // Server sends 0-based (p.Spells[0] = player spell slot 1).
        for (int i = 0; i < p.Spells.Length && i < Constants.MaxPlayerSpells; i++)
            _state.Me.Spell[i + 1] = p.Spells[i];
        _state.Me.PreparedSpell = p.PreparedSpell;
        PreparedSpellReceived?.Invoke(p.PreparedSpell);
    }

    // The action bar, wholesale — sent at join and re-sent after every accepted edit, so the client never
    // has to model "did my change stick". Server sends 0-based (Kinds[0] = slot 1), as with spells.
    private void HandlePlayerHotkeys(PlayerHotkeysPacket p)
    {
        var bar = _state.Me.Hotkeys;
        for (int i = 1; i < bar.Length; i++) bar[i] = PlayerHotkey.Empty;
        int n = Math.Min(p.Kinds.Length, p.Nums.Length);
        for (int i = 0; i < n && i + 1 < bar.Length; i++)
            bar[i + 1] = new PlayerHotkey((HotkeyKind)p.Kinds[i], p.Nums[i]);
    }

    private void HandleSendStats(SendStatsPacket p)
    {
        var me = _state.Me;
        long oldExp = me.Exp;
        int oldLevel = me.Level;
        me.Str = p.Str;
        me.Def = p.Def;
        me.Spd = p.Spd;
        me.Int = p.Int;
        me.Points = p.Points;
        me.Level = p.Level;
        me.Exp = p.Exp;
        if (p.Points > 0) TrainingReady?.Invoke();
        if (p.Level > oldLevel) LevelUp?.Invoke();
        if (p.Exp > oldExp)
            VitalDelta?.Invoke(_state.MyIndex, (int)Math.Min(p.Exp - oldExp, int.MaxValue), VitalType.Exp, false, false, 0);
        VitalsChanged?.Invoke(_state.MyIndex);
    }

    private void HandleSendHp(SendHpPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        int oldHp = player.Hp;
        if (p.Hp == 0)
        {
            if (p.Index == _state.MyIndex)
            {
                _state.SnapVitals = true;
                player.LastCombatMs = 0;
            }
            player.SnapVitals = true;
        }
        player.Hp = p.Hp;
        player.MaxHp = p.MaxHp;
        // Server-authoritative combat stamp converted to our clock — see SendHpPacket.MsSinceCombat.
        // Used by region re-syncs so re-entering observable range doesn't restart a fresh 10s window.
        if (p.MsSinceCombat != int.MaxValue) player.LastCombatMs = Environment.TickCount64 - p.MsSinceCombat;
        VitalsChanged?.Invoke(p.Index);
        if (p.ShowFloat && (p.Damage > 0 || p.Hp != oldHp))
        {
            int delta = p.Damage > 0 ? -p.Damage : (p.Hp - oldHp);
            // Combat is taken only from the server's authoritative MsSinceCombat stamp (above),
            // never inferred from an HP decrease: voluntary non-combat HP loss (a Sub-HP potion
            // drain) would otherwise falsely flip the player into combat.
            VitalDelta?.Invoke(p.Index, delta, VitalType.Hp, false, p.IsCrit, 0);
        }
    }

    private void HandleSendMp(SendMpPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        int oldMp = player.Mp;
        player.Mp = p.Mp;
        player.MaxMp = p.MaxMp;
        VitalsChanged?.Invoke(p.Index);
        if (p.ShowFloat && p.Mp != oldMp)
            VitalDelta?.Invoke(p.Index, p.Mp - oldMp, VitalType.Mp, false, false, 0);
    }

    private void HandleSendSp(SendSpPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        int oldSp = player.Sp;
        player.Sp = p.Sp;
        player.MaxSp = p.MaxSp;
        VitalsChanged?.Invoke(p.Index);
        if (p.ShowFloat && p.Sp != oldSp)
            VitalDelta?.Invoke(p.Index, p.Sp - oldSp, VitalType.Sp, false, false, 0);
    }
}
