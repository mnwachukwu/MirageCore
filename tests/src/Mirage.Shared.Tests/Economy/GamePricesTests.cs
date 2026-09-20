using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Economy;

/// <summary>
/// What the engine charges, which is only ever what a game told it to.
///
/// <para>🔴 <b>Zero is the answer nobody declared.</b> Every figure here defaults to nothing, and a
/// world that says nothing gets free postage, no tax, no sale, free repairs and no wait. That is the
/// engine by itself, and it has to stay coherent rather than throwing or charging a number Core
/// invented.</para>
///
/// <para>Both ends run this same arithmetic — the server to charge and the client to preview — so what
/// is pinned here is what a player is shown as much as what they pay.</para>
/// </summary>
[TestFixture]
public class GamePricesTests
{
    private static ItemRecord Sword(int price = 1000, short durability = 100) =>
        new() { Name = "Sword", Type = ItemType.Equipment, Price = price, Durability = durability };

    private static GamePrices Charging() => new()
    {
        MailBaseCost = 10,
        MailAttachmentCost = 50,
        MailValuePercent = 2,
        MarketTaxPercent = 5,
        SellBackPercent = 25,
        RepairPercent = 20,
    };

    // ── Declaring nothing ─────────────────────────────────────────────────────

    [Test]
    public void AnEngineWithNoGameLoaded_ChargesNothing()
    {
        var free = GamePrices.Free;
        var sword = Sword();

        Assert.Multiple(() =>
        {
            Assert.That(free.GuildCost, Is.Zero);
            Assert.That(free.MailSendCost(attachments: 3, attachedValue: 10_000), Is.Zero);
            Assert.That(free.MarketTax(1_000), Is.Zero);
            Assert.That(free.SellValue(sword, currentDurability: 100), Is.Zero, "a shop that buys nothing back");
            Assert.That(free.RepairCost(50, sword), Is.Zero, "mending is free");
        });
    }

    /// <summary>A free repair covers the whole piece rather than none of it — the shop's partial offer
    /// has to read as "all of it", not as an empty one.</summary>
    [Test]
    public void WithNoRepairShare_ThePurseCoversTheWholePiece()
    {
        Assert.That(GamePrices.Free.RepairPointsAffordable(0, Sword()), Is.EqualTo(100));
    }

    // ── Postage ───────────────────────────────────────────────────────────────

    [Test]
    public void PostageIsTheBasePlusEachAttachmentPlusAShareOfTheParcel()
    {
        var prices = Charging();

        Assert.Multiple(() =>
        {
            Assert.That(prices.MailSendCost(0), Is.EqualTo(10), "an empty letter is the base fee");
            Assert.That(prices.MailSendCost(2), Is.EqualTo(10 + 100));
            Assert.That(prices.MailSendCost(1, attachedValue: 5_000), Is.EqualTo(10 + 50 + 100));
        });
    }

    /// <summary>What is in the parcel is its authored price times how many, which is the same answer in
    /// every world — no rate is involved.</summary>
    [Test]
    public void AParcelIsWorthWhatIsInIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GamePrices.AttachmentValue(quantity: 7, unitPrice: 30), Is.EqualTo(210));
            Assert.That(GamePrices.AttachmentValue(quantity: -1, unitPrice: 30), Is.Zero);
        });
    }

    // ── Selling back ──────────────────────────────────────────────────────────

    [Test]
    public void AShopPaysAShareOfThePriceScaledByCondition()
    {
        var prices = Charging();
        var sword = Sword(price: 1000, durability: 100);

        Assert.Multiple(() =>
        {
            Assert.That(prices.SellValue(sword, 100), Is.EqualTo(250), "a quarter of 1000, pristine");
            Assert.That(prices.SellValue(sword, 50), Is.EqualTo(125), "half worn is half of that");
            Assert.That(prices.SellValue(sword, 0), Is.Zero, "broken is scrap");
        });
    }

    /// <summary>An item nobody priced is worth nothing, which is how the money item stays unsellable
    /// without a rule about the money item.</summary>
    [Test]
    public void AnUnpricedItemIsWorthNothing()
    {
        Assert.That(Charging().SellValue(Sword(price: 0), 100), Is.Zero);
    }

    // ── Mending ───────────────────────────────────────────────────────────────

    [Test]
    public void AWholeRepairCostsThatShareOfThePrice()
    {
        var prices = Charging();
        var sword = Sword(price: 1000, durability: 100);

        Assert.Multiple(() =>
        {
            Assert.That(prices.RepairCost(100, sword), Is.EqualTo(200), "a fifth of 1000");
            Assert.That(prices.RepairCost(50, sword), Is.EqualTo(100), "half the job, half the bill");
            Assert.That(prices.RepairCost(0, sword), Is.Zero);
            Assert.That(prices.RepairCost(500, sword), Is.EqualTo(200), "points past the maximum are not charged");
        });
    }

    /// <summary>🔴 Asked exactly rather than by dividing the purse by a display rate: rounding the other
    /// way names a point count that costs a coin more than the player actually has, and the shop then
    /// refuses its own offer.</summary>
    [Test]
    public void ThePartialOfferIsWhatThePurseActuallyCovers()
    {
        var prices = Charging();
        var sword = Sword(price: 1000, durability: 100);

        int points = prices.RepairPointsAffordable(gold: 55, item: sword);

        Assert.Multiple(() =>
        {
            Assert.That(points, Is.EqualTo(27));
            Assert.That(prices.RepairCost(points, sword), Is.LessThanOrEqualTo(55));
            Assert.That(prices.RepairCost(points + 1, sword), Is.GreaterThan(55));
        });
    }

    [Test]
    public void AnEmptyPurseMendsNothing()
    {
        Assert.That(Charging().RepairPointsAffordable(0, Sword()), Is.Zero);
    }

    // ── The marketplace ───────────────────────────────────────────────────────

    [Test]
    public void TheSaleTaxIsAShareOfThePriceAndFloors()
    {
        var prices = Charging();

        Assert.Multiple(() =>
        {
            Assert.That(prices.MarketTax(100), Is.EqualTo(5));
            Assert.That(prices.MarketTax(99), Is.EqualTo(4), "5% of 99 is 4.95, and the tax floors");
            Assert.That(prices.MarketTax(0), Is.Zero);
        });
    }
}
