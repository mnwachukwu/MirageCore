using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
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
    /// been worn to 0 durability (unequipping is always allowed). A key opens the door the player faces
    /// (across a map seam if need be), consuming itself only when that door's take flag is set.
    ///
    /// <para><b>A consumable does nothing here.</b> It takes its cooldown and no more: what using one means
    /// is a game's rule, and Core has nothing left that could decide it.</para></summary>
    public void UseItem(int index, int invSlot)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidInvSlot(invSlot)) return;
        var sp = _pm[index];
        var p = sp.Char;
        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;

        var item = _world.Items[itemNum];

        bool isEquipment = ItemRecord.IsEquipment(item.Type);
        if (isEquipment)
        {
            long now = Environment.TickCount64;
            if (sp.IsInCombat(now))
            {
                SendMsg(index, ServerStrings.ItemSystem_GearSwapCombat, GameColor.BrightRed);
                return;
            }
        }

        // An item worn to 0 durability BREAKS rather than being destroyed: it stays in the bag, unequipped
        // and unusable, until a repair shop restores it. Only the equip direction is blocked here — taking
        // off an already-worn piece is always allowed. A 0-Durability item carries no durability budget, so
        // it is never "broken".
        if (isEquipment && item.Durability > 0 && p.Inv[invSlot].Dur <= 0 && !p.IsEquipped(invSlot))
        {
            SendMsg(index, ServerStrings.ItemSystem_ItemBroken, GameColor.BrightRed, ("Item", item.TrimmedName));
            return;
        }

        // ── The consumable cooldown ──────────────────────────────────────────────────────────────
        // Its own clock, apart from the action beat, so using something up never costs an action and an
        // action never delays it. Authoritative here because a client-side gate is a courtesy.
        //
        // ONLY consumables: a KEY is deliberately free, because opening a door is a legitimate move and
        // must not be paced. Placed after every guard that rejects a use outright, so a refused use never
        // burns the cooldown.
        bool isConsumable = ItemRecord.IsConsumable(item.Type);
        long useWindMult = _world.WeatherOn(p.Map) == WeatherType.HeavyWind
            ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        long useNow = Environment.TickCount64;
        if (isConsumable && useNow < sp.ConsumableTimer + Constants.ConsumableCooldownMs * useWindMult) return;

        // Set by the potion branches that actually spend the item, so one refused — a full bar, a vital
        // with nothing left to give — costs neither the item nor the beat.
        bool consumed = false;

        switch (item.Type)
        {
            case ItemType.Equipment:
                ToggleWorn(index, p, invSlot, item);
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
                if (key.Type != TileType.Door || key.KeyItemNum != itemNum) break;
                var temp = _world.TempTiles[mapNum];
                // An already-open door must not re-trigger or consume the key (matches the KeyOpen trigger guard).
                if (temp.IsDoorOpen(tx, ty, p.Layer)) break;
                temp.OpenDoor(tx, ty, p.Layer, Environment.TickCount64);
                SendToMap(_world, mapNum, new MapKeyPacket { MapNum = mapNum, X = tx, Y = ty, Open = true, Layer = p.Layer });
                ViewportMsg(index, ServerStrings.Common_DoorUnlocked, GameColor.White);
                // Read off `key` — the attribute resolved on the PLAYER'S layer — not off the tile's inline
                // ground attribute — the flag has to be read on the layer the door being
                // opened was the fringe one, so a fringe door consumed the key only if the unrelated ground
                // attribute happened to say so. Naming the field is what made the mismatch visible.
                if (key.KeyIsConsumed)
                {
                    TakeItem(index, itemNum, 0);
                    SendMsg(index, ServerStrings.ItemSystem_KeyDissolves, GameColor.Yellow);
                }
                break;
        }

        if (consumed) sp.ConsumableTimer = useNow;

        // Wearing something and opening a door are the engine's; everything else is a game's. Raised for
        // every use, so an observer sees the equip and the key too and decides for itself what matters.
        _events.ItemUsed(index, itemNum, invSlot);
    }

    /// <summary>Put the item in this bag slot on, or take it off when it is already worn there.
    ///
    /// <para>One item per slot: whatever was worn in that slot comes off, staying in the bag where it
    /// already is. A piece naming a slot this world does not declare cannot be worn at all — the honest
    /// answer for an item authored against another game, and the only rule Core has about wearing that
    /// a game did not write.</para></summary>
    private void ToggleWorn(int index, PlayerRecord p, int invSlot, ItemRecord item)
    {
        if (!_world.EquipSlots.Has(item.EquipSlot))
        {
            SendMsg(index, ServerStrings.ItemSystem_CannotWear, GameColor.BrightRed,
                ("Item", item.TrimmedName));
            return;
        }

        p.SetEquipped(item.EquipSlot, p.EquippedIn(item.EquipSlot) == invSlot ? 0 : invSlot);
        SendEquippedGear(index);
    }
}
