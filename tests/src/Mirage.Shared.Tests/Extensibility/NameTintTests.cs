using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// What a game says a creature IS, and how that reaches the color of its name.
///
/// <para>🔴 Core's behaviors describe LOCOMOTION and nothing else, so a caster holding its distance and
/// a deer holding its distance are the same <c>Shadow</c>. Anything that colors a name off a behavior is
/// therefore colorless where it matters most — at the body the player most needs warning about. The
/// disposition lives in the game's own attributes, and these pin the projection from those to a
/// color.</para>
///
/// <para>A tint is a VIEW of an attribute, never a second copy, for the same reason an overhead bar is:
/// a color a game could set independently could be set to something the body does not say, and a name
/// disagreeing with the creature under it cannot be diagnosed from the screen.</para>
/// </summary>
[TestFixture]
public class NameTintTests
{
    private const int Yellow = 0xFFFF00;
    private const int White = 0xFFFFFF;
    private const int Green = 0x00FF00;

    /// <summary>A tint only colors anything if onlookers are told the attribute, so every module here
    /// declares its keys to the viewport the way a real one has to.</summary>
    private sealed class Tints(int otherwise, params NameTint[] tints) : ICoreModule
    {
        /// <summary>Settable, because the one-module rule keys on this: a game restating its own answer
        /// is fine and two games disagreeing is not, and those are the same call from here.</summary>
        public string Name { get; init; } = "Tints";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var tint in tints)
                builder.Attributes.Declare(tint.Key, AttributeVisibility.Viewport);

            foreach (var tint in tints) builder.AddNameTint(tint);
            if (otherwise != NameTintSet.PlainRgb)
                builder.SetNameColors(otherwise, NameTintSet.MarkedDefaultRgb, NameTintSet.AggressorDefaultRgb);
        }
    }

    private static NameTint Tint(string key, int rgb = 0, int ordinal = 0)
        => new() { Key = key, Rgb = rgb, Ordinal = ordinal };

    // ── Declaring ─────────────────────────────────────────────────────────────

    [Test]
    public void AnEngineWithNoGameLoaded_NamesEverythingPlainly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CoreRegistry.CoreOnly.NameTints.Tints, Is.Empty);
            Assert.That(CoreRegistry.CoreOnly.NameTints.RgbFor(new AttributeBag()), Is.EqualTo(NameTintSet.PlainRgb));
        });
    }

    [Test]
    public void TheTintsAreAskedInDeclaredOrder()
    {
        var registry = CoreRegistry.Build(new Tints(Green, Tint("hostile", ordinal: 1), Tint("guard", ordinal: 0)));

        Assert.That(registry.NameTints.Tints.Select(t => t.Key), Is.EqualTo(new[] { "guard", "hostile" }));
    }

    [Test]
    public void TwoTintsOnOneAttributeAreRefused()
        => Assert.That(() => CoreRegistry.Build(new Tints(Green, Tint("guard", Yellow), Tint("guard", White))),
                       Throws.TypeOf<CoreModuleException>());

    [Test]
    public void ATintNamingNoAttributeIsRefused()
        => Assert.That(() => CoreRegistry.Build(new Tints(Green, Tint(""))),
                       Throws.TypeOf<CoreModuleException>());

    /// <summary>⚠ The half that fails silently. A tint whose attribute never reaches an onlooker colors
    /// nothing and reports nothing: every creature comes out the plain color, which looks like a world
    /// that simply chose not to use the feature.</summary>
    [Test]
    public void ATintOnAnAttributeOnlookersCannotSee_IsRefusedAtLoad()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(new Undeclared()));

        Assert.That(ex!.Message, Does.Contain("guard"));
    }

    private sealed class Undeclared : ICoreModule
    {
        public string Name => "Undeclared";

        public void Configure(ICoreBuilder builder)
        {
            builder.Attributes.Declare("guard", AttributeVisibility.Owner);
            builder.AddNameTint(new NameTint { Key = "guard", Rgb = Yellow });
        }
    }

    [Test]
    public void OneMoreTintThanAllowedIsRefused_NamingTheModuleAndTheLimit()
    {
        var tints = Enumerable.Range(0, NameTintSet.Max + 1).Select(i => Tint("k" + i)).ToArray();

        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(new Tints(Green, tints)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ModuleName, Is.EqualTo("Tints"));
            Assert.That(ex.Message, Does.Contain(NameTintSet.Max.ToString()));
        });
    }

    /// <summary>Which module won would otherwise depend on load order, and nothing would say so.</summary>
    [Test]
    public void TwoModulesSettingTheNameColors_AreRefused()
        => Assert.That(() => CoreRegistry.Build(new Tints(Green) { Name = "First" },
                                                new Tints(Yellow) { Name = "Second" }),
                       Throws.TypeOf<CoreModuleException>());

    /// <summary>⚠ But ONE module may restate them. A scripting layer offers the three colors as three
    /// separate calls, and each arrives here carrying the whole answer.</summary>
    [Test]
    public void OneModuleRestatingThem_IsFine()
    {
        var registry = CoreRegistry.Build(new Restater());

        Assert.Multiple(() =>
        {
            Assert.That(registry.NameTints.OtherwiseRgb, Is.EqualTo(Green));
            Assert.That(registry.NameTints.MarkedRgb, Is.EqualTo(Yellow), "the last word on each one wins");
        });
    }

    private sealed class Restater : ICoreModule
    {
        public string Name => "Restater";

        public void Configure(ICoreBuilder builder)
        {
            builder.SetNameColors(Green, NameTintSet.MarkedDefaultRgb, NameTintSet.AggressorDefaultRgb);
            builder.SetNameColors(Green, Yellow, NameTintSet.AggressorDefaultRgb);
        }
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Test]
    public void ABodyCarryingATintedAttribute_IsNamedInThatColor()
    {
        var set = new NameTintSet([Tint("guard", Yellow)], Green);

        Assert.That(set.RgbFor(new AttributeBag().Set("guard", true)), Is.EqualTo(Yellow));
    }

    [Test]
    public void ABodyCarryingNone_IsNamedInThePlainColor()
        => Assert.That(new NameTintSet([Tint("guard", Yellow)], Green).RgbFor(new AttributeBag()),
                       Is.EqualTo(Green));

    /// <summary>A creature is not a guard because it has a guard key reading false. Reproducing the
    /// original's rule depends on this: every one of its 177 creatures carries all three flags, and all
    /// but a handful of them are false.</summary>
    [Test]
    public void AnAttributeReadingFalse_DoesNotTint()
        => Assert.That(new NameTintSet([Tint("guard", Yellow)], Green).RgbFor(new AttributeBag().Set("guard", false)),
                       Is.EqualTo(Green));

    /// <summary>⚠ The first match wins, so a body carrying both is named by whichever was declared
    /// first. A game that wants its guards picked out of its hostiles orders them that way.</summary>
    [Test]
    public void ABodyCarryingTwo_IsNamedByTheOneAskedFirst()
    {
        var set = new NameTintSet([Tint("guard", Yellow, 0), Tint("hostile", White, 1)], Green);

        Assert.That(set.RgbFor(new AttributeBag().Set("guard", true).Set("hostile", true)), Is.EqualTo(Yellow));
    }

    [Test]
    public void ABodyWithNoValuesAtAll_IsNamedInThePlainColor()
        => Assert.That(new NameTintSet([Tint("guard", Yellow)], Green).RgbFor(null), Is.EqualTo(Green));

    /// <summary>Whatever a game happens to write the flag as. An author who typed 1, or a record that
    /// carries the word, means the same thing as one that carries true.</summary>
    [Test]
    public void AnyTruthyValueTints([ValueSource(nameof(TruthyValues))] AttributeValue value)
        => Assert.That(new NameTintSet([Tint("guard", Yellow)], Green)
                           .RgbFor(new AttributeBag().Set("guard", value)),
                       Is.EqualTo(Yellow));

    private static IEnumerable<AttributeValue> TruthyValues =>
    [
        AttributeValue.From(true),
        AttributeValue.From(1),
        AttributeValue.From(2.5),
        AttributeValue.From("yes"),
    ];

    /// <summary>"0", "no" and "false" are the ways a text value says no, and a creature carrying one of
    /// them is no more a guard than one carrying nothing.</summary>
    [TestCase("false")]
    [TestCase("no")]
    [TestCase("0")]
    [TestCase("")]
    public void TextThatSpellsNo_DoesNotTint(string value)
        => Assert.That(new NameTintSet([Tint("guard", Yellow)], Green)
                           .RgbFor(new AttributeBag().Set("guard", value)),
                       Is.EqualTo(Green));

    // ── The original's rule ───────────────────────────────────────────────────

    /// <summary>🔴 The whole point, end to end: guards yellow, anything that fights white, everybody
    /// else green — three declarations, and the engine holds no opinion about any of them.</summary>
    [TestCase(false, false, Green, TestName = "a shopkeeper is green")]
    [TestCase(false, true, White, TestName = "a wolf is white")]
    [TestCase(true, false, Yellow, TestName = "a town guard is yellow")]
    [TestCase(true, true, Yellow, TestName = "a guard that fights is still yellow")]
    public void TheOriginalsThreeColors(bool guard, bool hostile, int expected)
    {
        var set = new NameTintSet([Tint("guard", Yellow, 0), Tint("hostile", White, 1)], Green);
        var bag = new AttributeBag().Set("guard", guard).Set("hostile", hostile);

        Assert.That(set.RgbFor(bag), Is.EqualTo(expected));
    }
}
