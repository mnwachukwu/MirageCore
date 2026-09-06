using Mirage.Editor.Controls;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Controls;

/// <summary>
/// The rule that keeps the World Preview looking like map art rather than a thumbnail.
///
/// <para>Each map is cached at a power-of-two scale instead of at native size, which is what makes a reach of
/// ten maps in every direction affordable. The whole bargain rests on the cached scale never falling below
/// the zoom: one step under and every map on screen is drawn from fewer pixels than it occupies, which is
/// exactly the soft, upscaled look the cache exists to avoid — and it would look like a rendering bug, not
/// like a memory decision.</para>
/// </summary>
[TestFixture]
public class WorldPreviewScaleTests
{
    /// <summary>The cached scale is never coarser than what is on screen, at any zoom the control allows.</summary>
    [Test]
    public void TheCachedScaleIsNeverBelowTheZoom()
    {
        for (double zoom = 0.05; zoom <= 1.0; zoom += 0.01)
        {
            double bucket = WorldPreviewControl.ScaleBucketFor(zoom);
            Assert.That(bucket, Is.GreaterThanOrEqualTo(zoom - 1e-9),
                $"zoom {zoom:0.###} would be drawn from a {bucket:0.####} bitmap and go soft");
        }
    }

    /// <summary>Native resolution is the ceiling. Rendering a map larger than it is buys nothing, and at the
    /// widest reach it is the difference between a few megabytes of surfaces and a few hundred.</summary>
    [Test]
    public void TheCachedScaleNeverExceedsNative()
    {
        foreach (double zoom in new[] { 0.05, 0.1, 0.25, 0.5, 0.99, 1.0 })
            Assert.That(WorldPreviewControl.ScaleBucketFor(zoom), Is.LessThanOrEqualTo(1.0));
    }

    /// <summary>Scales step in powers of two, so zooming through a range re-renders a handful of times rather
    /// than on every frame. Without this the cache would be rebuilt continuously during a pinch.</summary>
    [Test]
    public void TheScaleTakesOnlyPowerOfTwoValues()
    {
        double[] allowed = [1.0 / 16, 1.0 / 8, 1.0 / 4, 1.0 / 2, 1.0];

        for (double zoom = 0.05; zoom <= 1.0; zoom += 0.01)
            Assert.That(allowed, Has.Some.EqualTo(WorldPreviewControl.ScaleBucketFor(zoom)).Within(1e-9),
                $"zoom {zoom:0.###} produced an off-ladder scale");
    }

    /// <summary>A zoom at or below the smallest step still gets the smallest step, not something smaller,
    /// so the floor of the ladder is a real floor.</summary>
    [Test]
    public void TheSmallestZoomGetsTheSmallestStep()
    {
        Assert.That(WorldPreviewControl.ScaleBucketFor(0.05), Is.EqualTo(1.0 / 16).Within(1e-9));
        Assert.That(WorldPreviewControl.ScaleBucketFor(0.001), Is.EqualTo(1.0 / 16).Within(1e-9));
    }
}
