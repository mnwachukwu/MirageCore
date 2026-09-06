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

/// <summary>
/// Buying, selling and bartering more than one at a time.
///
/// <para>All of it rests on one distinction the inventory draws: a CURRENCY carries its amount inside a
/// slot, and everything else spends a slot per copy. Counting, room, taking and giving each have to know
/// which they are dealing with, and every bug this fixture exists to catch was one of them quietly
/// assuming the first.</para>
///
/// <para>The two limits are deliberately asymmetric and are pinned as such: gold CLAMPS, because buying as
/// many as the purse covers is the useful answer and nobody is charged for what they did not receive;
/// room REFUSES, because taking payment for twenty and handing over eight is the outcome a purchase must
/// never have.</para>
/// </summary>
[TestFixture]
public class BulkTradeTests
{
    const int Map = 1, ShopNum = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Gem = 20, Hat = 21, Tooth = 22, Blade = 23;
    const int KeeperNpc = 1, KeeperSlot = 1;

    static (GameWorld World, ShopSystem Shop, ItemSystem Items, PlayerRecord P) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var shop = new ShopSystem(world, pm, dispatcher, items);

        world.Shops[ShopNum].ShopType = ShopType.Store;
        world.Items[Gold].Type = ItemType.Currency;
        // Treasure-shaped: a slot apiece, which is what makes counting and room interesting.
        foreach (int n in new[] { Gem, Hat, Tooth }) world.Items[n].Type = ItemType.None;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var mn = world.MapNpcs[Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = sp.Char.X;
        mn.Y = sp.Char.Y;
        world.MapObservers[Map].Add(Idx);
        sp.SetActiveShop(ShopNum, Map, KeeperSlot);
        return (world, shop, items, sp.Char);
    }

    /// <summary>Fill every bag slot but <paramref name="leaveFree"/>, so a room limit can be aimed exactly.</summary>
    static void FillBagExcept(GameWorld world, PlayerRecord p, int leaveFree)
    {
        const int Filler = 30;
        world.Items[Filler].Type = ItemType.None;
        int free = Constants.MaxInv;
        for (int i = 1; i <= Constants.MaxInv && free > leaveFree; i++)
        {
            if (p.Inv[i].Num != 0) { free--; continue; }
            p.Inv[i].Num = Filler;
            p.Inv[i].Quantity = 1;
            free--;
        }
    }

    static void GiveSlots(PlayerRecord p, int itemNum, int count, int dur = 0)
    {
        for (int i = 1, placed = 0; i <= Constants.MaxInv && placed < count; i++)
        {
            if (p.Inv[i].Num != 0) continue;
            p.Inv[i].Num = itemNum;
            p.Inv[i].Quantity = 1;
            p.Inv[i].Dur = dur;
            placed++;
        }
    }

    // ── Counting ─────────────────────────────────────────────────────────────

    /// <summary>The bug the rest of this rests on: the old reading stopped at the first matching slot and
    /// answered 1, so every "do you have enough" test against a slot-per-copy item compared against 1.</summary>
    [Test]
    public void CountItem_CountsEverySlot_NotJustTheFirst()
    {
        var (world, _, _, p) = Setup();
        GiveSlots(p, Tooth, 5);

        Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.EqualTo(5));
    }

    [Test]
    public void CountItem_SumsACurrencyStack()
    {
        var (world, _, _, p) = Setup();
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 400;

        Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(400));
    }

    [Test]
    public void HasItem_IsTheSameQuestionAskedForAYes()
    {
        var (world, _, _, p) = Setup();
        GiveSlots(p, Tooth, 2);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.HasItem(p, world.Items, Tooth), Is.True);
            Assert.That(ItemSystem.HasItem(p, world.Items, Gem), Is.False);
        });
    }

    // ── Giving and taking by the handful ─────────────────────────────────────

    /// <summary>Three gems are three SLOTS. Placed as a quantity on one slot they would read as a stack,
    /// which nothing outside a currency understands.</summary>
    [Test]
    public void GiveItems_SpendsASlotPerCopy()
    {
        var (world, _, items, p) = Setup();

        int placed = items.GiveItems(Idx, Gem, 3);

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.EqualTo(3));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gem), Is.EqualTo(3));
            Assert.That(SlotsHolding(p, Gem), Is.EqualTo(3), "three slots, not one slot of three");
        });
    }

    [Test]
    public void GiveItems_StopsWhenTheBagFills_AndSaysHowManyLanded()
    {
        var (world, _, items, p) = Setup();
        FillBagExcept(world, p, leaveFree: 2);

        int placed = items.GiveItems(Idx, Gem, 5);

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.EqualTo(2));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gem), Is.EqualTo(2));
        });
    }

    [Test]
    public void TakeItems_ClearsThatManySlots()
    {
        var (world, _, items, p) = Setup();
        GiveSlots(p, Tooth, 5);

        items.TakeItems(Idx, Tooth, 4);

        Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.EqualTo(1), "one left over");
    }

    // ── Barter: a row is a RATE ──────────────────────────────────────────────

    static void SetBarterRow(GameWorld world, int give, int giveQty, int get, int getQty)
    {
        var rows = world.Shops[ShopNum].BarterItem;
        rows.Clear();
        rows.Add(new BarterItemRecord { GiveItem = give, GiveQuantity = giveQty, GetItem = get, GetQuantity = getQty });
    }

    /// <summary>The case that could not run at all before: a row asking for two of a slot-per-copy item was
    /// refused however many the player held, because the count answered 1.</summary>
    [Test]
    public void Barter_TwoForOne_RunsAtAll()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 2, Gold, 5);
        GiveSlots(p, Tooth, 2);

        shop.Barter(Idx, ShopNum, barterSlot: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.Zero, "both teeth are spent");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(5));
        });
    }

    /// <summary>Five teeth against a two-teeth row buys two helpings and leaves one behind — the remainder
    /// stays in the bag rather than being rounded into the trade.</summary>
    [Test]
    public void Barter_AppliesTheRowSeveralTimes_AndLeavesTheRemainder()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 2, Gold, 5);
        GiveSlots(p, Tooth, 5);

        shop.Barter(Idx, ShopNum, barterSlot: 1, multiples: 2);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.EqualTo(1), "four spent, one left");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(10));
        });
    }

    [Test]
    public void Barter_MoreHelpingsThanTheBagCanPayFor_Refused()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 2, Gold, 5);
        GiveSlots(p, Tooth, 3);   // one helping's worth, with a spare

        shop.Barter(Idx, ShopNum, barterSlot: 1, multiples: 2);

        Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.EqualTo(3), "nothing was taken");
    }

    /// <summary>A payout that will not fit EVEN AFTER the payment leaves buys nothing. Five slots free plus
    /// the three the teeth vacate is eight, and nine hats do not go into eight — so nothing moves at all,
    /// rather than nine being paid for and eight delivered.</summary>
    [Test]
    public void Barter_PayoutThatWouldNotFit_RefusedRatherThanTrimmed()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 1, Hat, 3);
        GiveSlots(p, Tooth, 3);
        FillBagExcept(world, p, leaveFree: 5);

        shop.Barter(Idx, ShopNum, barterSlot: 1, multiples: 3);   // nine hats into eight slots

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Hat), Is.Zero, "no hats");
            Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.EqualTo(3), "and no teeth spent");
        });
    }

    /// <summary>The payment is part of the room. Three teeth for nine hats with eight slots free DOES go
    /// through, because handing the teeth over leaves eleven — the player gets all nine they asked for,
    /// which is the thing the refusal exists to protect.</summary>
    [Test]
    public void Barter_PayoutThatFitsOnlyBecauseThePaymentLeft_GoesThrough()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 1, Hat, 3);
        GiveSlots(p, Tooth, 3);
        FillBagExcept(world, p, leaveFree: 8);

        shop.Barter(Idx, ShopNum, barterSlot: 1, multiples: 3);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Hat), Is.EqualTo(9), "all nine asked for");
            Assert.That(ItemSystem.CountItem(p, world.Items, Tooth), Is.Zero);
        });
    }

    [Test]
    public void Barter_APayoutThatFits_GoesThrough()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 1, Hat, 3);
        GiveSlots(p, Tooth, 3);
        FillBagExcept(world, p, leaveFree: 8);

        shop.Barter(Idx, ShopNum, barterSlot: 1, multiples: 2);   // six slots of nine free

        Assert.That(ItemSystem.CountItem(p, world.Items, Hat), Is.EqualTo(6));
    }

    // ── Buy: gold clamps, room refuses ───────────────────────────────────────

    static void SetSales(GameWorld world, params int[] itemNums)
    {
        var sales = world.Shops[ShopNum].SalesItem;
        sales.Clear();
        sales.AddRange(itemNums);
    }

    [Test]
    public void Buy_ManyOfANonStackingItem_ArriveAsSeparateSlots()
    {
        var (world, shop, _, p) = Setup();
        world.Items[Hat].Price = 10;
        SetSales(world, Hat);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Buy(Idx, ShopNum, salesSlot: 1, quantity: 4);

        Assert.Multiple(() =>
        {
            Assert.That(SlotsHolding(p, Hat), Is.EqualTo(4));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(60), "charged for four");
        });
    }

    [Test]
    public void Buy_MoreThanThePurseCovers_ClampsToWhatItDoes()
    {
        var (world, shop, _, p) = Setup();
        world.Items[Hat].Price = 10;
        SetSales(world, Hat);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 35;

        shop.Buy(Idx, ShopNum, salesSlot: 1, quantity: 10);

        Assert.Multiple(() =>
        {
            Assert.That(SlotsHolding(p, Hat), Is.EqualTo(3), "three is what 35 gold buys");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(5), "and only three are charged for");
        });
    }

    [Test]
    public void Buy_MoreThanTheBagHolds_RefusedRatherThanTrimmed()
    {
        var (world, shop, _, p) = Setup();
        world.Items[Hat].Price = 10;
        SetSales(world, Hat);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 1000;
        FillBagExcept(world, p, leaveFree: 3);

        shop.Buy(Idx, ShopNum, salesSlot: 1, quantity: 8);

        Assert.Multiple(() =>
        {
            Assert.That(SlotsHolding(p, Hat), Is.Zero, "none bought");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(1000), "and nothing charged");
        });
    }

    // ── Sell: only copies that are indistinguishable ─────────────────────────

    [Test]
    public void Sell_BulkSellsEveryIdenticalCopy()
    {
        var (world, shop, _, p) = Setup();
        world.Items[Hat].Price = 100;
        GiveSlots(p, Hat, 4);
        int first = FirstSlotHolding(p, Hat);

        shop.Sell(Idx, first, quantity: 4);

        Assert.That(SlotsHolding(p, Hat), Is.Zero);
    }

    /// <summary>Durability is part of "identical" — it is what ItemSellValue prices on, so a battered copy
    /// is a different thing from a pristine one and cannot be swept up with it.</summary>
    [Test]
    public void Sell_LeavesCopiesAtADifferentDurability_Alone()
    {
        var (world, shop, _, p) = Setup();
        world.Items[Hat].Type = ItemType.Helmet;
        world.Items[Hat].Price = 100;
        world.Items[Hat].Durability = 40;
        GiveSlots(p, Hat, 3, dur: 40);
        GiveSlots(p, Hat, 2, dur: 11);
        int pristine = FirstSlotHolding(p, Hat);

        shop.Sell(Idx, pristine, quantity: 0);   // 0 = as many as are identical to it

        Assert.Multiple(() =>
        {
            Assert.That(SlotsHolding(p, Hat), Is.EqualTo(2), "the battered pair survives");
            Assert.That(SlotsHoldingAtDur(p, Hat, 11), Is.EqualTo(2));
        });
    }

    // ── A full bag ───────────────────────────────────────────────────────────
    // The vendor is how a bag gets emptied, so a full one must never be the thing that stops a sale. What
    // makes that work is the ORDER: the goods leave before the payment arrives, so the slot the payment
    // needs is the slot the sale just freed.

    /// <summary>A blade is worth something to a vendor, which a gem is not: ItemValue derives a price for
    /// equipment and returns nothing for treasure, whose worth is authored and only realisable by barter.
    /// So these use one.</summary>
    static void MakeSellable(GameWorld world)
    {
        world.Items[Blade].Type = ItemType.Weapon;
        world.Items[Blade].Power = 10;
        world.Items[Blade].Durability = 40;
    }

    [Test]
    public void Sell_FromACompletelyFullBagWithNoGold_StillPays()
    {
        var (world, shop, _, p) = Setup();
        MakeSellable(world);
        GiveSlots(p, Blade, 1, dur: 40);
        FillBagExcept(world, p, leaveFree: 0);
        int slot = FirstSlotHolding(p, Blade);

        shop.Sell(Idx, slot, quantity: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.GreaterThan(0), "the payment landed");
            Assert.That(SlotsHolding(p, Blade), Is.Zero, "and the blade is gone");
        });
    }

    [Test]
    public void Sell_EveryIdenticalCopyFromAFullBag_StillPays()
    {
        var (world, shop, _, p) = Setup();
        MakeSellable(world);
        GiveSlots(p, Blade, 4, dur: 40);
        FillBagExcept(world, p, leaveFree: 0);
        int slot = FirstSlotHolding(p, Blade);

        shop.Sell(Idx, slot, quantity: 4);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.GreaterThan(0));
            Assert.That(SlotsHolding(p, Blade), Is.Zero);
        });
    }

    /// <summary>The payment is what frees the room. Two teeth for three hats with two slots free works,
    /// because handing the teeth over leaves four.</summary>
    [Test]
    public void Barter_WhereHandingOverThePaymentMakesTheRoom_GoesThrough()
    {
        var (world, shop, _, p) = Setup();
        SetBarterRow(world, Tooth, 2, Hat, 3);
        GiveSlots(p, Tooth, 2);
        FillBagExcept(world, p, leaveFree: 2);

        shop.Barter(Idx, ShopNum, barterSlot: 1);

        Assert.That(ItemSystem.CountItem(p, world.Items, Hat), Is.EqualTo(3));
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    static int SlotsHolding(PlayerRecord p, int itemNum)
    {
        int n = 0;
        for (int i = 1; i <= Constants.MaxInv; i++) if (p.Inv[i].Num == itemNum) n++;
        return n;
    }

    static int SlotsHoldingAtDur(PlayerRecord p, int itemNum, int dur)
    {
        int n = 0;
        for (int i = 1; i <= Constants.MaxInv; i++) if (p.Inv[i].Num == itemNum && p.Inv[i].Dur == dur) n++;
        return n;
    }

    static int FirstSlotHolding(PlayerRecord p, int itemNum)
    {
        for (int i = 1; i <= Constants.MaxInv; i++) if (p.Inv[i].Num == itemNum) return i;
        return 0;
    }

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
