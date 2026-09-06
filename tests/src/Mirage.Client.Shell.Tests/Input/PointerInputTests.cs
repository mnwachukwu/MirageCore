using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Input;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Input;

/// <summary>Regression net for the pointer state machine — the invariants that kill mouse bleed-through.
/// Each case here is one a per-widget workaround could only patch for a single widget, so they are
/// asserted against the shared state machine instead.</summary>
[TestFixture]
public class PointerInputTests
{
    private static readonly Rectangle A = new(0, 0, 10, 10);
    private static readonly Rectangle B = new(20, 20, 10, 10);

    // Advance one frame with the given left-button state at position pos (right button up).
    private static void Frame(PointerInput p, bool left, Point pos) => p.NextFrame(left, false, pos, false);

    [Test]
    public void PressAndReleaseInSameRect_IsAClick()
    {
        var p = new PointerInput();
        Frame(p, false, new Point(5, 5));  // idle over A
        Frame(p, true, new Point(5, 5));   // press in A
        Assert.That(p.IsClickIn(A), Is.False); // still held — not a click yet
        Frame(p, false, new Point(5, 5));  // release in A
        Assert.That(p.IsClickIn(A), Is.True);
    }

    [Test]
    public void PressInA_ReleaseInB_ClicksNeither()
    {
        var p = new PointerInput();
        Frame(p, false, new Point(5, 5));
        Frame(p, true, new Point(5, 5));    // press in A
        Frame(p, false, new Point(25, 25)); // release in B
        Assert.That(p.IsClickIn(A), Is.False); // press-origin was A, release is B
        Assert.That(p.IsClickIn(B), Is.False); // press-origin was A, not B
    }

    [Test]
    public void DragReleaseOverAnotherWidget_IsSuppressed()
    {
        var p = new PointerInput();
        var scrollbar = new object();
        Frame(p, false, new Point(5, 5));
        Frame(p, true, new Point(5, 5));    // press on the "scrollbar" in A
        p.Capture(scrollbar);               // scrollbar claims the pointer for its drag
        Frame(p, true, new Point(25, 25));  // drag over B
        Frame(p, false, new Point(25, 25)); // release over B (a button)
        Assert.That(p.IsClickIn(B), Is.False); // the release must not click B
    }

    [Test]
    public void CaptureAutoReleasesTheFrameAfterButtonUp()
    {
        var p = new PointerInput();
        var owner = new object();
        Frame(p, true, new Point(5, 5));
        p.Capture(owner);
        Assert.That(p.HasCapture(owner), Is.True);
        Frame(p, false, new Point(5, 5));   // release frame — capture still held (suppresses the release)
        Assert.That(p.HasCapture(owner), Is.True);
        Frame(p, false, new Point(5, 5));   // next frame — button up a full frame → released
        Assert.That(p.HasCapture(owner), Is.False);
    }

    [Test]
    public void HoverAndScrollSuppressedWhileCaptured()
    {
        var p = new PointerInput();
        var owner = new object();
        Frame(p, true, new Point(5, 5));
        p.Capture(owner);
        Frame(p, true, new Point(5, 5));
        Assert.That(p.IsHoverConsumed, Is.True); // a drag suppresses hover for everyone
        Assert.That(p.IsHoverIn(A), Is.False);
    }

    [Test]
    public void HoverIn_TrueWhenOverRectAndUnconsumed()
    {
        var p = new PointerInput();
        Frame(p, false, new Point(5, 5));
        Assert.That(p.IsHoverIn(A), Is.True);
        Assert.That(p.IsHoverIn(B), Is.False);
    }

    [Test]
    public void ConsumedClickDoesNotFire()
    {
        var p = new PointerInput();
        Frame(p, false, new Point(5, 5));
        Frame(p, true, new Point(5, 5));
        Frame(p, false, new Point(5, 5));   // release in A
        p.ConsumeClick();
        Assert.That(p.IsClickIn(A), Is.False); // consumed by a higher-Z widget this frame
    }

    [Test]
    public void PressIn_FiresOnlyOnThePressEdgeInsideTheRect()
    {
        var p = new PointerInput();
        Frame(p, false, new Point(5, 5));
        Assert.That(p.IsPressIn(A), Is.False); // no press
        Frame(p, true, new Point(5, 5));       // press edge in A
        Assert.That(p.IsPressIn(A), Is.True);
        Frame(p, true, new Point(5, 5));       // still down — not a new edge
        Assert.That(p.IsPressIn(A), Is.False);
    }

    [Test]
    public void RightClickUsesItsOwnPressOrigin()
    {
        var p = new PointerInput();
        p.NextFrame(false, false, new Point(5, 5));
        p.NextFrame(false, true, new Point(5, 5));  // right press in A
        p.NextFrame(false, false, new Point(5, 5)); // right release in A
        Assert.That(p.IsRightClickIn(A), Is.True);
    }
}
