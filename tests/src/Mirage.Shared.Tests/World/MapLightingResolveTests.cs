using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>
/// The lighting override, which is the one inheritable property that does NOT resolve field-by-field.
///
/// <para>AlwaysLit and AlwaysDark are stored as two nullable bools but mean one three-valued thing. Resolving
/// each on its own with the usual <c>map ?? group ?? false</c> would let a map that says "lit" and a group
/// that says "dark" both come out true, and nothing downstream could tell which the author meant — so they
/// resolve together, and every combination below has exactly one answer.</para>
///
/// <para>Lighting is authored, and independent of Moral: a town can be dark, and a lit map need not be safe.</para>
/// </summary>
[TestFixture]
public class MapLightingResolveTests
{
    private static MapRecord Map(bool? lit = null, bool? dark = null) =>
        new() { AlwaysLit = lit, AlwaysDark = dark };

    private static MapGroupRecord Group(bool? lit = null, bool? dark = null) =>
        new() { AlwaysLit = lit, AlwaysDark = dark };

    [Test]
    public void NothingAsserted_LightsByTimeOfDay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(), Group()), Is.EqualTo(MapLighting.Normal));
            Assert.That(MapGroupResolve.Lighting(Map(), null), Is.EqualTo(MapLighting.Normal),
                "a map in no group at all");
        });
    }

    [Test]
    public void AGroupsAssertionIsInherited()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(), Group(lit: true)), Is.EqualTo(MapLighting.AlwaysLit));
            Assert.That(MapGroupResolve.Lighting(Map(), Group(dark: true)), Is.EqualTo(MapLighting.AlwaysDark));
        });
    }

    [Test]
    public void AMapsOwnAssertionWins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(lit: true), Group()), Is.EqualTo(MapLighting.AlwaysLit));
            Assert.That(MapGroupResolve.Lighting(Map(dark: true), Group()), Is.EqualTo(MapLighting.AlwaysDark));
        });
    }

    /// <summary>The case two independent resolves would get wrong: the map and its group assert OPPOSITE
    /// overrides. Field-by-field, both would read true and the map would be lit and dark at once.</summary>
    [Test]
    public void AMapOverridesItsGroupsOppositeAssertion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(lit: true), Group(dark: true)), Is.EqualTo(MapLighting.AlwaysLit),
                "a lit map inside a dark group is lit");
            Assert.That(MapGroupResolve.Lighting(Map(dark: true), Group(lit: true)), Is.EqualTo(MapLighting.AlwaysDark),
                "and a dark map inside a lit group is dark");
        });
    }

    /// <summary>Unchecking a box is how a map opts out of a flag its group asserts — that is what the third
    /// tri-state value is for. It must not also cancel the OTHER flag, which the author said nothing about.</summary>
    [Test]
    public void AnExplicitFalseOnTheMapDeclinesOnlyThatFlag()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(lit: false), Group(lit: true)), Is.EqualTo(MapLighting.Normal),
                "the map refuses the group's lit");
            Assert.That(MapGroupResolve.Lighting(Map(dark: false), Group(dark: true)), Is.EqualTo(MapLighting.Normal),
                "and refuses the group's dark");
            Assert.That(MapGroupResolve.Lighting(Map(lit: false), Group(dark: true)), Is.EqualTo(MapLighting.AlwaysDark),
                "refusing lit says nothing about dark, so the group's dark still stands");
        });
    }

    /// <summary>The editor makes the two boxes exclusive, but records are files and files get hand-edited.
    /// Lit wins: a map stuck bright is a complaint, a map stuck black is unplayable.</summary>
    [Test]
    public void BothAssertedAtOnce_ResolvesToLit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(Map(lit: true, dark: true), null), Is.EqualTo(MapLighting.AlwaysLit));
            Assert.That(MapGroupResolve.Lighting(Map(), Group(lit: true, dark: true)), Is.EqualTo(MapLighting.AlwaysLit));
        });
    }

    /// <summary>Moral is orthogonal: it decides whether you can be attacked, not whether you can see.</summary>
    [Test]
    public void MoralDoesNotDecideLighting()
    {
        var safe = new MapRecord { Moral = MapMoral.Safe };
        var lethalButLit = new MapRecord { Moral = MapMoral.None, AlwaysLit = true };

        Assert.Multiple(() =>
        {
            Assert.That(MapGroupResolve.Lighting(safe, null), Is.EqualTo(MapLighting.Normal),
                "a safe zone lights by time of day like anywhere else");
            Assert.That(MapGroupResolve.Lighting(lethalButLit, null), Is.EqualTo(MapLighting.AlwaysLit),
                "and a lit map need not be safe");
        });
    }
}
