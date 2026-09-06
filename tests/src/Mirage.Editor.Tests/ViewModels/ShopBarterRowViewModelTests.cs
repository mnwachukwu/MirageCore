using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>Trade-slot quantity coercion — the authoritative editor-side backstop that keeps a shop trade
/// well-formed: an empty side carries 0, a non-currency item pins to exactly 1 (gear never stacks), and a
/// currency item allows 1..9999. The spinner Min/Max bounds track the item the same way.</summary>
[TestFixture]
public class ShopBarterRowViewModelTests
{
    const int Gold = 1;    // the test predicate treats this id as currency
    const int Sword = 2;   // non-currency

    static ShopBarterRowViewModel Trade(BarterItemRecord? r = null) =>
        new(1, r ?? new BarterItemRecord(), () => [], id => id == Gold);

    [Test]
    public void NonCurrencyGive_PinsQuantityToOne()
    {
        var t = Trade();
        t.GiveItem = Sword;
        t.GiveQuantity = 5;      // a sword never stacks
        Assert.That(t.GiveQuantity, Is.EqualTo(1));
    }

    [Test]
    public void CurrencyGive_AllowsQuantityAboveOne()
    {
        var t = Trade();
        t.GiveItem = Gold;
        t.GiveQuantity = 100;
        Assert.That(t.GiveQuantity, Is.EqualTo(100));
    }

    [Test]
    public void ClearingTheItem_ZeroesQuantity()
    {
        var t = Trade(new BarterItemRecord { GiveItem = Gold, GiveQuantity = 50 });
        t.GiveItem = 0;
        Assert.That(t.GiveQuantity, Is.EqualTo(0), "an empty side carries no quantity");
    }

    // Coercion applies symmetrically to the "get" side.
    [Test]
    public void GetSide_CoercesToo()
    {
        var t = Trade();
        t.GetItem = Sword;
        t.GetQuantity = 9;
        Assert.That(t.GetQuantity, Is.EqualTo(1));
    }

    [Test]
    public void ValueBounds_TrackTheItem()
    {
        var t = Trade();
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveQuantityMin, Is.EqualTo(0), "empty side: 0..0");
            Assert.That(t.GiveQuantityMax, Is.EqualTo(0));
        });

        t.GiveItem = Sword;
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveQuantityMin, Is.EqualTo(1), "non-currency: exactly 1");
            Assert.That(t.GiveQuantityMax, Is.EqualTo(1));
        });

        t.GiveItem = Gold;
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveQuantityMin, Is.EqualTo(1));
            Assert.That(t.GiveQuantityMax, Is.EqualTo(9999), "currency: up to 9999");
        });
    }

    // The ctor seeds raw fields (no coercion), so a well-formed record round-trips untouched.
    [Test]
    public void ToRecord_RoundTripsAllFourFields()
    {
        var t = Trade(new BarterItemRecord { GiveItem = Gold, GiveQuantity = 100, GetItem = Sword, GetQuantity = 1 });
        var r = t.ToRecord();
        Assert.That((r.GiveItem, r.GiveQuantity, r.GetItem, r.GetQuantity), Is.EqualTo((Gold, 100, Sword, 1)));
    }
}
