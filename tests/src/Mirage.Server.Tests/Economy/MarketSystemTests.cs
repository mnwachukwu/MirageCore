using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Mirage.Server.Tests.Economy;

/// <summary>The player marketplace on <see cref="MarketSystem"/>: listing escrows the item off the seller,
/// buying charges the buyer and delivers goods + post-tax payout as delayed mail, canceling returns the item,
/// and the guards (own listing, insufficient gold, gold-not-listable, per-seller cap, no open inn session) hold.
/// Buy delivers via mail, whose subject/body resolve through ServerStrings (loaded once by StringsSetUpFixture).</summary>
[TestFixture]
public class MarketSystemTests
{
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10;

    static (GameWorld world, PlayerManager pm, MarketSystem market) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);
        var market = new MarketSystem(world, pm, dispatcher, items, mail, persistence: null!, bg: null!);

        // An inn reachable via its keeper NPC — any inn opens the marketplace (the market resolves the active
        // shop from the keeper, not the map). The keeper sits on Map 1 at (0,0); AtInn opens the session against
        // that slot, which is the whole of what authorises a listing.
        world.Shops[1].ShopType = ShopType.Inn;
        world.Shops[1].Keeper = KeeperNpc;
        world.MapNpcs[1, KeeperSlot].Num = KeeperNpc;
        return (world, pm, market);
    }

    const int KeeperNpc = 1, KeeperSlot = 1;

    static ServerPlayer AtInn(GameWorld world, PlayerManager pm, int idx, string login)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = login;
        sp.Char.Map = 1;
        world.MapObservers[1].Add(idx);
        sp.SetActiveShop(1, 1, KeeperSlot);
        return sp;
    }

    [Test]
    public void List_EscrowsItem_CreatesListing()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Sword;
        sp.Char.Inv[3].Dur = 40;

        market.List(1, invSlot: 3, amount: 0, price: 500);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Inv[3].Num, Is.EqualTo(0), "the item is escrowed off the seller");
            Assert.That(world.MarketListings, Has.Count.EqualTo(1));
            var l = world.MarketListings.Values.First();
            Assert.That(l.Seller, Is.EqualTo("seller"));
            Assert.That(l.ItemNum, Is.EqualTo(Sword));
            Assert.That(l.Dur, Is.EqualTo(40), "worn durability rides into the listing");
            Assert.That(l.Price, Is.EqualTo(500));
        });
    }

    [Test]
    public void List_NonListable_IsRefused()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].NonListable = true;   // stands in for gold / reagent / valor / a soulbound item
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Sword;

        market.List(1, 3, 0, 500);

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "a non-listable item can't be listed");
            Assert.That(sp.Char.Inv[3].Num, Is.EqualTo(Sword), "and it isn't escrowed");
        });
    }

    // The market runs off the INN SESSION, not off where the player is standing: the panel pins them in place,
    // so a keeper wandering off mid-listing must not void the sale. What still has to hold is that there is a
    // session at all, and that it belongs to an inn — leaving the map clears it (MovementSystem.PlayerWarp).
    [Test]
    public void List_WithNoOpenInnSession_IsRefused()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.ClearActiveShop();   // what leaving the map does
        sp.Char.Inv[3].Num = Sword;

        market.List(1, 3, 0, 500);

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "listing with no inn session open is refused");
            Assert.That(sp.Char.Inv[3].Num, Is.EqualTo(Sword), "and the item isn't escrowed");
        });
    }

    [Test]
    public void List_FromAShopThatIsNotAnInn_IsRefused()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        world.Shops[1].ShopType = ShopType.Store;   // an open session, but not to an innkeeper
        sp.Char.Inv[3].Num = Sword;

        market.List(1, 3, 0, 500);

        Assert.That(world.MarketListings, Is.Empty, "only an innkeeper's counter reaches the market");
    }

    [Test]
    public void List_AfterTheInnkeeperWalksAway_StillWorks()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.X = 15;   // the pair are now nowhere near each other
        sp.Char.Y = 11;
        sp.Char.Inv[3].Num = Sword;

        market.List(1, 3, 0, 500);

        Assert.That(world.MarketListings, Is.Not.Empty,
            "the panel pins the player in place, so only the keeper can move — that must not void the sale");
    }

    [Test]
    public void List_RespectsPerSellerCap()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        for (int i = 0; i < Constants.MaxMarketListingsPerSeller + 2; i++)
        {
            sp.Char.Inv[1].Num = Sword;   // refill the slot each round (the escrow clears it)
            market.List(1, 1, 0, 100);
        }

        Assert.That(world.MarketListings.Count, Is.EqualTo(Constants.MaxMarketListingsPerSeller), "listings are capped per seller");
    }

    // The headline path: a purchase charges the buyer, drops the listing, and delivers the goods to the buyer
    // and the post-tax payout to the seller as (delayed) mail.
    [Test]
    public void Buy_ChargesBuyer_RemovesListing_DeliversGoodsAndPayout()
    {
        var (world, pm, market) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        var seller = AtInn(world, pm, 1, "seller");
        var buyer = AtInn(world, pm, 2, "buyer");
        seller.Char.Inv[3].Num = Sword;
        seller.Char.Inv[3].Dur = 40;
        buyer.Char.Inv[1].Num = Gold;
        buyer.Char.Inv[1].Quantity = 1000;
        market.List(1, 3, 0, price: 500);
        int listingId = world.MarketListings.Values.First().Id;

        market.Buy(2, listingId, 0);

        int tax = MarketSystem.SaleTax(500);
        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "the listing is removed once sold");
            Assert.That(ItemSystem.CountItem(buyer.Char, world.Items, Gold), Is.EqualTo(500), "the buyer is charged the price");
            Assert.That(buyer.Mail, Has.Count.EqualTo(1), "the goods are delivered to the buyer as mail");
            Assert.That(buyer.Mail[0].Attachments[0].ItemNum, Is.EqualTo(Sword));
            Assert.That(buyer.Mail[0].Attachments[0].Dur, Is.EqualTo(40), "with the seller's worn durability");
            Assert.That(seller.Mail, Has.Count.EqualTo(1), "the payout is delivered to the seller as mail");
            Assert.That(seller.Mail[0].Attachments[0].ItemNum, Is.EqualTo(Gold));
            Assert.That(seller.Mail[0].Attachments[0].Quantity, Is.EqualTo(500 - tax), "the seller nets the price minus the sale tax");
        });
    }

    [Test]
    public void Buy_OwnListing_IsRefused()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Sword;
        market.List(1, 3, 0, 500);
        int id = world.MarketListings.Values.First().Id;

        market.Buy(1, id, 0);

        Assert.That(world.MarketListings, Has.Count.EqualTo(1), "you can't buy your own listing");
    }

    [Test]
    public void Buy_InsufficientGold_IsRefused()
    {
        var (world, pm, market) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        var seller = AtInn(world, pm, 1, "seller");
        var buyer = AtInn(world, pm, 2, "buyer");
        seller.Char.Inv[3].Num = Sword;
        buyer.Char.Inv[1].Num = Gold;
        buyer.Char.Inv[1].Quantity = 100;  // short of the 500 price
        market.List(1, 3, 0, 500);
        int id = world.MarketListings.Values.First().Id;

        market.Buy(2, id, 0);

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Has.Count.EqualTo(1), "the listing survives a failed buy");
            Assert.That(ItemSystem.CountItem(buyer.Char, world.Items, Gold), Is.EqualTo(100), "no gold is taken");
        });
    }

    [Test]
    public void Cancel_ReturnsItem_RemovesListing()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Sword;
        sp.Char.Inv[3].Dur = 40;
        market.List(1, 3, 0, 500);
        int id = world.MarketListings.Values.First().Id;

        market.Cancel(1, id);

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "the listing is removed");
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => sp.Char.Inv[i].Num == Sword), Is.True, "the escrowed item is returned to the seller");
        });
    }

    [Test]
    public void SaleTax_FloorsThePercent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarketSystem.SaleTax(500), Is.EqualTo(25), "5% of 500");
            Assert.That(MarketSystem.SaleTax(99), Is.EqualTo(4), "5% of 99 = 4.95, floored");
            Assert.That(MarketSystem.SaleTax(0), Is.EqualTo(0), "no tax on a zero price");
        });
    }

    // A currency listing prices PER UNIT and supports a partial buy: the buyer takes N units at exactly
    // N * per-unit (no proration), and the listing shrinks by N with its per-unit price unchanged.
    [Test]
    public void Buy_PartialCurrency_PerUnit_ChargesExactly()
    {
        const int Token = 12;
        var (world, pm, market) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Token].Type = ItemType.Currency;
        var seller = AtInn(world, pm, 1, "seller");
        var buyer = AtInn(world, pm, 2, "buyer");
        seller.Char.Inv[3].Num = Token;
        seller.Char.Inv[3].Quantity = 1000;
        buyer.Char.Inv[1].Num = Gold;
        buyer.Char.Inv[1].Quantity = 1000;
        market.List(1, 3, amount: 1000, price: 2);   // 2 gold PER UNIT
        var listing = world.MarketListings.Values.First();

        market.Buy(2, listing.Id, amount: 300);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(buyer.Char, world.Items, Gold), Is.EqualTo(1000 - 600), "charged 300 units * 2/unit = 600 exactly");
            Assert.That(world.MarketListings, Has.Count.EqualTo(1), "the listing survives, reduced");
            Assert.That(listing.Quantity, Is.EqualTo(700), "700 units remain");
            Assert.That(listing.Price, Is.EqualTo(2), "the per-unit price is unchanged");
            Assert.That(buyer.Mail[0].Attachments[0].Quantity, Is.EqualTo(300), "the buyer receives 300 tokens");
        });
    }

    [Test]
    public void Expiry_ReturnsListingToSeller()
    {
        var (world, pm, market) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Sword;
        sp.Char.Inv[3].Dur = 40;
        market.List(1, 3, 0, 500);
        var listing = world.MarketListings.Values.First();
        listing.ListedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Constants.MarketListingLifetimeSeconds - 10;

        market.TickExpiry();

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "the expired listing is removed");
            Assert.That(sp.Mail, Has.Count.EqualTo(1), "the item is mailed back to the seller");
            Assert.That(sp.Mail[0].Attachments[0].ItemNum, Is.EqualTo(Sword));
            Assert.That(sp.Mail[0].Attachments[0].Dur, Is.EqualTo(40), "with its durability");
        });
    }

    [Test]
    public void Buy_RecordsSaleInHistory()
    {
        var (world, pm, market) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        var seller = AtInn(world, pm, 1, "seller");
        var buyer = AtInn(world, pm, 2, "buyer");
        seller.Char.Inv[3].Num = Sword;
        buyer.Char.Inv[1].Num = Gold;
        buyer.Char.Inv[1].Quantity = 1000;
        market.List(1, 3, 0, 500);
        int id = world.MarketListings.Values.First().Id;

        market.Buy(2, id, 0);

        Assert.That(world.MarketSales, Has.Count.EqualTo(1));
        var sale = world.MarketSales[0];
        Assert.Multiple(() =>
        {
            Assert.That(sale.Seller, Is.EqualTo("seller"));
            Assert.That(sale.Buyer, Is.EqualTo("buyer"));
            Assert.That(sale.ItemNum, Is.EqualTo(Sword));
            Assert.That(sale.Price, Is.EqualTo(500), "gross price the buyer paid");
            Assert.That(sale.Tax, Is.EqualTo(MarketSystem.SaleTax(500)), "with the withheld tax recorded");
        });
    }

    // A currency stack can be listed in PART: only the chosen units are escrowed off the seller, the rest stays
    // in the bag (the client's units-to-list prompt drives this; the server clamps via RemoveFromSlot).
    [Test]
    public void List_PartialCurrency_EscrowsChosenUnits()
    {
        const int Token = 12;
        var (world, pm, market) = Setup();
        world.Items[Token].Type = ItemType.Currency;
        var sp = AtInn(world, pm, 1, "seller");
        sp.Char.Inv[3].Num = Token;
        sp.Char.Inv[3].Quantity = 1000;

        market.List(1, invSlot: 3, amount: 300, price: 2);   // list 300 of 1000 units at 2/ea

        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Has.Count.EqualTo(1));
            var l = world.MarketListings.Values.First();
            Assert.That(l.ItemNum, Is.EqualTo(Token));
            Assert.That(l.Quantity, Is.EqualTo(300), "only the chosen units are listed");
            Assert.That(l.Price, Is.EqualTo(2), "priced per unit");
            Assert.That(sp.Char.Inv[3].Num, Is.EqualTo(Token), "the slot survives");
            Assert.That(sp.Char.Inv[3].Quantity, Is.EqualTo(700), "the rest stays in the seller's bag");
        });
    }

    // Live listings: a change by one open browser re-syncs EVERY other open browser, not just the actor.
    [Test]
    public void ListingChange_BroadcastsToOtherViewers()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);
        var market = new MarketSystem(world, pm, dispatcher, items, mail, persistence: null!, bg: null!);
        world.Shops[1].ShopType = ShopType.Inn;
        world.Shops[1].Keeper = KeeperNpc;
        world.MapNpcs[1, KeeperSlot].Num = KeeperNpc;
        world.Items[Sword].Type = ItemType.Weapon;

        var lister = AtInn(world, pm, 1, "lister");
        _ = AtInn(world, pm, 2, "browser");
        lister.Char.Inv[3].Num = Sword;
        market.Open(1);            // both open the market → both become live-broadcast viewers
        market.Open(2);
        int browserSyncsBefore = dispatcher.MarketListTo.GetValueOrDefault(2);

        market.List(1, 3, 0, 500);   // the lister lists; the passive browser should get a live update

        Assert.That(dispatcher.MarketListTo.GetValueOrDefault(2), Is.GreaterThan(browserSyncsBefore),
            "a listing created by one browser is broadcast to the other open browser");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        // Counts MarketListPacket sends per player index, so a test can assert live-broadcast fan-out.
        public readonly Dictionary<int, int> MarketListTo = new();
        public void SendTo(int index, IPacket packet)
        {
            if (packet is MarketListPacket) MarketListTo[index] = MarketListTo.GetValueOrDefault(index) + 1;
        }
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
