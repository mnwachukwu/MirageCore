using Mirage.Editor.Models;
using Mirage.Shared;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Models;

/// <summary>
/// The visibility mask itself: which of the fifteen art layers the canvas draws.
///
/// <para>Checked means visible, so the value has to read that way from the outside. Inside it stores the
/// HIDDEN set, which is what makes the default value mean everything showing — a mask of visible bits
/// would make a forgotten initializer blank the whole canvas, and nothing would say why.</para>
/// </summary>
[TestFixture]
public class LayerVisibilityTests
{
    /// <summary>The default value draws everything. This is the whole reason the stored bits are the
    /// hidden ones, so it is asserted rather than assumed.</summary>
    [Test]
    public void TheDefaultValueShowsEveryLayer()
    {
        LayerVisibility fresh = default;

        Assert.That(fresh.AllVisible, Is.True);
        foreach (var type in Enum.GetValues<LayerType>())
            for (int i = 0; i < Constants.MaxGroundLayers; i++)
                Assert.That(fresh.IsVisible(type, i), Is.True, $"{type} {i + 1}");
    }

    /// <summary>Each stack has its own bits: hiding a ground layer must not take the fringe layer that
    /// shares its number with it.</summary>
    [Test]
    public void HidingOneLayerLeavesTheSameNumberInEveryOtherStack()
    {
        var v = LayerVisibility.All.With(LayerType.Ground, 2, visible: false);

        Assert.That(v.IsVisible(LayerType.Ground, 2), Is.False);
        Assert.That(v.IsVisible(LayerType.Fringe, 2), Is.True);
        Assert.That(v.IsVisible(LayerType.Canopy, 2), Is.True);
        Assert.That(v.IsVisible(LayerType.Ground, 1), Is.True);
        Assert.That(v.IsVisible(LayerType.Ground, 3), Is.True);
    }

    [Test]
    public void HidingAStackTakesEveryLayerInItAndNoOthers()
    {
        var v = LayerVisibility.All.WithStack(LayerType.Fringe, visible: false);

        for (int i = 0; i < Constants.MaxFringeLayers; i++)
            Assert.That(v.IsVisible(LayerType.Fringe, i), Is.False, $"Fringe {i + 1}");
        for (int i = 0; i < Constants.MaxGroundLayers; i++)
            Assert.That(v.IsVisible(LayerType.Ground, i), Is.True, $"Ground {i + 1}");
    }

    /// <summary>The parent box is three-state. A stack with some of its layers put away must not read as
    /// either fully shown or fully hidden, or one click on the parent would silently take the rest.</summary>
    [Test]
    public void AStackWithSomeLayersHiddenReadsAsIndeterminate()
    {
        var all = LayerVisibility.All;
        var one = all.With(LayerType.Canopy, 0, visible: false);
        var none = all.WithStack(LayerType.Canopy, visible: false);

        Assert.That(all.StackState(LayerType.Canopy), Is.True);
        Assert.That(one.StackState(LayerType.Canopy), Is.Null);
        Assert.That(none.StackState(LayerType.Canopy), Is.False);
    }

    /// <summary>The renderer walks a layer span and asks for one stack's bits, so bit k of that answer has
    /// to be layer k of that stack and nothing else.</summary>
    [Test]
    public void TheRenderersBitsLineUpWithTheLayerIndices()
    {
        var v = LayerVisibility.All
            .With(LayerType.Fringe, 0, visible: false)
            .With(LayerType.Fringe, 3, visible: false);

        int bits = v.VisibleBits(LayerType.Fringe);

        Assert.That(bits & 1, Is.EqualTo(0), "fringe 1 is hidden");
        Assert.That(bits >> 1 & 1, Is.EqualTo(1), "fringe 2 is showing");
        Assert.That(bits >> 3 & 1, Is.EqualTo(0), "fringe 4 is hidden");
        Assert.That(bits >> 4 & 1, Is.EqualTo(1), "fringe 5 is showing");
    }

    /// <summary>Every stack's bits are read at its own offset, so a hidden canopy layer must not appear as
    /// a hidden ground layer to the renderer.</summary>
    [Test]
    public void EachStacksBitsAreReadFromItsOwnOffset()
    {
        var v = LayerVisibility.All.With(LayerType.Canopy, 0, visible: false);

        Assert.That(v.VisibleBits(LayerType.Ground) & 1, Is.EqualTo(1));
        Assert.That(v.VisibleBits(LayerType.Fringe) & 1, Is.EqualTo(1));
        Assert.That(v.VisibleBits(LayerType.Canopy) & 1, Is.EqualTo(0));
    }

    [Test]
    public void ShowAllAndHideAllAreExactOpposites()
    {
        Assert.That(LayerVisibility.ForAll(true).AllVisible, Is.True);
        Assert.That(LayerVisibility.ForAll(false).AnyHidden, Is.True);

        foreach (var type in Enum.GetValues<LayerType>())
            for (int i = 0; i < Constants.MaxGroundLayers; i++)
                Assert.That(LayerVisibility.Nothing.IsVisible(type, i), Is.False, $"{type} {i + 1}");
    }
}
