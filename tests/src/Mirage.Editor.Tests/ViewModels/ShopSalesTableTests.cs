using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The shop editor's SALES table — items sold for gold at the item's own price, as opposed to the barter
/// table's give→get rows.
///
/// <para>The round-trip tests here are the load-bearing ones. Before the UI existed the view model carried
/// the sales list through load and save as an untouched <c>List<int></c>, precisely so a shop stocked
/// by the content generator could survive being opened in an editor that could not show it. Now that rows
/// exist, that guarantee has to be re-earned through a collection that empties, rebuilds and renumbers —
/// and a shopfront quietly lost on save looks exactly like a successful save.</para>
///
/// <para>Order is a real property, not presentation: <c>ShopRecord.Normalize</c> deliberately preserves it
/// because it is the order the player sees, which is what makes reordering worth authoring at all.</para>
/// </summary>
[TestFixture]
public class ShopSalesTableTests
{
    private const int Gold = 1;
    private const int Sword = 2;      // priced
    private const int Shield = 3;     // priced
    private const int Rock = 4;       // deliberately unpriced

    private static NamedEntry[] Entries() =>
    [
        new NamedEntry(0, ""), new NamedEntry(Gold, "Gold"), new NamedEntry(Sword, "Sword"),
        new NamedEntry(Shield, "Shield"), new NamedEntry(Rock, "Rock"),
    ];
    private static int? PriceOf(int id) => id switch
    {
        Sword => 100,
        Shield => 250,
        Rock => 0,
        _ => null,
    };

    private static ShopRowViewModel Shop(params int[] sales) =>
        new(1, new ShopRecord { Name = "General Store", ShopType = ShopType.Store, SalesItem = [.. sales] },
            Entries, Entries, static _ => false, PriceOf);

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Test]
    public void LoadAndSave_PreservesTheStockAndItsOrder()
    {
        var shop = Shop(Shield, Sword, Rock);

        var saved = shop.ToRecord();

        Assert.That(saved.SalesItem, Is.EqualTo(new[] { Shield, Sword, Rock }).AsCollection,
            "authored order IS the shopfront — re-sorting would rearrange someone's storefront");
    }

    [Test]
    public void OpeningAStockedShop_DoesNotMarkItDirty()
    {
        var shop = Shop(Sword, Shield);

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales, Has.Count.EqualTo(2));
            Assert.That(shop.IsDirty, Is.False, "a row built straight from disk has not been edited");
        });
    }

    [Test]
    public void ApplyPacket_RebuildsTheStock()
    {
        var shop = Shop();
        shop.ApplyPacket(new UpdateShopPacket
        {
            ShopNum = 1, Name = "General Store", ShopType = ShopType.Store, Sales = [Sword, Shield],
        });

        Assert.That(shop.BuildSavePacket().Sales, Is.EqualTo(new[] { Sword, Shield }).AsCollection);
    }

    [Test]
    public void EmptyRows_AreDroppedOnSave()
    {
        var shop = Shop(Sword);
        shop.AddSaleCommand.Execute(null);   // a half-authored row with no item picked yet

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales, Has.Count.EqualTo(2), "the blank row is real while authoring");
            Assert.That(shop.ToRecord().SalesItem, Is.EqualTo(new[] { Sword }).AsCollection,
                "but a saved file should say what it means");
        });
    }

    // ── Reordering ────────────────────────────────────────────────────────────

    [Test]
    public void MovingARow_ReordersTheStockAndRenumbers()
    {
        var shop = Shop(Sword, Shield, Rock);

        shop.MoveSaleDownCommand.Execute(shop.Sales[0]);

        Assert.Multiple(() =>
        {
            Assert.That(shop.ToRecord().SalesItem, Is.EqualTo(new[] { Shield, Sword, Rock }).AsCollection);
            // SlotIndex is the player-visible position, so it has to follow the move, not the original load.
            Assert.That(shop.Sales.Select(s => s.SlotIndex), Is.EqualTo(new[] { 1, 2, 3 }).AsCollection);
            Assert.That(shop.IsDirty, Is.True, "a reorder is a real edit");
        });
    }

    [Test]
    public void MovingPastTheEnds_IsRefused()
    {
        var shop = Shop(Sword, Shield);

        Assert.Multiple(() =>
        {
            Assert.That(shop.MoveSaleUpCommand.CanExecute(shop.Sales[0]), Is.False, "already first");
            Assert.That(shop.MoveSaleDownCommand.CanExecute(shop.Sales[1]), Is.False, "already last");
            Assert.That(shop.MoveSaleUpCommand.CanExecute(shop.Sales[1]), Is.True);
        });
    }

    // ── Price readout ─────────────────────────────────────────────────────────

    [Test]
    public void PriceComesFromTheItem_AndTheTotalIsTheSum()
    {
        var shop = Shop(Sword, Shield);

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales[0].Price, Is.EqualTo(100));
            Assert.That(shop.Sales[1].Price, Is.EqualTo(250));
            Assert.That(shop.SalesSummary, Does.Contain("350"), "the running total is the hard-to-eyeball part");
        });
    }

    [Test]
    public void AnUnpricedItem_ReadsAsFreeAndWarns()
    {
        var shop = Shop(Rock);

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales[0].HasNoPrice, Is.True);
            Assert.That(shop.Sales[0].HasPrice, Is.False, "the two must be exact complements — the row "
                + "template shows one TextBlock per state and they would otherwise overlap");
            Assert.That(shop.HasSalesWarning, Is.True, "giving stock away for nothing is never silent");
        });
    }

    [Test]
    public void AnEmptyRow_ShowsNeitherPriceState()
    {
        var shop = Shop();
        shop.AddSaleCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales[0].HasPrice, Is.False);
            Assert.That(shop.Sales[0].HasNoPrice, Is.False);
            Assert.That(shop.Sales[0].PriceText, Is.Empty);
        });
    }

    [Test]
    public void ListingTheSameItemTwice_Warns()
    {
        // Not an error — ShopRecord.Normalize drops the duplicate on load — but silent, and silent is what
        // makes it worth surfacing: the author sees two rows and the player gets one.
        var shop = Shop(Sword, Shield, Sword);

        Assert.That(shop.HasSalesWarning, Is.True);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    [Test]
    public void PickingAnItem_DirtiesTheShop_AndClearDirtyResetsRowsToo()
    {
        var shop = Shop(Sword);

        shop.Sales[0].SelectedItem = new NamedEntry(Shield, "Shield");
        Assert.That(shop.IsDirty, Is.True);

        shop.ClearDirty();

        Assert.Multiple(() =>
        {
            Assert.That(shop.Sales[0].IsDirty, Is.False,
                "a child left dirty re-marks the shop on its next derived re-raise, and the dot returns");
            Assert.That(shop.IsDirty, Is.False);
        });
    }

    [Test]
    public void RefreshingTheItemList_DoesNotDirtyTheShop()
    {
        var shop = Shop(Sword, Rock);

        shop.NotifyEntriesChanged();

        Assert.That(shop.IsDirty, Is.False, "a refresh is not an author edit");
    }
}
