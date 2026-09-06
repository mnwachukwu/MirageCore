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

/// <summary>
/// The player marketplace: a global set of item listings, each escrowed off its seller until it sells or is
/// canceled. Runs on the game thread (no locks). Browsable from any inn; a completed sale delivers the goods
/// to the buyer and the POST-TAX payout to the seller as DELAYED marketplace mail (so a seller can be offline
/// throughout), and the tax is a gold sink. Listings live on <see cref="GameWorld.MarketListings"/>, loaded
/// once at boot, and persist per-entry as JSON. Reuses the mail delayed-delivery + attachment plumbing.
/// </summary>
public sealed class MarketSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly MailSystem _mail;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;

    // Monotonic id source, lazily seeded from the loaded listings on first use (0 = not yet seeded). Never
    // reused within a session, so a buyer's cached listing id can't silently rebind to a different item.
    private int _nextId;
    private int _nextSaleId;   // monotonic id for the sales-history log

    // Engine sender label on the delivery / return mail (like the "System" label for other engine mail).
    private static string MarketSender => ServerStrings.Get(ServerStrings.Market_Sender);

    public MarketSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items,
        MailSystem mail, IPersistenceService persistence, IBackgroundPersistence bg,
                        IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _mail = mail;
        _persistence = persistence;
        _bg = bg;
    }

    // ── Open / browse ──────────────────────────────────────────────────────────────

    /// <summary>Open the marketplace for a player at an inn — validates the location, then pushes the current
    /// listings with the open signal. A client that skips the inn is refused.</summary>
    public void Open(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (!IsAtInn(index))
        {
            SendMsg(index, ServerStrings.Market_NotNearInn, GameColor.BrightRed);
            return;
        }
        _pm[index].ViewingMarket = true;   // now a live-broadcast recipient until they close the panel
        SyncTo(index, open: true);
    }

    /// <summary>Re-fetch the current listings on demand (the client's "Refresh" button) — the same live data a
    /// broadcast would push, but pulled. Keeps the panel open; re-arms the viewer flag defensively.</summary>
    public void Refresh(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (!IsAtInn(index))
        {
            SendMsg(index, ServerStrings.Market_NotNearInn, GameColor.BrightRed);
            return;
        }
        _pm[index].ViewingMarket = true;
        SyncTo(index, open: false);
    }

    /// <summary>The player closed the market panel — stop broadcasting live listing updates to them.</summary>
    public void Close(int index) => _pm[index].ViewingMarket = false;

    // ── List ────────────────────────────────────────────────────────────────────────

    /// <summary>List an inventory stack for sale at a fixed gold price. Escrows the item off the seller
    /// (anti-dupe; refuses equipped/empty and gold itself), capped per seller. Amount applies to a currency
    /// slot only.</summary>
    public void List(int index, int invSlot, int amount, int price)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (!IsAtInn(index))
        {
            SendMsg(index, ServerStrings.Market_NotNearInn, GameColor.BrightRed);
            return;
        }
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;
        if (price <= 0 || price > Constants.MarketMaxPrice)
        {
            SendMsg(index, ServerStrings.Market_BadPrice, GameColor.BrightRed);
            return;
        }
        if (CountBySeller(sp.Login) >= Constants.MaxMarketListingsPerSeller)
        {
            SendMsg(index, ServerStrings.Market_TooManyListings, GameColor.BrightRed);
            return;
        }

        var slot = sp.Char.Inv[invSlot];
        if (slot.Num <= 0 || slot.Num > _world.Limits.Items)
        {
            SendMsg(index, ServerStrings.Market_CannotList, GameColor.BrightRed);
            return;
        }
        if (_world.Items[slot.Num].NonListable)
        {
            SendMsg(index, ServerStrings.Market_CannotListItem, GameColor.BrightRed);
            return;
        }

        var (num, val, dur) = _items.RemoveFromSlot(index, invSlot, amount);
        if (num <= 0)
        {
            SendMsg(index, ServerStrings.Market_CannotList, GameColor.BrightRed);
            return;
        }

        var listing = new MarketListing
        {
            Id = NextId(),
            Seller = sp.Login,
            ItemNum = num,
            Quantity = val,
            Dur = dur,
            Price = price,
            ListedUtc = NowUtc,
        };
        _world.MarketListings[listing.Id] = listing;
        Persist(listing);
        _pm.MarkDirty(index);   // the escrow left the seller's bag — persist so a disconnect can't dupe it
        SendMsg(index, ServerStrings.Market_Listed, GameColor.BrightGreen);
        SyncViewers();   // new listing shows up for every open browser
    }

    // ── Buy ───────────────────────────────────────────────────────────────────────

    /// <summary>Buy a listing: charge the buyer its price in gold, drop the listing, then deliver the goods to
    /// the buyer and the post-tax payout to the seller as delayed marketplace mail. Can't buy your own.</summary>
    public void Buy(int index, int listingId, int amount)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (!IsAtInn(index))
        {
            SendMsg(index, ServerStrings.Market_NotNearInn, GameColor.BrightRed);
            return;
        }
        if (!_world.MarketListings.TryGetValue(listingId, out var listing))
        {
            SendMsg(index, ServerStrings.Market_ListingGone, GameColor.BrightRed);
            SyncTo(index, open: false);
            return;
        }
        if (string.Equals(listing.Seller, sp.Login, StringComparison.OrdinalIgnoreCase))
        {
            SendMsg(index, ServerStrings.Market_CannotBuyOwn, GameColor.BrightRed);
            return;
        }

        // A CURRENCY listing prices PER UNIT and can be bought partially (amount units); anything else buys the
        // whole stack. Cost is exact — units * per-unit price, no proration.
        bool isCurrency = listing.ItemNum > 0 && listing.ItemNum < _world.Items.Length
            && _world.Items[listing.ItemNum].Type == ItemType.Currency;
        int units = isCurrency && amount > 0 && amount < listing.Quantity ? amount : listing.Quantity;
        long cost = isCurrency ? (long)units * listing.Price : listing.Price;
        if (cost <= 0)
        {
            SendMsg(index, ServerStrings.Market_BadPrice, GameColor.BrightRed);
            return;
        }

        if (ItemSystem.CountItem(sp.Char, _world.Items, Constants.GoldItemIndex) < cost)
        {
            SendMsg(index, ServerStrings.Market_NotEnoughGold, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);   // cost <= the buyer's (int) gold
        _pm.MarkDirty(index);   // the buyer's gold left the bag — persist within the tick

        // Shrink a partially-bought currency listing (per-unit price unchanged); otherwise remove it wholesale.
        if (isCurrency && units < listing.Quantity)
        {
            listing.Quantity -= units;
            Persist(listing);
        }
        else
        {
            _world.MarketListings.Remove(listingId);
            Unpersist(listingId);
        }

        long deliverAt = NowUtc + Rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);
        int tax = SaleTax((int)cost);
        int payout = (int)cost - tax;

        // Goods → buyer, post-tax payout → seller, both as delayed marketplace mail.
        _mail.Deliver(sp.Login, MarketSender, ServerStrings.Get(ServerStrings.Market_BoughtSubject),
            ServerStrings.Get(ServerStrings.Market_BoughtBody),
            new List<MailAttachment> { new() { ItemNum = listing.ItemNum, Quantity = units, Dur = listing.Dur } }, deliverAt);
        _mail.Deliver(listing.Seller, MarketSender, ServerStrings.Get(ServerStrings.Market_SoldSubject),
            ServerStrings.Format(ServerStrings.Market_SoldBody, ("Gold", payout), ("Tax", tax)),
            new List<MailAttachment> { new() { ItemNum = Constants.GoldItemIndex, Quantity = payout } }, deliverAt);

        RecordSale(listing.Seller, sp.Login, listing.ItemNum, units, (int)cost, tax);

        SendMsg(index, ServerStrings.Market_Bought, GameColor.BrightGreen);
        SyncViewers();   // the sold/shrunk listing updates for every open browser
    }

    // ── Cancel ──────────────────────────────────────────────────────────────────────

    /// <summary>Cancel your own listing: return the escrowed stack to your bag (or your mailbox if the bag is
    /// full), then drop the listing.</summary>
    public void Cancel(int index, int listingId)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (!IsAtInn(index))
        {
            SendMsg(index, ServerStrings.Market_NotNearInn, GameColor.BrightRed);
            return;
        }
        if (!_world.MarketListings.TryGetValue(listingId, out var listing))
        {
            SyncTo(index, open: false);
            return;
        }
        if (!string.Equals(listing.Seller, sp.Login, StringComparison.OrdinalIgnoreCase)) return;   // not yours

        _world.MarketListings.Remove(listingId);
        Unpersist(listingId);

        if (!_items.TryGiveItem(index, listing.ItemNum, listing.Quantity, listing.Dur))
        {
            _mail.Deliver(sp.Login, MarketSender, ServerStrings.Get(ServerStrings.Market_ReturnSubject),
                ServerStrings.Get(ServerStrings.Market_ReturnBody),
                new List<MailAttachment> { new() { ItemNum = listing.ItemNum, Quantity = listing.Quantity, Dur = listing.Dur } });
        }

        _pm.MarkDirty(index);
        SendMsg(index, ServerStrings.Market_Canceled, GameColor.BrightGreen);
        SyncViewers();   // the removed listing disappears for every open browser
    }

    // ── Expiry sweep ──────────────────────────────────────────────────────────────

    /// <summary>Periodic game-thread sweep: a listing older than its lifetime is dropped and its escrowed item
    /// is mailed back to the seller (who may be offline). Called from the game loop's slow maintenance tick.</summary>
    public void TickExpiry()
    {
        long nowUtc = NowUtc;
        List<MarketListing>? expired = null;
        foreach (var l in _world.MarketListings.Values)
        {
            if (nowUtc - l.ListedUtc >= Constants.MarketListingLifetimeSeconds)
                (expired ??= new()).Add(l);
        }

        if (expired is null) return;
        foreach (var l in expired)
        {
            _world.MarketListings.Remove(l.Id);
            Unpersist(l.Id);
            _mail.Deliver(l.Seller, MarketSender, ServerStrings.Get(ServerStrings.Market_ExpiredSubject),
                ServerStrings.Get(ServerStrings.Market_ExpiredBody),
                new List<MailAttachment> { new() { ItemNum = l.ItemNum, Quantity = l.Quantity, Dur = l.Dur } });
        }
        SyncViewers();   // expired listings disappear for every open browser
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    /// <summary>The sale tax (a gold sink) withheld from a listing price — floor of the configured percent.
    /// Public so the compose UI's "you receive" preview and the server agree on the number.</summary>
    public static int SaleTax(int price) => (int)((long)price * Constants.MarketSaleTaxPercent / 100);

    private bool IsAtInn(int index)
    {
        int shopNum = _pm[index].ActiveShop(_world, index);
        return shopNum > 0 && shopNum < _world.Shops.Length && _world.Shops[shopNum].ShopType == ShopType.Inn;
    }

    private int CountBySeller(string login)
        => _world.MarketListings.Values.Count(l => string.Equals(l.Seller, login, StringComparison.OrdinalIgnoreCase));

    // Re-push the listings to EVERY player currently viewing the market, so a change by one browser shows up
    // for all of them without a close-reopen. Per-viewer (each sees their own MySales + login), but the viewer
    // set is small and listing changes are infrequent.
    private void SyncViewers()
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[i].IsPlaying && _pm[i].ViewingMarket)
                SyncTo(i, open: false);
        }
    }

    // Push the current listings to one player; open=true also opens their market panel.
    private void SyncTo(int index, bool open)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        _dispatcher.SendTo(index, new MarketListPacket
        {
            Listings = _world.MarketListings.Values.Select(MarketListPacket.Entry.From).ToList(),
            MySales = _world.MarketSales.Where(s => string.Equals(s.Seller, sp.Login, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Clone()).ToList(),
            MeLogin = sp.Login,
            Open = open,
            NowUtc = NowUtc,
        });
    }

    private int NextId()
    {
        if (_nextId == 0)
            _nextId = (_world.MarketListings.Count == 0 ? 0 : _world.MarketListings.Keys.Max()) + 1;
        return _nextId++;
    }

    // Fire-and-forget per-entry persistence; null-tolerant so the unit harness can drive the system with
    // null persistence/background deps (the "deps can be null" test convention).
    private void Persist(MarketListing listing)
    {
        if (_persistence is null || _bg is null) return;
        _bg.Run(_persistence.SaveMarketListingAsync(listing.Id, listing.Clone()), "MarketSave");
    }

    private void Unpersist(int id)
    {
        if (_persistence is null || _bg is null) return;
        _bg.Run(_persistence.DeleteMarketListingAsync(id), "MarketDelete");
    }

    // Append a completed sale to the rolling history (seller Sales tab + on-disk audit), bounded to the cap.
    private void RecordSale(string seller, string buyer, int itemNum, int value, int price, int tax)
    {
        _world.MarketSales.Add(new MarketSale
        {
            Id = NextSaleId(),
            Seller = seller,
            Buyer = buyer,
            ItemNum = itemNum,
            Quantity = value,
            Price = price,
            Tax = tax,
            TimeUtc = NowUtc,
        });
        while (_world.MarketSales.Count > Constants.MaxMarketSalesLog) _world.MarketSales.RemoveAt(0);
        if (_persistence is not null && _bg is not null)
            _bg.Run(_persistence.SaveMarketSalesAsync(_world.MarketSales.Select(s => s.Clone()).ToList()), "MarketSalesSave");
    }

    private int NextSaleId()
    {
        if (_nextSaleId == 0)
            _nextSaleId = (_world.MarketSales.Count == 0 ? 0 : _world.MarketSales.Max(s => s.Id)) + 1;
        return _nextSaleId++;
    }
}
