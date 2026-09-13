using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// What a game says to show, and what one body's values make of it.
///
/// <para>🔴 A declaration is resolved per body, so the same field is a row on one and nothing on
/// another — and "nothing" has to mean absent rather than blank. A row drawn with no value states
/// something false: that the body is at zero, or has no name, or is not in a guild. Every case below
/// is a way of having nothing to say, and they all answer the same.</para>
/// </summary>
[TestFixture]
public class DisplayFieldTests
{
    private sealed class Fields(params DisplayField[] fields) : ICoreModule
    {
        public string Name => "Fields";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var f in fields) builder.AddDisplayField(f);
        }
    }

    private static DisplayField Text(string key, string? label = null, int ordinal = 0)
        => new() { ValueKey = key, LabelKey = label, Ordinal = ordinal, Style = DisplayStyle.Text };

    private static DisplayField Meter(string key, int ordinal = 0)
        => new() { ValueKey = key, MaxKey = key + "Max", Ordinal = ordinal, Style = DisplayStyle.Meter };

    // ── Declaring ─────────────────────────────────────────────────────────────

    [Test]
    public void AnEngineWithNoGameLoaded_ShowsNothingAnywhere()
        => Assert.That(CoreRegistry.CoreOnly.DisplayFields.Fields, Is.Empty);

    [Test]
    public void FieldsComeBackInDeclaredOrder()
    {
        var set = CoreRegistry.Build(new Fields(Text("b", ordinal: 1), Text("a", ordinal: 0))).DisplayFields;

        Assert.That(set.For(DisplaySurfaces.Hud).Select(f => f.ValueKey), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void AFieldWithNoValueKeyIsRefused()
        => Assert.That(() => CoreRegistry.Build(new Fields(new DisplayField { ValueKey = "" })),
                       Throws.TypeOf<CoreModuleException>());

    /// <summary>A meter with no ceiling could only ever draw empty, which states that the body is at
    /// zero rather than that nobody said how big the bar is.</summary>
    [Test]
    public void AMeterWithNoMaximumIsRefused()
    {
        var bad = new DisplayField { ValueKey = "fuel", Style = DisplayStyle.Meter };

        Assert.That(() => CoreRegistry.Build(new Fields(bad)), Throws.TypeOf<CoreModuleException>());
    }

    /// <summary>A heading says something about the rows under it, so it is the one style with nothing
    /// to read.</summary>
    [Test]
    public void AHeadingNeedsNoValueKey()
    {
        var heading = new DisplayField { LabelKey = "hud.section", Style = DisplayStyle.Heading };

        Assert.That(CoreRegistry.Build(new Fields(heading)).DisplayFields.Count, Is.EqualTo(1));
    }

    [Test]
    public void ASurfaceAskedForByNobody_HasNothing()
    {
        var set = CoreRegistry.Build(new Fields(Text("gold"))).DisplayFields;

        Assert.That(set.For("tooltip"), Is.Empty);
    }

    // ── Projecting ────────────────────────────────────────────────────────────

    [Test]
    public void ATextRowReadsTheValueAsItIsWritten()
    {
        var bag = new AttributeBag().Set("title", "Harbourmaster");

        var row = Text("title", "hud.title").RowIn(bag);

        Assert.Multiple(() =>
        {
            Assert.That(row!.Value.Text, Is.EqualTo("Harbourmaster"));
            Assert.That(row.Value.LabelKey, Is.EqualTo("hud.title"));
            Assert.That(row.Value.Style, Is.EqualTo(DisplayStyle.Text));
        });
    }

    [Test]
    public void AMeterRowReadsItsValueAgainstItsMaximum()
    {
        var bag = new AttributeBag().Set("fuel", 30).Set("fuelMax", 120);

        var row = Meter("fuel").RowIn(bag);

        Assert.Multiple(() =>
        {
            Assert.That(row!.Value.Fill, Is.EqualTo(0.25).Within(0.0001));
            Assert.That(row.Value.Style, Is.EqualTo(DisplayStyle.Meter));
        });
    }

    /// <summary>Every way of having nothing to say gives the same answer, so a surface has one test
    /// rather than five.</summary>
    [TestCase("no value at all", false, false, 0)]
    [TestCase("a value and no maximum", true, false, 0)]
    [TestCase("a maximum of zero", true, true, 0)]
    [TestCase("a negative maximum", true, true, -5)]
    public void AMeterOnABodyWithNothingToSay_IsNotARow(string _, bool hasValue, bool hasMax, int max)
    {
        var bag = new AttributeBag();
        if (hasValue) bag.Set("fuel", 10);
        if (hasMax) bag.Set("fuelMax", max);

        Assert.That(Meter("fuel").RowIn(bag), Is.Null);
    }

    [Test]
    public void ABodyWithNoValuesAtAll_IsNotARow()
        => Assert.That(Text("gold").RowIn(null), Is.Null);

    /// <summary>🔴 A badge states something CURRENTLY true. A flag that is false is not a badge reading
    /// "false" — it is no badge, which is the whole difference between a status and a field.</summary>
    [Test]
    public void ABadgeOnAFalseFlag_IsNotARow()
    {
        var badge = new DisplayField { ValueKey = "wanted", LabelKey = "hud.wanted", Style = DisplayStyle.Badge };

        Assert.Multiple(() =>
        {
            Assert.That(badge.RowIn(new AttributeBag().Set("wanted", false)), Is.Null);
            Assert.That(badge.RowIn(new AttributeBag().Set("wanted", true))!.Value.Text, Is.EqualTo("hud.wanted"));
        });
    }

    [Test]
    public void ABadgeOnAnEmptyWord_IsNotARow()
    {
        var badge = new DisplayField { ValueKey = "state", Style = DisplayStyle.Badge };

        Assert.Multiple(() =>
        {
            Assert.That(badge.RowIn(new AttributeBag().Set("state", "")), Is.Null);
            Assert.That(badge.RowIn(new AttributeBag().Set("state", "asleep"))!.Value.Text, Is.EqualTo("asleep"));
        });
    }

    [Test]
    public void AHeadingIsDrawnWhateverTheBodyCarries()
    {
        var heading = new DisplayField { LabelKey = "hud.section", Style = DisplayStyle.Heading };

        Assert.That(heading.RowIn(null), Is.Not.Null);
    }

    // ── A whole surface ───────────────────────────────────────────────────────

    [Test]
    public void ASurfaceGetsOnlyTheRowsItShouldDraw()
    {
        var set = CoreRegistry.Build(new Fields(
            Text("title", ordinal: 0), Meter("fuel", ordinal: 1), Text("gold", ordinal: 2))).DisplayFields;
        var bag = new AttributeBag().Set("title", "Pilot").Set("gold", 40);

        var rows = set.Project(DisplaySurfaces.Hud, bag).Rows;

        Assert.That(rows.Select(r => r.Text), Is.EqualTo(new[] { "Pilot", "40" }),
            "the meter has no maximum on this body, so it is absent rather than empty");
    }

    [Test]
    public void ABodyThatFillsNothing_ProjectsToNothing()
    {
        var set = CoreRegistry.Build(new Fields(Text("gold"))).DisplayFields;

        Assert.That(set.Project(DisplaySurfaces.Hud, new AttributeBag()).IsEmpty, Is.True);
    }

    [Test]
    public void AGameThatDeclaredNothing_ProjectsToNothing()
        => Assert.That(DisplayFieldSet.Empty.Project(DisplaySurfaces.Hud, new AttributeBag().Set("x", 1)).IsEmpty,
                       Is.True);
}
