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

/// <summary>Store trade + item repair. Trade swaps GiveItem*GiveQuantity for GetItem*GetQuantity with the gates
/// (at the shop, a Store, well-formed slot, enough to give, room to receive); FixItem prices durability off
/// the shared repair formula and does a full repair when affordable, a best-effort partial when not, and
/// refuses below one point's cost.</summary>
[TestFixture]
public class ShopSystemTests
{
    const int Map = 1, ShopNum = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10, Potion = 16;

    const int KeeperNpc = 1, KeeperSlot = 1;

    static (GameWorld world, ShopSystem shop, PlayerRecord p) Setup(ShopType type = ShopType.Store)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var shop = new ShopSystem(world, pm, dispatcher, items);

        world.Shops[ShopNum].ShopType = type;
        world.Items[Gold].Type = ItemType.Currency;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        // Shops are keeper-based now: place the shop's keeper NPC on the player's tile and open
        // it, so ActiveShop resolves (in-range + observed + keeper matches) exactly as a real interact would.
        OpenKeeperShop(world, sp, Idx);
        return (world, shop, sp.Char);
    }

    // Wire up the active shop the way OpenNpcShop does: assign the keeper, drop it on the player's tile, mark the
    // player an observer, and record the active-shop context. Every op then re-validates r=5 of this keeper.
    static void OpenKeeperShop(GameWorld world, ServerPlayer sp, int idx)
    {
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var mn = world.MapNpcs[sp.Char.Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = sp.Char.X;
        mn.Y = sp.Char.Y;
        world.MapObservers[sp.Char.Map].Add(idx);
        sp.SetActiveShop(ShopNum, sp.Char.Map, KeeperSlot);
    }

    // ── Buy (the gold storefront) ────────────────────────────────────────────────
    // Sales entries are item NUMBERS priced from ItemRecord.Price, as opposed to Trade's give→get rows.

    static void SetSales(GameWorld world, params int[] itemNums)
    {
        var sales = world.Shops[ShopNum].SalesItem;
        sales.Clear();
        sales.AddRange(itemNums);
    }

    [Test]
    public void Buy_ChargesThePriceAndHandsOverTheItem()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Price = 250;
        SetSales(world, Sword);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 400;

        shop.Buy(Idx, ShopNum, salesSlot: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(150), "the price is taken");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(1), "the item is received");
        });
    }

    [Test]
    public void Buy_WithoutEnoughGold_Refused()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Price = 250;
        SetSales(world, Sword);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Buy(Idx, ShopNum, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100), "nothing is taken");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "nothing is received");
        });
    }

    // ── Buying a stack ───────────────────────────────────────────────────────────
    // Reagents are a currency-type item and a caster burns them by the dozen, so the storefront sells them
    // by the handful. Everything else is one indivisible piece however many the packet asks for.

    const int Reagent = 20;

    static void SetUpReagentStall(GameWorld world, PlayerRecord p, int price, int purse)
    {
        world.Items[Reagent].Type = ItemType.Currency;
        world.Items[Reagent].Price = price;
        SetSales(world, Reagent);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = purse;
    }

    [Test]
    public void Buy_AStack_TakesTheWholeCostAndHandsOverTheWholeAmount()
    {
        var (world, shop, p) = Setup();
        SetUpReagentStall(world, p, price: 3, purse: 100);

        shop.Buy(Idx, ShopNum, salesSlot: 1, quantity: 25);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Reagent), Is.EqualTo(25));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(25), "75 of 100 spent");
        });
    }

    [Test]
    public void Buy_AStack_AddsToOneTheBagAlreadyHolds()
    {
        var (world, shop, p) = Setup();
        SetUpReagentStall(world, p, price: 1, purse: 50);
        p.Inv[2].Num = Reagent;
        p.Inv[2].Quantity = 8;

        shop.Buy(Idx, ShopNum, 1, quantity: 12);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Reagent), Is.EqualTo(20), "stacked, not a second slot");
            Assert.That(p.Inv[3].Num, Is.Zero);
        });
    }

    /// <summary>Clamped to what the purse covers rather than refused, the way a partial repair is. The client
    /// only ever offers what is affordable, so this is the guard against a packet that asked for more.</summary>
    [Test]
    public void Buy_MoreOfAStackThanTheGoldCovers_BuysWhatItCovers()
    {
        var (world, shop, p) = Setup();
        SetUpReagentStall(world, p, price: 5, purse: 32);

        shop.Buy(Idx, ShopNum, 1, quantity: 100);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Reagent), Is.EqualTo(6), "32 gold buys six at five");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(2), "the remainder is left alone");
        });
    }

    [Test]
    public void Buy_AStack_WithoutEnoughForEvenOne_Refused()
    {
        var (world, shop, p) = Setup();
        SetUpReagentStall(world, p, price: 5, purse: 4);

        shop.Buy(Idx, ShopNum, 1, quantity: 10);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Reagent), Is.Zero);
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(4));
        });
    }

    /// <summary>An amount applies to a sword as much as to a stack — it just costs a bag slot per copy
    /// rather than deepening one. Ten swords are ten slots and ten prices; see BulkTradeTests for the
    /// limits that bound it.</summary>
    [Test]
    public void Buy_AnAmountOfSomethingThatDoesNotStack_BuysThatMany()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Price = 100;
        SetSales(world, Sword);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 1000;

        shop.Buy(Idx, ShopNum, 1, quantity: 10);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(10));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.Zero, "charged for ten");
        });
    }

    [TestCase(0)]
    [TestCase(-4)]
    public void Buy_WithNoAmountAsked_BuysOne(int quantity)
    {
        var (world, shop, p) = Setup();
        SetUpReagentStall(world, p, price: 7, purse: 100);

        shop.Buy(Idx, ShopNum, 1, quantity);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Reagent), Is.EqualTo(1));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(93));
        });
    }

    /// <summary>An unpriced entry in a sales list is a data bug. Handing it over free is the same failure
    /// as the zero-quantity trade row that used to mint items, so it is refused rather than given away.</summary>
    [Test]
    public void Buy_UnpricedEntry_IsRefusedRatherThanFree()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Price = 0;
        SetSales(world, Sword);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 400;

        shop.Buy(Idx, ShopNum, 1);

        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "a 0-price entry is not free stock");
    }

    [Test]
    public void Buy_AtAnInn_Refused()
    {
        var (world, shop, p) = Setup(ShopType.Inn);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Price = 10;
        SetSales(world, Sword);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 400;

        shop.Buy(Idx, ShopNum, 1);

        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0));
    }

    // ── Sell ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Sell_PaysTheSellValueAndTakesTheItem()
    {
        var (world, shop, p) = Setup();
        var sword = world.Items[Sword];
        sword.Type = ItemType.Weapon;
        sword.Power = 40;
        sword.LevelReq = 10;
        sword.Durability = 100;
        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 100;   // pristine

        int expected = EconomyFormulas.ItemSellValue(sword, 100);
        shop.Sell(Idx, invSlot: 1, quantity: 0);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "the item leaves the bag");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(expected), "paid at the sell rate");
            Assert.That(expected, Is.GreaterThan(0), "the fixture item should be worth something");
        });
    }

    /// <summary>Condition is priced in: half-worn fetches about half. The exact figure is the formula's,
    /// not restated here — what is pinned is that wear reduces the offer and that a pristine piece is
    /// worth strictly more than a worn one.</summary>
    [Test]
    public void Sell_PaysLessForAWornItem()
    {
        var (world, shop, p) = Setup();
        var sword = world.Items[Sword];
        sword.Type = ItemType.Weapon;
        sword.Power = 40;
        sword.LevelReq = 10;
        sword.Durability = 100;

        int pristine = EconomyFormulas.ItemSellValue(sword, 100);
        int worn = EconomyFormulas.ItemSellValue(sword, 50);
        Assert.That(worn, Is.LessThan(pristine), "condition must move the offer");

        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 50;
        shop.Sell(Idx, 1, 0);

        Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(worn));
    }

    /// <summary>A worthless item still sells. The vendor doubles as the way to empty a bag, and a slot
    /// you cannot clear is worse than one that clears for nothing.</summary>
    [Test]
    public void Sell_WorthlessItem_StillClearsTheSlot()
    {
        var (world, shop, p) = Setup();
        var sword = world.Items[Sword];
        sword.Type = ItemType.Weapon;
        sword.Power = 40;
        sword.Durability = 100;
        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 0;   // broken: the shop buys scrap for nothing

        shop.Sell(Idx, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "the slot clears");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(0), "and pays nothing");
        });
    }

    /// <summary>NonJunkable is what gold, valor and treasure carry. Gold cannot be sold for gold, and
    /// treasure is meant to reach a specific buyer through the barter table rather than a universal one.</summary>
    [Test]
    public void Sell_NonJunkableItem_Refused()
    {
        var (world, shop, p) = Setup();
        var trinket = world.Items[Potion];
        trinket.Type = ItemType.None;
        trinket.Price = 500;
        trinket.NonJunkable = true;
        p.Inv[1].Num = Potion;
        p.Inv[1].Quantity = 1;

        shop.Sell(Idx, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Potion), Is.EqualTo(1), "treasure stays in the bag");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(0), "and pays nothing");
        });
    }

    /// <summary>Selling gear off your own back would silently unequip it, so the player is made to take
    /// it off first — the same rule the bank deposit uses.</summary>
    [Test]
    public void Sell_EquippedItem_Refused()
    {
        var (world, shop, p) = Setup();
        var sword = world.Items[Sword];
        sword.Type = ItemType.Weapon;
        sword.Power = 40;
        sword.Durability = 100;
        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 100;
        p.WeaponSlot = 1;

        shop.Sell(Idx, 1, 0);

        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(1), "equipped gear is not sold out from under you");
    }

    [Test]
    public void Sell_AtAnInn_Refused()
    {
        var (world, shop, p) = Setup(ShopType.Inn);
        var sword = world.Items[Sword];
        sword.Type = ItemType.Weapon;
        sword.Power = 40;
        sword.Durability = 100;
        p.Inv[1].Num = Sword;
        p.Inv[1].Dur = 100;

        shop.Sell(Idx, 1, 0);

        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(1));
    }

    // ── Trade ────────────────────────────────────────────────────────────────────

    // Barters are a dense 0-based list now; slot 1 (what the tests trade against) is BarterItem[0].
    static void SetTrade(GameWorld world, int giveItem, int giveVal, int getItem, int getVal)
    {
        var trades = world.Shops[ShopNum].BarterItem;
        trades.Clear();
        trades.Add(new BarterItemRecord { GiveItem = giveItem, GiveQuantity = giveVal, GetItem = getItem, GetQuantity = getVal });
    }

    [Test]
    public void Trade_Success_SwapsGiveForGet()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Barter(Idx, ShopNum, barterSlot: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(0), "the price is taken");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(1), "the traded item is received");
        });
    }

    [Test]
    public void Trade_NotEnoughToGive_Refused()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 50;  // only 50 of the 100 needed

        shop.Barter(Idx, ShopNum, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(50), "nothing is taken");
            Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "nothing is received");
        });
    }

    [Test]
    public void Trade_AtAnInn_Refused()
    {
        var (world, shop, p) = Setup(ShopType.Inn);   // not a Store
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Barter(Idx, ShopNum, 1);

        Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100), "an Inn is not a trading store");
    }

    // A misconfigured slot (zero quantity) must not mint a free item — the explicit guard.
    [Test]
    public void Trade_ZeroQuantitySlot_Refused()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 0, Sword, 1);   // GiveQuantity 0
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Barter(Idx, ShopNum, 1);

        Assert.That(ItemSystem.CountItem(p, world.Items, Sword), Is.EqualTo(0), "a zero-quantity trade hands out nothing");
    }

    // ── FixItem ──────────────────────────────────────────────────────────────────

    // A mid-band sword: max durability 100 at its tier's medium Power. Repair is now a share of the item's
    // VALUE (EconomyFormulas.RepairCost), so these tests take their expected gold FROM the formula rather
    // than restating its arithmetic — what is under test here is the shop's behavior (does it charge
    // exactly the quoted cost, restore exactly what was bought, refuse when a single point is unaffordable),
    // not the price curve, which EconomyFormulasTests pins separately. A tier-100 piece is used so the
    // numbers are large enough that the partial-repair division is actually exercised.
    const short RepairTier = 100;

    static ItemRecord SwordDef(GameWorld world) => world.Items[Sword];

    static void PlaceRepairableSword(GameWorld world, PlayerRecord p, int currentDur, int gold)
    {
        world.Shops[ShopNum].FixesItems = true;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability = 100;
        world.Items[Sword].LevelReq = RepairTier;
        world.Items[Sword].Power = (short)EconomyFormulas.ReferencePower(RepairTier);
        p.Inv[2].Num = Sword;
        p.Inv[2].Dur = currentDur;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = gold;
    }

    [Test]
    public void FixItem_Affordable_FullyRepairs()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        int cost = EconomyFormulas.RepairCost(60, SwordDef(world));
        int purse = cost * 2;
        p.Inv[1].Quantity = purse;

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[2].Dur, Is.EqualTo(100), "restored to max durability");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(purse - cost),
                "exactly the quoted full-repair cost is deducted");
        });
    }

    // Can't afford a full repair: buy as many points as the purse covers, at the quoted per-point rate.
    [Test]
    public void FixItem_Unaffordable_PartiallyRepairs()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        var def = SwordDef(world);
        int purse = EconomyFormulas.RepairCost(60, def) / 2;   // half of what a full repair costs
        p.Inv[1].Quantity = purse;

        int expectedPoints = EconomyFormulas.RepairPointsAffordable(purse, def);
        int expectedCost = EconomyFormulas.RepairCost(expectedPoints, def);

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(expectedPoints, Is.GreaterThan(0).And.LessThan(60), "the case must actually be partial");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(40 + expectedPoints), "restores the points the gold covers");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(purse - expectedCost),
                "spends what those points cost");
            Assert.That(expectedCost, Is.LessThanOrEqualTo(purse), "a partial repair never costs more than the purse");
        });
    }

    // Below the cost of even one durability point, the repair is refused outright.
    [Test]
    public void FixItem_BelowOnePointCost_Refused()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        int broke = EconomyFormulas.RepairRatePerPoint(SwordDef(world)) - 1;
        Assume.That(broke, Is.GreaterThan(0), "the per-point rate must exceed 1 for this case to exist");
        p.Inv[1].Quantity = broke;

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[2].Dur, Is.EqualTo(40), "no repair");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(broke), "no gold spent");
        });
    }

    [Test]
    public void FixItem_AlreadyPerfect_NoOp()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 100, gold: 100);   // already at max

        shop.FixItem(Idx, invSlot: 2);

        Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100), "a pristine item costs nothing");
    }

    [Test]
    public void FixItem_NonRepairableType_Refused()
    {
        var (world, shop, p) = Setup();
        world.Shops[ShopNum].FixesItems = true;
        world.Items[Potion].Type = ItemType.PotionAddHp;
        p.Inv[2].Num = Potion;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.FixItem(Idx, invSlot: 2);

        Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100), "a potion can't be repaired");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

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
