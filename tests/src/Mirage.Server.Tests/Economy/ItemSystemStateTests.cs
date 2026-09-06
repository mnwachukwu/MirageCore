using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>Stateful inventory flows on <see cref="ItemSystem"/>: currency accumulates into one slot, gear is
/// stamped with max durability, a full bag rejects a give, currency is taken partially then fully, ground
/// pickup is true LIFO (highest DropSeq first), the voluntary-drop cap refuses drops at the limit, and the
/// Sub potion drains one vital and pays a PROPORTIONAL share into each of the other two.</summary>
[TestFixture]
public class ItemSystemStateTests
{
    const int Map = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10, Armor = 11, SubHpPotion = 19;

    static (GameWorld world, PlayerManager pm, ItemSystem items, PlayerRecord p) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var items = new ItemSystem(world, pm, new NoOpDispatcher(), persistence: null!, bg: null!);
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        return (world, pm, items, sp.Char);
    }

    [Test]
    public void GiveItem_Currency_AccumulatesIntoOneSlot()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        items.GiveItem(Idx, Gold, 100);
        items.GiveItem(Idx, Gold, 50);
        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Quantity, Is.EqualTo(150), "currency stacks into a single slot");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(150));
        });
    }

    [Test]
    public void GiveItem_Gear_StampsMaxDurability()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability = 80;
        items.GiveItem(Idx, Sword, 0);
        Assert.That(p.Inv[1].Dur, Is.EqualTo(80), "gear is stamped with its max durability");
    }

    [Test]
    public void GiveItem_FullBag_Rejected()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        for (int i = 1; i <= Constants.MaxInv; i++) p.Inv[i].Num = Sword;
        items.GiveItem(Idx, Armor, 0);
        Assert.That(ItemSystem.CountItem(p, world.Items, Armor), Is.EqualTo(0), "a full bag rejects the item");
    }

    [Test]
    public void TryGiveItem_FullBag_ReturnsFalse()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        for (int i = 1; i <= Constants.MaxInv; i++) p.Inv[i].Num = Sword;
        bool ok = items.TryGiveItem(Idx, Armor, 1);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "a full bag reports failure instead of silently dropping the item");
            Assert.That(ItemSystem.CountItem(p, world.Items, Armor), Is.EqualTo(0));
        });
    }

    [Test]
    public void TryGiveItem_Gear_DurOverride_CarriesWear()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability = 80;
        bool ok = items.TryGiveItem(Idx, Sword, 1, dur: 55);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(p.Inv[1].Dur, Is.EqualTo(55), "a positive dur override wins over the item's max durability");
        });
    }

    [Test]
    public void RemoveFromSlot_Currency_PartialTake_KeepsRemainder()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        p.Inv[3].Num = Gold;
        p.Inv[3].Quantity = 100;
        var (num, val, dur) = items.RemoveFromSlot(Idx, invSlot: 3, amount: 30);
        Assert.Multiple(() =>
        {
            Assert.That((num, val, dur), Is.EqualTo((Gold, 30, 0)), "takes the requested currency amount");
            Assert.That(p.Inv[3].Quantity, Is.EqualTo(70), "the remainder stays in the slot");
        });
    }

    [Test]
    public void RemoveFromSlot_Gear_TakesWholeSlot_CarriesDurability()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        p.Inv[2].Num = Sword;
        p.Inv[2].Dur = 42;
        var (num, _, dur) = items.RemoveFromSlot(Idx, invSlot: 2, amount: 0);
        Assert.Multiple(() =>
        {
            Assert.That(num, Is.EqualTo(Sword));
            Assert.That(dur, Is.EqualTo(42), "the slot's worn durability comes out with it");
            Assert.That(p.Inv[2].Num, Is.EqualTo(0), "the slot is cleared");
        });
    }

    [Test]
    public void RemoveFromSlot_EquippedGear_Refused()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        p.Inv[2].Num = Sword;
        p.WeaponSlot = 2;  // equipped
        var (num, _, _) = items.RemoveFromSlot(Idx, invSlot: 2, amount: 0);
        Assert.Multiple(() =>
        {
            Assert.That(num, Is.EqualTo(0), "equipped gear can't be escrowed");
            Assert.That(p.Inv[2].Num, Is.EqualTo(Sword), "it stays in the bag");
        });
    }

    [Test]
    public void TakeItem_Currency_PartialThenFull()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        items.TakeItem(Idx, Gold, 30);
        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Num, Is.EqualTo(Gold), "a partial take keeps the stack");
            Assert.That(p.Inv[1].Quantity, Is.EqualTo(70));
        });

        items.TakeItem(Idx, Gold, 999);   // >= remaining
        Assert.That(p.Inv[1].Num, Is.EqualTo(0), "taking the rest clears the slot");
    }

    // Two items on one tile: the later drop (higher DropSeq) is the top of the stack and picks up first.
    [Test]
    public void PlayerMapGetItem_IsLifoByDropSeq()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        p.X = 4;
        p.Y = 5;

        items.SpawnItem(Sword, 0, Map, 4, 5);   // DropSeq 1 (bottom)
        items.SpawnItem(Armor, 0, Map, 4, 5);   // DropSeq 2 (top)

        items.PlayerMapGetItem(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Armor), Is.EqualTo(1), "the top item is picked first");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "the bottom item is left behind");
            Assert.That(world.MapItems[Map].Exists(m => m.Num == Sword), Is.True, "the bottom item stays on the ground");
            Assert.That(world.MapItems[Map].Exists(m => m.Num == Armor), Is.False, "the picked item is removed from the map");
        });
    }

    // Two-layer world: pickup is gated to the player's own layer — a ground player can't grab a drop on the
    // bridge above (same tile, different layer), even though it's the newer top-of-stack drop; and vice-versa.
    [Test]
    public void PlayerMapGetItem_OnlyPicksUpItemsOnThePlayersLayer()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        p.X = 4;
        p.Y = 5;

        items.SpawnItem(Armor, 0, Map, 4, 5, layer: WorldLayer.Ground);   // on the ground (older / bottom)
        items.SpawnItem(Sword, 0, Map, 4, 5, layer: WorldLayer.Fringe);   // up on the bridge (newer / would be top)

        // On the ground, only the ground armor is reachable — the fringe sword is skipped despite being on top.
        p.Layer = WorldLayer.Ground;
        items.PlayerMapGetItem(Idx);
        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Armor), Is.EqualTo(1), "the ground drop is picked");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "the fringe drop is out of reach from the ground");
            Assert.That(world.MapItems[Map].Exists(m => m.Num == Sword), Is.True, "the fringe drop stays on the bridge");
        });

        // Step up onto the bridge → the fringe sword is now reachable.
        p.Layer = WorldLayer.Fringe;
        items.PlayerMapGetItem(Idx);
        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(1), "on the fringe, the bridge drop is picked");
    }

    // Two-plane world (§1b): a tile-defined Item authored on the FringeAttr spawns a FRINGE map item; a Ground
    // Item at the same (x,y) spawns its own Ground item — SpawnMapItems scans both planes via LayerLogic.AttrFor.
    [Test]
    public void SpawnMapItems_SpawnsTileItemsOnTheirAuthoredLayer()
    {
        var (world, _, items, _) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;

        world.Maps[Map].EditTile(4, 5, t => t with { Type = TileType.Item, ItemNum = Armor, ItemQuantity = 1, FringeAttr = new FringeAttr { Type = TileType.Item, ItemNum = Sword, ItemQuantity = 1 } });

        items.SpawnMapItems(Map);

        var list = world.MapItems[Map];
        Assert.Multiple(() =>
        {
            Assert.That(list.Exists(m => m.Num == Armor && m.X == 4 && m.Y == 5 && m.Layer == WorldLayer.Ground), Is.True,
                "the ground tile-item spawns on the ground layer");
            Assert.That(list.Exists(m => m.Num == Sword && m.X == 4 && m.Y == 5 && m.Layer == WorldLayer.Fringe), Is.True,
                "the fringe tile-item spawns on the fringe layer");
        });
    }

    // §1b per-layer item respawn: a co-located ground + fringe tile-item respawn INDEPENDENTLY. Arming ONLY the
    // ground tile's timer respawns just the ground item and never duplicates the fringe one — a shared 2D timer
    // would have re-spawned both.
    [Test]
    public void CheckItemRespawn_RespawnsOnlyTheLayerWhoseTimerIsArmed()
    {
        var (world, _, items, _) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        world.Maps[Map].EditTile(4, 5, t => t with { Type = TileType.Item, ItemNum = Armor, ItemQuantity = 1, FringeAttr = new FringeAttr { Type = TileType.Item, ItemNum = Sword, ItemQuantity = 1 } });

        items.SpawnMapItems(Map);
        var list = world.MapItems[Map];
        // Simulate a ground-layer pickup: remove the ground item and arm ONLY its ground respawn timer.
        list.RemoveAll(m => m.Layer == WorldLayer.Ground);
        const long armAt = 1_000;
        world.TempTiles[Map].TakeTileItem(4, 5, WorldLayer.Ground, armAt);

        items.CheckItemRespawn(Map, armAt + Constants.DefaultItemRespawnSeconds * 1000L + 1);

        Assert.Multiple(() =>
        {
            Assert.That(list.FindAll(m => m.Num == Armor && m.Layer == WorldLayer.Ground).Count, Is.EqualTo(1), "the ground item respawns");
            Assert.That(list.FindAll(m => m.Num == Sword && m.Layer == WorldLayer.Fringe).Count, Is.EqualTo(1), "the fringe item is NOT duplicated (its timer was never armed)");
        });
    }

    // The voluntary-drop clutter cap counts only PlayerDropped items; at the cap a further drop is refused.
    [Test]
    public void PlayerMapDropItem_AtCap_Refused()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;   // tradeable (NonTradeable defaults false)
        p.Inv[1].Num = Sword;

        var list = world.MapItems[Map];
        for (int i = 1; i <= Constants.MaxMapItems; i++)
            list.Add(new MapItemRecord { Slot = i, Num = Sword, Source = ItemSource.PlayerDropped });

        items.PlayerMapDropItem(Idx, invSlot: 1, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Num, Is.EqualTo(Sword), "the drop is refused at the cap");
            Assert.That(list.Count, Is.EqualTo(Constants.MaxMapItems), "no new ground item was added");
        });
    }

    // ── DestroyOnDrop enforcement ───────────────────────────────────────────────

    [Test]
    public void PlayerMapDropItem_DestroyOnDrop_Currency_DestroysRequestedAmount_NoGroundDrop()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Gold].DestroyOnDrop = true;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        items.PlayerMapDropItem(Idx, invSlot: 1, amount: 30);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Quantity, Is.EqualTo(70), "a partial amount is destroyed, the remainder kept");
            Assert.That(p.Inv[1].Num, Is.EqualTo(Gold), "the slot survives a partial destroy");
            Assert.That(world.MapItems[Map], Is.Empty, "nothing hits the ground — a DestroyOnDrop item is destroyed");
        });
    }

    [Test]
    public void PlayerMapDropItem_DestroyOnDrop_Currency_WholeStack_ClearsSlot()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Gold].DestroyOnDrop = true;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        items.PlayerMapDropItem(Idx, invSlot: 1, amount: 100);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Num, Is.EqualTo(0), "destroying the whole stack clears the slot");
            Assert.That(world.MapItems[Map], Is.Empty);
        });
    }

    [Test]
    public void PlayerMapDropItem_DestroyOnDrop_NonCurrency_DestroysSlot_NoGroundDrop()
    {
        var (world, _, items, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].DestroyOnDrop = true;
        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 50;

        items.PlayerMapDropItem(Idx, invSlot: 1, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Num, Is.EqualTo(0), "a DestroyOnDrop item is removed from the bag, not dropped");
            Assert.That(world.MapItems[Map], Is.Empty);
        });
    }

    [Test]
    public void PlayerMapDropItemForDeath_DestroyOnDrop_Currency_DestroysPassedAmount()
    {
        var (world, _, items, p) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Gold].DestroyOnDrop = true;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        // The death roller passes a partial amount for currency; the destroy must HONOR it (not zero the stack).
        items.PlayerMapDropItemForDeath(Idx, invSlot: 1, amount: 40);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Quantity, Is.EqualTo(60), "death destroy is partial by the passed amount");
            Assert.That(world.MapItems[Map], Is.Empty, "destroyed, not shed onto the corpse");
        });
    }

    [Test]
    public void UseItem_SubPotion_EqualPools_FeedsHalfTheDrain()
    {
        // The CONTROL case. With every pool the same size, converting through pool fractions gives the
        // same answer the old flat "half the drained amount" rule did — 40 of 100 is 40%, half of that
        // is 20% of each 100-point pool. Kept precisely because it is the one shape where the old rule
        // was right, and it must not move.
        var (world, _, items, p) = Setup();
        world.Items[SubHpPotion].Type = ItemType.PotionSubHp;
        world.Items[SubHpPotion].VitalAmount = 40;
        p.MaxHp = 100;
        p.MaxMp = 100;
        p.MaxSp = 100;
        p.Hp = 100;
        p.Mp = 10;
        p.Sp = 10;
        p.Inv[1].Num = SubHpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(60), "drains VitalAmount (40) from the chosen vital");
            Assert.That(p.Mp, Is.EqualTo(30), "feeds half (20) to each of the other two");
            Assert.That(p.Sp, Is.EqualTo(30));
            Assert.That(p.Inv[1].Num, Is.EqualTo(0), "the potion is consumed");
        });
    }

    [Test]
    public void UseItem_SubPotion_ConvertsThroughPoolFractions_NotRawAmounts()
    {
        // The real shape of the game: SP is a LINEAR pool and far smaller than HP. Draining half the HP
        // bar must buy a quarter of the SP bar, not half the HP NUMBER — which at max level was 1,584
        // stamina poured into a 901-point pool, most of it lost on the clamp.
        var (world, _, items, p) = Setup();
        world.Items[SubHpPotion].Type = ItemType.PotionSubHp;
        world.Items[SubHpPotion].VitalAmount = 600;   // half of a 1,200 HP bar
        p.MaxHp = 1_200;
        p.MaxMp = 1_200;
        p.MaxSp = 200;
        p.Hp = 1_200;
        p.Mp = 0;
        p.Sp = 0;
        p.Inv[1].Num = SubHpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(600), "spent half the HP bar");
            Assert.That(p.Mp, Is.EqualTo(300), "a quarter of the MP bar, which is the same size");
            Assert.That(p.Sp, Is.EqualTo(50), "a quarter of the SMALL SP bar — 50, not 300");
        });
    }

    [Test]
    public void UseItem_SubPotion_SmallPoolIntoLargeOnes_IsWorthProportionallyTheSame()
    {
        // The direction a raw-amount exchange gets wrong: draining the tiny SP pool into two huge ones.
        // Converting through pool fractions makes it proportionally exactly as good a trade as the
        // reverse; paying a raw share instead lands under 1% of an HP bar at max level.
        var (world, _, items, p) = Setup();
        const int SubSpPotion = 20;
        world.Items[SubSpPotion].Type = ItemType.PotionSubSp;
        world.Items[SubSpPotion].VitalAmount = 100;   // half of a 200 SP bar
        p.MaxHp = 1_200;
        p.MaxMp = 1_200;
        p.MaxSp = 200;
        p.Hp = 0;
        p.Mp = 0;
        p.Sp = 200;
        p.Inv[1].Num = SubSpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Sp, Is.EqualTo(100), "spent half the SP bar");
            Assert.That(p.Hp, Is.EqualTo(300), "and bought a quarter of the HP bar — 300, not 50");
            Assert.That(p.Mp, Is.EqualTo(300));
        });
    }

    [Test]
    public void UseItem_SubHpPotion_OverAPlayersWholeBar_TakesAllButOne_AndPaysForThat()
    {
        // A potion sized far above the drinker's pool still works — it just takes everything it can and
        // pays for exactly that. Player death is raised ONLY by the combat damage path, which sets Dead
        // and zero HP together; nothing sweeps for a zero-HP player anywhere else, so a drain that
        // emptied the bar would leave a LIVE character standing at 0 with regen ticking them back up.
        var (world, _, items, p) = Setup();
        world.Items[SubHpPotion].Type = ItemType.PotionSubHp;
        world.Items[SubHpPotion].VitalAmount = 3_000;   // far beyond this character's bar
        p.MaxHp = 900;
        p.MaxMp = 900;
        p.MaxSp = 900;
        p.Hp = 900;
        p.Mp = 0;
        p.Sp = 0;
        p.Inv[1].Num = SubHpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(1), "takes 899 — everything but the reserved point");
            Assert.That(p.Dead, Is.False, "and never kills");
            // 899 of a 900 bar is ~99.9%, so half of that lands just under half of each other bar.
            Assert.That(p.Mp, Is.EqualTo(450).Within(1), "the payout is sized on the 899 actually taken");
            Assert.That(p.Sp, Is.EqualTo(450).Within(1));
            Assert.That(p.Inv[1].Num, Is.Zero, "the potion is consumed");
        });
    }

    [Test]
    public void UseItem_SubPotion_HpReservesItsLastPoint_ButManaAndStaminaDoNot()
    {
        var (world, _, items, p) = Setup();
        world.Items[SubHpPotion].Type = ItemType.PotionSubHp;
        world.Items[SubHpPotion].VitalAmount = 400;
        p.MaxHp = 400;
        p.MaxMp = 400;
        p.MaxSp = 400;
        p.Hp = 1;              // sitting exactly on the floor: nothing left to spend
        p.Mp = 0;
        p.Sp = 0;
        p.Inv[1].Num = SubHpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(1), "at the floor there is nothing to give");
            Assert.That(p.Inv[1].Num, Is.EqualTo(SubHpPotion), "so the potion is refused, not wasted");
            Assert.That(p.Mp, Is.Zero, "and nothing is paid out");
        });

        // Mana has no floor — running dry kills nobody, so it may be emptied outright.
        const int SubMpPotion = 21;
        world.Items[SubMpPotion].Type = ItemType.PotionSubMp;
        world.Items[SubMpPotion].VitalAmount = 400;
        p.Mp = 400;
        p.Inv[2].Num = SubMpPotion;

        items.UseItem(Idx, 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.Mp, Is.Zero, "mana may be spent to nothing");
            Assert.That(p.Inv[2].Num, Is.Zero, "the potion is consumed");
        });
    }

    [Test]
    public void UseItem_SubPotion_RefusedWhenDrainedVitalZero()
    {
        var (world, _, items, p) = Setup();
        world.Items[SubHpPotion].Type = ItemType.PotionSubHp;
        world.Items[SubHpPotion].VitalAmount = 40;
        p.MaxHp = 100;
        p.MaxMp = 100;
        p.MaxSp = 100;
        p.Hp = 0;
        p.Mp = 10;
        p.Sp = 10;
        p.Inv[1].Num = SubHpPotion;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Num, Is.EqualTo(SubHpPotion), "the potion is not consumed");
            Assert.That(p.Mp, Is.EqualTo(10), "no vitals change");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // No-op packet dispatcher (per-file convention). Item ops only fan out to it.
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
