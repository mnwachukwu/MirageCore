using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The shop-editor row: the Store/Inn radio facade over ShopType is mutually exclusive; the shop's
/// dirty flag AGGREGATES its nested trade rows (a dirty trade dirties the shop) and its structure (adding or
/// removing a row dirties the shop, ClearDirty clears both). The trade table is dynamic — blank by default,
/// grown via AddBarter up to the MaxTrades ceiling; load skips empty/legacy-null slots, and ToRecord persists a
/// dense list (empty rows dropped, no gaps).</summary>
[TestFixture]
public class ShopRowViewModelTests
{
    static ShopRowViewModel Shop(ShopRecord? r = null) =>
        new(1, r ?? new ShopRecord(), () => [], () => [], _ => false);

    static ShopRecord ShopWith(params BarterItemRecord[] trades)
    {
        var r = new ShopRecord();
        r.BarterItem.AddRange(trades);
        return r;
    }

    static BarterItemRecord Trade(int giveItem = 1, int giveQuantity = 100, int getItem = 2, int getQuantity = 1) =>
        new() { GiveItem = giveItem, GiveQuantity = giveQuantity, GetItem = getItem, GetQuantity = getQuantity };

    [Test]
    public void StoreAndInn_AreMutuallyExclusiveRadios()
    {
        var s = Shop();

        s.IsInn = true;
        Assert.Multiple(() =>
        {
            Assert.That(s.ShopType, Is.EqualTo(ShopType.Inn));
            Assert.That(s.IsInn, Is.True);
            Assert.That(s.IsStore, Is.False);
        });

        s.IsStore = true;
        Assert.Multiple(() =>
        {
            Assert.That(s.ShopType, Is.EqualTo(ShopType.Store));
            Assert.That(s.IsStore, Is.True);
            Assert.That(s.IsInn, Is.False);
        });
    }

    [Test]
    public void NewShop_StartsWithABlankTradeTable()
    {
        var s = Shop();
        Assert.Multiple(() =>
        {
            Assert.That(s.Barters, Is.Empty, "a shop with no trades shows a blank table");
            Assert.That(s.HasNoBarters, Is.True);
            Assert.That(s.IsDirty, Is.False);
        });
    }

    [Test]
    public void Load_SkipsEmptyAndLegacyNullTradeSlots()
    {
        // Legacy shop JSON deserializes to a list with a leading null + empty padding; keep only real trades.
        var r = new ShopRecord();
        r.BarterItem.Add(null!);                 // legacy index-0 null
        r.BarterItem.Add(Trade(getItem: 5));     // real
        r.BarterItem.Add(new BarterItemRecord());  // empty
        r.BarterItem.Add(Trade(getItem: 9));     // real

        var s = Shop(r);

        Assert.That(s.Barters, Has.Count.EqualTo(2), "only the two real trades load, dense");
    }

    [Test]
    public void EditingANestedTradeRow_MarksTheShopDirty()
    {
        var s = Shop(ShopWith(Trade()));
        Assume.That(s.IsDirty, Is.False, "a freshly loaded shop is clean");

        s.Barters[0].GetItem = 5;

        Assert.That(s.IsDirty, Is.True, "a dirty trade row makes the whole shop dirty");
    }

    [Test]
    public void AddTrade_AppendsARowAndDirties_RemoveTrade_RemovesIt()
    {
        var s = Shop();
        Assume.That(s.IsDirty, Is.False);

        s.AddBarterCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(s.Barters, Has.Count.EqualTo(1), "add appends a blank row");
            Assert.That(s.HasNoBarters, Is.False);
            Assert.That(s.IsDirty, Is.True, "a structural change dirties the shop");
        });

        s.RemoveBarterCommand.Execute(s.Barters[0]);
        Assert.That(s.Barters, Is.Empty, "remove deletes the row");
    }

    /// <summary>There is no row ceiling — the same rule the sales table already had. An author adds as many
    /// trades as the shop needs, and the wire is JSON, so nothing downstream constrains the count.</summary>
    [Test]
    public void AddTrade_HasNoCeiling()
    {
        var s = Shop();
        for (int i = 0; i < 300; i++) s.AddBarterCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(s.Barters, Has.Count.EqualTo(300));
            Assert.That(s.AddBarterCommand.CanExecute(null), Is.True, "still addable past the old 255 ceiling");
        });
    }

    [Test]
    public void ClearDirty_ClearsShopLevelStructuralAndNestedTradeDirt()
    {
        var s = Shop(ShopWith(Trade()));
        s.Name = "Blacksmith";            // shop-level dirt
        s.Barters[0].GetItem = 5;          // nested-trade dirt
        s.AddBarterCommand.Execute(null);  // structural dirt
        Assume.That(s.IsDirty, Is.True);

        s.ClearDirty();

        Assert.That(s.IsDirty, Is.False);
    }

    [Test]
    public void ApplyPacket_LoadsExactTrades_AndDoesNotMarkDirty()
    {
        var s = Shop();
        s.ApplyPacket(new UpdateShopPacket
        {
            Name = "General Store",
            ShopType = ShopType.Store,
            Barters = [new EditorSaveShopPacket.BarterEntry(GiveItem: 1, GiveQuantity: 100, GetItem: 2, GetQuantity: 1)],
        });

        Assert.Multiple(() =>
        {
            Assert.That(s.Barters, Has.Count.EqualTo(1), "loads exactly the wire trades — no padding");
            Assert.That(s.IsLoaded, Is.True);
            Assert.That(s.IsDirty, Is.False, "loading from the wire is not an edit");
            Assert.That(s.Barters[0].ToRecord().GiveItem, Is.EqualTo(1), "the row carries the packet's trade");
        });
    }

    [Test]
    public void Keeper_RoundTripsThroughRecordAndPacket()
    {
        var s = Shop(new ShopRecord { Keeper = 7 });
        Assert.That(s.Keeper, Is.EqualTo(7), "ctor reads the record's keeper NPC");
        Assert.That(s.ToRecord().Keeper, Is.EqualTo(7), "ToRecord writes it back");

        s.ApplyPacket(new UpdateShopPacket { Keeper = 12 });
        Assert.That(s.Keeper, Is.EqualTo(12), "ApplyPacket adopts the wire keeper");
    }

    [Test]
    public void ToRecord_RoundTripsFlagsAndTrades()
    {
        var rec = ShopWith(Trade(giveItem: 1, giveQuantity: 100, getItem: 2, getQuantity: 1));
        rec.Name = "Shop";
        rec.ShopType = ShopType.Store;
        rec.FixesItems = true;
        rec.AllowBanking = false;

        var back = Shop(rec).ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Shop"));
            Assert.That(back.ShopType, Is.EqualTo(ShopType.Store));
            Assert.That(back.FixesItems, Is.True);
            Assert.That(back.BarterItem, Has.Count.EqualTo(1));
            Assert.That(back.BarterItem[0].GiveItem, Is.EqualTo(1));
            Assert.That(back.BarterItem[0].GiveQuantity, Is.EqualTo(100));
        });
    }

    [Test]
    public void ToRecord_DropsEmptyRows_SoPersistedTradesAreDense()
    {
        var s = Shop(ShopWith(Trade(getItem: 5), Trade(getItem: 6), Trade(getItem: 7)));
        // Blank the middle row (both sides itemless) — it must not persist, and the list compacts with no gap.
        s.Barters[1].GiveItem = 0;
        s.Barters[1].GetItem = 0;

        var back = s.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(back.BarterItem, Has.Count.EqualTo(2), "the empty middle row is dropped");
            Assert.That(back.BarterItem[0].GetItem, Is.EqualTo(5));
            Assert.That(back.BarterItem[1].GetItem, Is.EqualTo(7), "the third row compacts up — no gap");
        });
    }
}
