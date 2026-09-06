using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Ui;

/// <summary>
/// The frame readout stacks above the action bar, which sits in the bottom-right corner — its left edge is at
/// 583 of the 800-wide reference, leaving barely two hundred pixels before the screen ends.
///
/// <para>🔴 It starts AT the bar and slides left only as far as the screen edge demands. The chat log fills
/// the other half of the screen, so a block that moves further than it has to lands on top of it — which is
/// what aligning its right edge to the BAR's right edge does, since every line is wider than four hotkeys.
/// The screen edge is the constraint; the bar is only where it starts.</para>
///
/// <para>This is arithmetic, not rendering, so it is testable without a device — what a font measures is
/// the input.</para>
/// </summary>
[TestFixture]
public class FrameReadoutLayoutTests
{
    private static readonly float BarLeft = HotkeyBarPanel.Bounds.Left;
    private const float Screen = UiHelper.RefW;

    /// <summary>The room a line has if the block never moves — what every readout line is written to fit.</summary>
    private static readonly float Room = Screen - BarLeft;

    [TestCase(60f)]
    [TestCase(150f)]
    public void ContentThatFits_StartsAtTheBarAndDoesNotMove(float widest)
    {
        Assert.That(GameplayScreen.ReadoutLeft(BarLeft, Screen, widest), Is.EqualTo(BarLeft),
            "a line with room to spare has no reason to move left, and moving lands it on the chat log");
    }

    [Test]
    public void ContentExactlyFillingTheRoom_StillStartsAtTheBar()
    {
        Assert.That(GameplayScreen.ReadoutLeft(BarLeft, Screen, Room), Is.EqualTo(BarLeft));
    }

    [TestCase(20f)]
    [TestCase(80f)]
    public void ContentTooWide_SlidesLeftByExactlyTheOverflow(float over)
    {
        float x = GameplayScreen.ReadoutLeft(BarLeft, Screen, Room + over);
        Assert.Multiple(() =>
        {
            Assert.That(BarLeft - x, Is.EqualTo(over).Within(0.01f), "it moves by the overflow and no further");
            Assert.That(x + Room + over, Is.EqualTo(Screen).Within(0.01f), "so the last pixel is the last pixel");
        });
    }

    [Test]
    public void ContentWiderThanTheScreen_StartsAtTheLeftEdge()
    {
        // Nothing can save a line this long, but it must clip at the far edge rather than start off-screen.
        Assert.That(GameplayScreen.ReadoutLeft(BarLeft, Screen, Screen * 2f), Is.EqualTo(0f));
    }

    [Test]
    public void TheBarLeavesEnoughRoomForTheLinesAsWritten()
    {
        // The premise. The longest line is the GC one — "gc 12/3 in 10s  1234 KB/s  (1234/56/7)" at its
        // widest — and the readout font runs about seven pixels a character, so this is the budget the
        // strings are written against. If the bar ever moves right, they have to get shorter.
        Assert.That(Room, Is.GreaterThanOrEqualTo(210f),
            "there is no longer room beside the action bar for a readout line, so the block will cover chat");
    }
}
