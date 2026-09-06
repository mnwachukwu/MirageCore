using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Economy;

/// <summary>The stored <see cref="ItemRecord.Price"/> and the <see cref="ItemRecord.NonJunkable"/> flag:
/// what <see cref="ItemRecord.Normalize"/> is and is not allowed to do to a price, that the value survives
/// the packet round-trip, and that the field is wide enough for the ladder it has to carry.</summary>
[TestFixture]
public class ItemPriceTests
{
    [Test]
    public void Price_IsWideEnoughForTheTopOfTheLadder()
    {
        // The reason the field is int and not short like every other type-specific field here. A short
        // wraps at 32,767, which is passed before level 60 — so the whole upper ladder would be corrupt
        // and nothing would report it. Asserted against the real formula so it tracks any retune.
        var top = new ItemRecord
        {
            Name = "top", Type = ItemType.Weapon, LevelReq = Constants.MaxLevel,
            Power = (short)EconomyFormulas.ReferencePower(Constants.MaxLevel), Durability = 50,
        };
        Assert.That(EconomyFormulas.ItemValue(top), Is.GreaterThan(short.MaxValue),
            "the top of the ladder must exceed a short, or the int is unjustified");
    }

    [Test]
    public void Normalize_KeepsAnAuthoredPriceOnEveryType_IncludingCurrency()
    {
        // The casting reagent is typed Currency so that it STACKS, but it is a consumable good a spell
        // vendor sells for gold. Normalize must leave its authored price alone or the shop row it appears
        // in is permanently "not for sale".
        var reagent = new ItemRecord { Name = "Magical Reagent", Type = ItemType.Currency, Price = 1 };
        reagent.Normalize();
        Assert.That(reagent.Price, Is.EqualTo(1), "a reagent is bought at a counter like anything else");

        // Gold is protected by the two rules that actually matter rather than by a schema ban: nothing
        // derives a worth for it, so a re-seed never writes one, and the purchase path refuses a zero.
        var gold = new ItemRecord { Name = "Gold", Type = ItemType.Currency };
        gold.Normalize();
        Assert.That(gold.Price, Is.Zero, "gold has no price in gold");
        Assert.That(EconomyFormulas.ItemValue(gold), Is.Zero, "and nothing may derive one for it");

        // A KEY keeps its price even though the formula declines to derive one — "cannot be derived" and
        // "cannot exist" are different claims, and treasure lives in that gap.
        var key = new ItemRecord { Name = "Ruby Pendant", Type = ItemType.Key, Price = 25_000 };
        key.Normalize();
        Assert.That(key.Price, Is.EqualTo(25_000), "an authored price must survive Normalize");
        Assert.That(EconomyFormulas.ItemValue(key), Is.Zero, "...even though nothing derives it");
    }

    [Test]
    public void Normalize_NeverRecomputesAnAuthoredPrice()
    {
        // Normalize runs on every editor save. If it recomputed, a deliberate override would be erased the
        // next time anyone touched the item in the editor — silently, and only for the items that matter.
        var weapon = new ItemRecord
        {
            Name = "Oddly Cheap Sword", Type = ItemType.Weapon, LevelReq = 100, Power = 127, Durability = 50,
            Price = 1,
        };
        weapon.Normalize();
        Assert.That(weapon.Price, Is.EqualTo(1));
        Assert.That(EconomyFormulas.ItemValue(weapon), Is.GreaterThan(1), "the formula disagrees, and loses");
    }

    [Test]
    public void NormalizeKeepsAnAuthoredPrice_OnEveryType()
    {
        // Normalize never zeroes a price, whatever the type. Currency covers gold AND the casting reagent
        // a spell vendor sells; None is TREASURE's type, whose whole substance is its price. A type-level
        // bar on either silently deletes authored data.
        Assert.Multiple(() =>
        {
            foreach (var t in Enum.GetValues<ItemType>())
            {
                var item = new ItemRecord { Name = $"{t} probe", Type = t, Price = 77 };
                item.Normalize();
                Assert.That(item.Price, Is.EqualTo(77), $"{t} must keep an authored price");
            }
        });
    }

    [Test]
    public void TreasureSurvivesNormalize_WithNothingButANameAndAPrice()
    {
        // The whole treasure contract in one assertion. Typed None so it carries no stats, no level gate
        // and no use; Normalize must leave the price and the flag standing rather than treating the record
        // as blank, and ItemValue must decline to derive over the top of an authored worth.
        var gem = new ItemRecord
        {
            Name = "Jade Seal", Type = ItemType.None, Price = 461, NonJunkable = true,
            // Junk left over from whatever this row used to be — Normalize should strip all of it.
            Durability = 100, Power = 40, LevelReq = 15, VitalAmount = 9, SpellNum = 3,
        };

        gem.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(gem.Price, Is.EqualTo(461), "an authored worth survives");
            Assert.That(gem.NonJunkable, Is.True, "and so does the flag that protects it from the junk dump");
            Assert.That(EconomyFormulas.ItemValue(gem), Is.Zero, "the formula declines to price it");
            Assert.That(gem.Durability, Is.Zero);
            Assert.That(gem.Power, Is.Zero);
            Assert.That(gem.LevelReq, Is.Zero, "treasure is not gated — a gem is worth what it is worth");
            Assert.That(gem.VitalAmount, Is.Zero);
            Assert.That(gem.SpellNum, Is.Zero);
        });
    }

    [Test]
    public void PriceAndNonJunkable_SurviveTheItemPackets()
    {
        var item = new ItemRecord
        {
            Name = "Ruby Pendant", Type = ItemType.None, Price = 25_000, NonJunkable = true,
        };

        var update = PacketBuilder.UpdateItem(7, item);
        Assert.Multiple(() =>
        {
            Assert.That(update.Price, Is.EqualTo(25_000));
            Assert.That(update.NonJunkable, Is.True);
        });

        var bulk = PacketBuilder.SendItems([(7, item)]);
        Assert.Multiple(() =>
        {
            Assert.That(bulk.Items[0].Price, Is.EqualTo(25_000), "the bulk definition push carries it too");
            Assert.That(bulk.Items[0].NonJunkable, Is.True);
        });
    }

    [Test]
    public void SellValue_IsWorseThanThePlayerMarket_ByDesign()
    {
        // The 25% rate is what keeps a universal buyer a price FLOOR rather than a competitor: any player
        // offering more than a quarter wins the sale. Raising it quietly kills the player economy.
        var item = new ItemRecord
        {
            Name = "kit piece", Type = ItemType.Armor, LevelReq = 120,
            Power = (short)EconomyFormulas.ReferencePower(120), Durability = 50,
        };
        int price = EconomyFormulas.ItemValue(item);
        Assert.That(EconomyFormulas.ItemSellValue(item, item.Durability), Is.LessThan(price / 3),
            "a shop must pay well under a third, or vendoring beats trading");
    }
}
