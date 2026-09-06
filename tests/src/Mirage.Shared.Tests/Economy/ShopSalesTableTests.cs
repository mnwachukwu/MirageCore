using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Economy;

/// <summary>The sales table: the plain item-number list a shop sells for gold, alongside — not instead of —
/// the barter trade table. Covers canonicalization and the round-trips a generated shopfront has to survive.</summary>
[TestFixture]
public class ShopSalesTableTests
{
    private const int MaxItems = 1000;

    [Test]
    public void Normalize_DropsDeadNumbersAndDuplicates_KeepingAuthoredOrder()
    {
        var shop = new ShopRecord { SalesItem = [12, 0, 5, 12, -3, MaxItems + 1, 7, 5] };
        shop.Normalize(MaxItems);
        // Order is the DISPLAY order, so it must survive — sorting would quietly rearrange a shopfront
        // someone laid out deliberately.
        Assert.That(shop.SalesItem, Is.EqualTo(new[] { 12, 5, 7 }));
    }

    [Test]
    public void Normalize_LeavesAValidListAlone()
    {
        var shop = new ShopRecord { SalesItem = [3, 1, 2] };
        shop.Normalize(MaxItems);
        Assert.That(shop.SalesItem, Is.EqualTo(new[] { 3, 1, 2 }));
    }

    [Test]
    public void Normalize_OnAShopWithNoSales_IsANoOp()
    {
        // A shop authored before the sales table simply has none — an absent list deserializes to an empty
        // one, so this needs no migration path.
        var shop = new ShopRecord();
        shop.Normalize(MaxItems);
        Assert.That(shop.SalesItem, Is.Empty);
    }

    [Test]
    public void TheTwoTablesAreIndependent()
    {
        // The whole point of the split: barter keeps everything it had, and stocking a storefront does not
        // disturb it. A trade row can still name any two items; a sales entry is only ever a number.
        var shop = new ShopRecord
        {
            BarterItem = [new BarterItemRecord { GiveItem = 9, GiveQuantity = 5, GetItem = 4, GetQuantity = 1 }],
            SalesItem = [11, 12],
        };
        shop.Normalize(MaxItems);
        Assert.Multiple(() =>
        {
            Assert.That(shop.BarterItem, Has.Count.EqualTo(1), "normalizing sales must not touch trades");
            Assert.That(shop.BarterItem[0].GiveQuantity, Is.EqualTo(5), "GiveQuantity is the price");
            Assert.That(shop.SalesItem, Is.EqualTo(new[] { 11, 12 }));
        });
    }

    [Test]
    public void SalesSurviveTheEditorSaveRoundTrip()
    {
        // The destructive case this guards: a generated shopfront opened and saved in the editor must come
        // back intact. A field the round-trip forgets looks like a successful save and silently empties the
        // shop, which is exactly the kind of loss nothing would report.
        var packet = new EditorSaveShopPacket
        {
            ShopNum = 4,
            Name = "Kilnforged Armory",
            ShopType = ShopType.Store,
            Sales = [31, 32, 33],
            Barters = [new EditorSaveShopPacket.BarterEntry(1, 500, 88, 1)],
        };

        var shop = new ShopRecord
        {
            Name = packet.Name,
            ShopType = packet.ShopType,
            SalesItem = [.. packet.Sales],
            BarterItem = [.. packet.Barters.Select(t => new BarterItemRecord
            {
                GiveItem = t.GiveItem, GiveQuantity = t.GiveQuantity,
                GetItem = t.GetItem, GetQuantity = t.GetQuantity,
            })],
        };
        shop.Normalize(MaxItems);

        Assert.Multiple(() =>
        {
            Assert.That(shop.SalesItem, Is.EqualTo(new[] { 31, 32, 33 }));
            Assert.That(shop.BarterItem[0].GiveQuantity, Is.EqualTo(500));
        });
    }

    [Test]
    public void ASalesListCostsOneIntPerEntry_HoweverLargeTheShopfront()
    {
        // Why the sales table is numbers and not BarterItemRecord rows. The armory is 471 items; as barter
        // rows that is four ints and a hand-authored line each, and a shop panel rendering "give X → get Y"
        // for every one. As numbers it is a list, and the client prices it from definitions it already holds.
        var big = new ShopRecord { SalesItem = [.. Enumerable.Range(1, 200)] };
        big.Normalize(MaxItems);
        Assert.That(big.SalesItem, Has.Count.EqualTo(200), "no per-row ceiling like MaxTrades applies here");
    }
}
