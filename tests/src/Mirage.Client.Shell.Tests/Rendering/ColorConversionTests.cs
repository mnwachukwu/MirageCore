using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Rendering;

/// <summary>HSV ↔ RGB math behind the guild color picker's box/slider. The picker relies on a clean
/// round-trip so dragging the box and typing RGB numbers stay in sync without drifting.</summary>
[TestFixture]
public class ColorConversionTests
{
    [TestCase(0f, 1f, 1f, 255, 0, 0)]     // red
    [TestCase(120f, 1f, 1f, 0, 255, 0)]   // green
    [TestCase(240f, 1f, 1f, 0, 0, 255)]   // blue
    [TestCase(0f, 0f, 1f, 255, 255, 255)] // white (no saturation)
    [TestCase(0f, 0f, 0f, 0, 0, 0)]       // black (no value)
    [TestCase(60f, 1f, 1f, 255, 255, 0)]  // yellow
    public void HsvToRgb_KnownColors(float h, float s, float v, int r, int g, int b)
    {
        var (rr, gg, bb) = ColorConversion.HsvToRgb(h, s, v);
        Assert.That((rr, gg, bb), Is.EqualTo((r, g, b)));
    }

    [TestCase(150, 110, 40)]
    [TestCase(17, 200, 233)]
    [TestCase(1, 2, 3)]
    [TestCase(128, 128, 128)]
    public void RgbToHsvToRgb_RoundTrips(int r, int g, int b)
    {
        var (h, s, v) = ColorConversion.RgbToHsv(r, g, b);
        var (rr, gg, bb) = ColorConversion.HsvToRgb(h, s, v);
        // Allow a 1-step rounding wobble per channel from the float round-trip.
        Assert.That(rr, Is.EqualTo(r).Within(1));
        Assert.That(gg, Is.EqualTo(g).Within(1));
        Assert.That(bb, Is.EqualTo(b).Within(1));
    }

    [Test]
    public void HsvToRgb_NormalizesHueWraparound()
    {
        // 360 and 0 are the same hue; both must give pure red at full S/V.
        Assert.That(ColorConversion.HsvToRgb(360f, 1f, 1f), Is.EqualTo(ColorConversion.HsvToRgb(0f, 1f, 1f)));
    }
}
