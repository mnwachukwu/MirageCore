using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary><c>UseItem</c> and the equip/consume behavior behind each item type, including the
/// six potion variants and the vital read/write/broadcast plumbing they share.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Use item ──────────────────────────────────────────────────────────────

    /// <summary>Use the item in an inventory slot, dispatching on its type. Equipment toggles the matching
    /// gear slot — refused in combat, on a class mismatch, below the stat requirement, or when the piece has
    /// been worn to 0 durability (unequipping is always allowed). Potions apply their vital change, a spell
    /// scroll is studied into an open spell slot, and a key opens the door the player faces (across a map seam
    /// if need be), consuming itself only when that door's take flag is set.</summary>
    public void UseItem(int index, int invSlot)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidInvSlot(invSlot)) return;
        var sp = _pm[index];
        var p = sp.Char;
        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;

        var item = _world.Items[itemNum];

        bool isEquipment = item.Type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;
        if (isEquipment)
        {
            long now = Environment.TickCount64;
            if (sp.IsInCombat(now))
            {
                SendMsg(index, ServerStrings.ItemSystem_GearSwapCombat, GameColor.BrightRed);
                return;
            }
        }

        // Class gate (equipment only; empty = anyone). Mirrors the spell-learning gate below — both ask
        // ClassGate, so "may this class use this?" has one answer everywhere.
        if (ItemRecord.IsEquipment(item.Type) && !ClassGate.Allows(item.AllowedClasses, p.Class))
        {
            SendMsg(index, ServerStrings.ItemSystem_WrongClass, GameColor.BrightRed,
                ("Class", ClassGate.Describe(item.AllowedClasses, _world.Classes)));
            return;
        }

        // An item worn to 0 durability BREAKS rather than being destroyed: it stays in the bag, unequipped
        // and unusable, until a repair shop restores it. Only the equip direction is blocked here — taking
        // off an already-worn piece is always allowed. A 0-Durability item carries no durability budget, so
        // it is never "broken" (mirrors CombatSystem.WarnDurability).
        if (isEquipment && item.Durability > 0 && p.Inv[invSlot].Dur <= 0
            && EquippedSlotForType(p, item.Type) != invSlot)
        {
            SendMsg(index, ServerStrings.ItemSystem_ItemBroken, GameColor.BrightRed, ("Item", item.TrimmedName));
            return;
        }

        // Level gate. Sits here, ahead of the type switch, because it is the one requirement that reads
        // the same on a sword, a potion and a scroll — and because it is what actually paces the tier
        // ladder. The stat requirements below cannot: a class's BASE stat is high enough at level 1 that
        // a specialist already meets a mid-ladder piece on the day it is rolled, so those gate WHO may
        // wear a thing while this gates WHEN. Unequipping is never blocked — only reaching for it is.
        if (ItemRecord.UsesLevelReq(item.Type) && item.LevelReq > p.Level
            && !(isEquipment && EquippedSlotForType(p, item.Type) == invSlot))
        {
            SendMsg(index, ServerStrings.ItemSystem_LevelReq, GameColor.BrightRed, ("Level", item.LevelReq));
            return;
        }

        // Player's class record — drives the class-affinity head-start on the equip/learn gates below
        // (a class needs proportionally less of its affinity stat to meet a requirement).
        var cls = _world.Classes[p.Class];
        // ── The drinking cooldown ────────────────────────────────────────────────────────────────
        // Potions run on their own 2s clock, apart from the 1s action beat that attacking and casting
        // share, so a potion never costs a swing and a swing never delays a potion. Heavy Wind doubles
        // it as it doubles the others. Authoritative here because a client-side gate is a courtesy.
        //
        // ONLY potions. Everything else is already guarded or has no business being paced: equipment and
        // scrolls are blocked outright in combat, and a KEY is deliberately free — opening a door
        // mid-fight is a legitimate move and must not cost the swing that follows it.
        //
        // Placed after every guard that rejects a use outright — class, broken, level — so a refused
        // use never burns the cooldown.
        bool isPotion = item.Type is ItemType.PotionAddHp or ItemType.PotionAddMp or ItemType.PotionAddSp
                                  or ItemType.PotionSubHp or ItemType.PotionSubMp or ItemType.PotionSubSp;
        long useWindMult = _world.WeatherOn(p.Map) == WeatherType.HeavyWind
            ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        long useNow = Environment.TickCount64;
        if (isPotion && useNow < sp.PotionTimer + Constants.PotionCooldownMs * useWindMult) return;

        // Set by the potion branches that actually spend the item, so one refused — a full bar, a vital
        // with nothing left to give — costs neither the item nor the beat.
        bool consumed = false;

        switch (item.Type)
        {
            case ItemType.Weapon:
                int weaponStrReq = CombatFormulas.GearStatRequirement(item.Power, cls.Str);
                if (p.WeaponSlot != invSlot && p.Str < weaponStrReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_WeaponStrReq, GameColor.BrightRed, ("Required", weaponStrReq));
                    break;
                }
                p.WeaponSlot = (p.WeaponSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Armor:
                int armorDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.ArmorSlot != invSlot && p.Def < armorDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ArmorDefReq, GameColor.BrightRed, ("Required", armorDefReq));
                    break;
                }
                p.ArmorSlot = (p.ArmorSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Helmet:
                int helmetDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.HelmetSlot != invSlot && p.Def < helmetDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_HelmetDefReq, GameColor.BrightRed, ("Required", helmetDefReq));
                    break;
                }
                p.HelmetSlot = (p.HelmetSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Shield:
                int shieldDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.ShieldSlot != invSlot && p.Def < shieldDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ShieldDefReq, GameColor.BrightRed, ("Required", shieldDefReq));
                    break;
                }
                p.ShieldSlot = (p.ShieldSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.PotionAddHp:
                consumed = ApplyAddPotion(index, p, item, itemNum, PotionVital.Hp);
                break;
            case ItemType.PotionAddMp:
                consumed = ApplyAddPotion(index, p, item, itemNum, PotionVital.Mp);
                break;
            case ItemType.PotionAddSp:
                consumed = ApplyAddPotion(index, p, item, itemNum, PotionVital.Sp);
                break;
            case ItemType.PotionSubHp:
                consumed = ApplySubPotion(index, p, item, itemNum, PotionVital.Hp);
                break;
            case ItemType.PotionSubMp:
                consumed = ApplySubPotion(index, p, item, itemNum, PotionVital.Mp);
                break;
            case ItemType.PotionSubSp:
                consumed = ApplySubPotion(index, p, item, itemNum, PotionVital.Sp);
                break;

            case ItemType.Spell:
                if (sp.IsInCombat(Environment.TickCount64))
                {
                    SendMsg(index, ServerStrings.PacketHandler_StudyCombat, GameColor.BrightRed);
                    break;
                }
                int spellNum = item.SpellNum;
                if (spellNum <= 0 || spellNum > _world.Limits.Spells)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ScrollNoSpell, GameColor.White);
                    break;
                }
                var learnSpell = _world.Spells[spellNum];
                // One definition of "may this character have this spell", shared with the editor's account
                // browser — the message names whichever gate failed.
                var learn = SpellSystem.CanLearn(p, spellNum, learnSpell, cls);
                if (learn != SpellSystem.LearnResult.Ok)
                {
                    switch (learn)
                    {
                        case SpellSystem.LearnResult.WrongClass:
                            SendMsg(index, ServerStrings.ItemSystem_SpellWrongClass, GameColor.White,
                                ("Class", ClassGate.Describe(learnSpell.AllowedClasses, _world.Classes)));
                            break;
                        case SpellSystem.LearnResult.LevelTooLow:
                            SendMsg(index, ServerStrings.ItemSystem_SpellLevelReq, GameColor.White,
                                ("Level", learnSpell.LevelReq));
                            break;
                        case SpellSystem.LearnResult.IntTooLow:
                            SendMsg(index, ServerStrings.ItemSystem_SpellIntReq, GameColor.White,
                                ("Int", CombatFormulas.GetSpellIntRequirement(learnSpell, cls.Int)));
                            break;
                        case SpellSystem.LearnResult.AlreadyKnown:
                            SendMsg(index, ServerStrings.ItemSystem_SpellAlreadyKnown, GameColor.BrightRed);
                            break;
                        default:
                            SendMsg(index, ServerStrings.ItemSystem_SpellBookFull, GameColor.BrightRed);
                            break;
                    }
                    break;
                }
                int spellSlot = SpellSystem.FindOpenSpellSlot(p);
                p.Spell[spellSlot] = spellNum;
                TakeItem(index, itemNum, 0);
                _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = p.Spell[1..], PreparedSpell = p.PreparedSpell });
                SendMsg(index, ServerStrings.ItemSystem_StudyingSpell, GameColor.Yellow);
                SendMsg(index, ServerStrings.ItemSystem_LearnedSpell, GameColor.White);
                break;

            case ItemType.Key:
                // Resolve the faced tile in world coords so a locked door on the neighbor map
                // directly across a seam opens too (mirrors combat/LoS). The player's own map sits
                // at grid [1,1], so local (0,0) maps to world (MapTilesX, MapTilesY).
                var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, p.Map);
                var (dx, dy) = WorldCoordHelper.DirDelta(p.Dir);
                var (px, py) = grid.CenterToWorld(p.X, p.Y);
                int wx = px + dx;
                int wy = py + dy;
                var (mapNum, tx, ty) = grid.ResolveWorldTile(wx, wy);
                if (mapNum <= 0 || mapNum > _world.Limits.Maps) break;
                var map = _world.Maps[mapNum];
                if (map is null) break; // cardinal link to a non-existent map
                var tile = map.Tile[tx, ty];
                // The faced door is read + opened on the player's own layer (a fringe door on the bridge, a ground
                // one beneath). KeyItemNum names the item that opens it; compare against the item being used.
                var key = LayerLogic.AttrFor(tile, p.Layer);
                if (key.Type != TileType.Key || key.KeyItemNum != itemNum) break;
                var temp = _world.TempTiles[mapNum];
                // An already-open door must not re-trigger or consume the key (matches the KeyOpen trigger guard).
                if (temp.IsDoorOpen(tx, ty, p.Layer)) break;
                temp.OpenDoor(tx, ty, p.Layer, Environment.TickCount64);
                SendToMap(_world, mapNum, new MapKeyPacket { MapNum = mapNum, X = tx, Y = ty, Open = true, Layer = p.Layer });
                ViewportMsg(index, ServerStrings.Common_DoorUnlocked, GameColor.White);
                // Read off `key` — the attribute resolved on the PLAYER'S layer — not off the tile's inline
                // ground attribute. As `tile.Data2` it read the ground tile's flag even when the door being
                // opened was the fringe one, so a fringe door consumed the key only if the unrelated ground
                // attribute happened to say so. Naming the field is what made the mismatch visible.
                if (key.KeyIsConsumed)
                {
                    TakeItem(index, itemNum, 0);
                    SendMsg(index, ServerStrings.ItemSystem_KeyDissolves, GameColor.Yellow);
                }
                break;
        }

        if (consumed) sp.PotionTimer = useNow;
    }

    // Shared implementation for the six PotionAdd*/PotionSub* item types: one Add helper, one Sub
    // helper, and per-vital read/write/broadcast plumbing.

    private enum PotionVital { Hp, Mp, Sp }

    /// <summary>PotionAdd{Hp,Mp,Sp}: restore <see cref="ItemRecord.VitalAmount"/> of one vital,
    /// clamped to its max. Refuses (with a chat message) if the vital is already at max.</summary>
    /// <summary>Returns whether the potion was drunk. A refusal costs the player nothing — not the
    /// item, and not the shared beat.</summary>
    private bool ApplyAddPotion(int index, PlayerRecord p, ItemRecord item, int itemNum, PotionVital vital)
    {
        // The same rule the client reads before starting its drinking sweep, so a refused sip looks
        // refused on both sides rather than graying a slot the server would still have served.
        if (!StatFormulas.PotionWouldDoSomething(item.Type, item.VitalAmount,
                                                 p.Hp, p.MaxHp, p.Mp, p.MaxMp, p.Sp, p.MaxSp))
        {
            SendMsg(index, ServerStrings.ItemSystem_VitalFull, GameColor.BrightRed, ("Vital", VitalName(vital)));
            return false;
        }
        SetVital(p, vital, Math.Min(GetVital(p, vital) + item.VitalAmount, GetVitalMax(p, vital)));
        BroadcastVital(index, p, vital);
        TakeItem(index, itemNum, 0);
        SendMsg(index, ServerStrings.ItemSystem_UsedPotion, GameColor.White, ("Item", item.Name));
        return true;
    }

    /// <summary>PotionSub{Hp,Mp,Sp}: drain <see cref="ItemRecord.VitalAmount"/> from one vital and pay a
    /// PROPORTIONAL share into each of the OTHER two — see <see cref="StatFormulas.SubPotionGain"/> for
    /// why the exchange goes through pool fractions rather than raw amounts.
    ///
    /// <para>A SHORT POUR IS ALLOWED and is paid for accordingly: the drain takes whatever the player can
    /// spare, and the payout is sized on what was actually taken. HP additionally reserves a point (see
    /// <see cref="StatFormulas.SubPotionDrain"/>) so a potion can never be lethal; refused outright only
    /// when there is nothing left to spend. All three vitals are broadcast even when only one moved
    /// meaningfully.</para></summary>
    private bool ApplySubPotion(int index, PlayerRecord p, ItemRecord item, int itemNum, PotionVital drainVital)
    {
        int have = GetVital(p, drainVital);
        int drained = StatFormulas.SubPotionDrain(item.VitalAmount, have, drainVital == PotionVital.Hp);
        if (drained <= 0)
        {
            SendMsg(index, ServerStrings.ItemSystem_CantUsePotion, GameColor.BrightRed);
            return false;
        }
        int drainMax = GetVitalMax(p, drainVital);
        SetVital(p, drainVital, have - drained);
        foreach (var v in new[] { PotionVital.Hp, PotionVital.Mp, PotionVital.Sp })
        {
            if (v == drainVital) continue;
            int gain = StatFormulas.SubPotionGain(drained, drainMax, GetVitalMax(p, v));
            SetVital(p, v, Math.Min(GetVital(p, v) + gain, GetVitalMax(p, v)));
        }

        BroadcastVital(index, p, PotionVital.Hp);
        BroadcastVital(index, p, PotionVital.Mp);
        BroadcastVital(index, p, PotionVital.Sp);
        TakeItem(index, itemNum, 0);
        return true;
    }

    private static int GetVital(PlayerRecord p, PotionVital v) => v switch
    {
        PotionVital.Hp => p.Hp,
        PotionVital.Mp => p.Mp,
        _ => p.Sp,
    };

    private static int GetVitalMax(PlayerRecord p, PotionVital v) => v switch
    {
        PotionVital.Hp => p.MaxHp,
        PotionVital.Mp => p.MaxMp,
        _ => p.MaxSp,
    };

    private static void SetVital(PlayerRecord p, PotionVital v, int value)
    {
        switch (v)
        {
            case PotionVital.Hp:
                p.Hp = value;
                break;
            case PotionVital.Mp:
                p.Mp = value;
                break;
            case PotionVital.Sp:
                p.Sp = value;
                break;
        }
    }

    private static string VitalName(PotionVital v) => v switch
    {
        PotionVital.Hp => "HP",
        PotionVital.Mp => "MP",
        _ => "SP",
    };

    private void BroadcastVital(int index, PlayerRecord p, PotionVital v)
    {
        var observers = _world.MapObservers[p.Map];
        switch (v)
        {
            case PotionVital.Hp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendHp(index, p.Hp, p.MaxHp, showFloat: true));
                break;
            case PotionVital.Mp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendMp(index, p.Mp, p.MaxMp, showFloat: true));
                break;
            case PotionVital.Sp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendSp(index, p.Sp, p.MaxSp, showFloat: true));
                break;
        }
    }
}
