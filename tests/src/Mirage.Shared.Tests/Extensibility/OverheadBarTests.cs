using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// What a game says goes over a body's head, and how a row reads itself out of that body's values.
///
/// <para>🔴 A bar is a VIEW of an attribute, never a second copy of it. The whole reason there is no bar
/// state to set is that a game that could set one could set it to something the attribute does not say —
/// and a bar showing a number nothing else agrees with is a bug nobody can find from the screen. So the
/// projection is pinned here rather than the storage.</para>
/// </summary>
[TestFixture]
public class OverheadBarTests
{
    private sealed class Bars(params OverheadBar[] bars) : ICoreModule
    {
        public string Name => "Bars";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var bar in bars) builder.AddOverheadBar(bar);
        }
    }

    private static OverheadBar Bar(string value, string max = "", int rgb = 0, int ordinal = 0)
        => new() { ValueKey = value, MaxKey = max.Length > 0 ? max : value + "Max", Rgb = rgb, Ordinal = ordinal };

    // ── Declaring ─────────────────────────────────────────────────────────────

    [Test]
    public void AnEngineWithNoGameLoaded_DrawsNothingOverAnybody()
        => Assert.That(CoreRegistry.CoreOnly.OverheadBars.Bars, Is.Empty);

    [Test]
    public void TheBarsComeBackInDeclaredOrder()
    {
        var registry = CoreRegistry.Build(new Bars(Bar("fuel", ordinal: 1), Bar("hull", ordinal: 0)));

        Assert.That(registry.OverheadBars.Bars.Select(b => b.ValueKey), Is.EqualTo(new[] { "hull", "fuel" }));
    }

    /// <summary>A fourth would push a name off the top of the viewport and be unreadable anyway, so it is
    /// refused at load rather than dropped at draw — a bar that silently never appears is a game bug that
    /// looks like a rendering bug.</summary>
    [Test]
    public void AFourthBarIsRefused_NamingTheModuleAndTheLimit()
    {
        var ex = Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Bars(Bar("a"), Bar("b"), Bar("c"), Bar("d"))));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ModuleName, Is.EqualTo("Bars"));
            Assert.That(ex.Message, Does.Contain("d").And.Contain(OverheadBarSet.Max.ToString()));
        });
    }

    [Test]
    public void ThreeBarsAreFine()
        => Assert.That(CoreRegistry.Build(new Bars(Bar("a"), Bar("b"), Bar("c"))).OverheadBars.Count,
                       Is.EqualTo(OverheadBarSet.Max));

    /// <summary>Two rows reading the same value would draw the same bar twice, which is never what was
    /// meant and costs one of the three.</summary>
    [Test]
    public void TwoBarsOnOneValueAreRefused()
        => Assert.That(() => CoreRegistry.Build(new Bars(Bar("hull"), Bar("hull", max: "shield"))),
                       Throws.TypeOf<CoreModuleException>());

    [Test]
    public void ABarWithNoMaximumIsRefused()
    {
        var bar = new OverheadBar { ValueKey = "hull", MaxKey = "" };

        Assert.That(() => CoreRegistry.Build(new Bars(bar)), Throws.TypeOf<CoreModuleException>());
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Test]
    public void ARowIsTheValueOverTheMaximum()
    {
        var bag = new AttributeBag().Set("hull", 30).Set("hullMax", 120);

        Assert.That(Bar("hull").FractionIn(bag), Is.EqualTo(0.25f).Within(0.0001f));
    }

    /// <summary>A game that overheals for a moment has a full bar, not a bar that vanishes.</summary>
    [Test]
    public void AValueOverTheMaximumClampsFull()
    {
        var bag = new AttributeBag().Set("hull", 200).Set("hullMax", 120);

        Assert.That(Bar("hull").FractionIn(bag), Is.EqualTo(1f));
    }

    [Test]
    public void AnIntegerAndARealReadTheSame()
    {
        var whole = new AttributeBag().Set("hull", 3).Set("hullMax", 4);
        var real = new AttributeBag().Set("hull", 3.0).Set("hullMax", 4.0);

        Assert.That(Bar("hull").FractionIn(whole), Is.EqualTo(Bar("hull").FractionIn(real)));
    }

    /// <summary>Every way of having nothing to say gives the same answer, so the draw layer has one test
    /// rather than four.</summary>
    [TestCase("nothing at all", false, false, 0)]
    [TestCase("a value and no maximum", true, false, 0)]
    [TestCase("a maximum of zero", true, true, 0)]
    [TestCase("a negative maximum", true, true, -5)]
    public void ABodyWithNothingToSay_ShowsNoRow(string _, bool hasValue, bool hasMax, int max)
    {
        var bag = new AttributeBag();
        if (hasValue) bag.Set("hull", 10);
        if (hasMax) bag.Set("hullMax", max);

        Assert.That(Bar("hull").FractionIn(bag), Is.LessThan(0f));
    }

    [Test]
    public void ABodyWithNoValuesAtAll_ShowsNoRow()
        => Assert.That(Bar("hull").FractionIn(null), Is.EqualTo(OverheadBar.Absent));

    // ── The set ───────────────────────────────────────────────────────────────

    /// <summary>A renderer walks three fixed positions against a set that may hold fewer, so asking past
    /// the end has to be an ordinary answer rather than a throw.</summary>
    [Test]
    public void AskingPastTheEndOfTheSetGivesNothing()
    {
        var bars = CoreRegistry.Build(new Bars(Bar("hull"))).OverheadBars;

        Assert.Multiple(() =>
        {
            Assert.That(bars.At(0), Is.Not.Null);
            Assert.That(bars.At(1), Is.Null);
            Assert.That(bars.At(OverheadBarSet.Max - 1), Is.Null);
            Assert.That(bars.At(-1), Is.Null);
        });
    }
}
