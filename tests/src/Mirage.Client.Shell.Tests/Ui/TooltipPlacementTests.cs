using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Ui;

/// <summary>
/// Where the hover tooltip lands. It opens below the cursor, and flips above when the card will not
/// fit — which is the only thing that keeps the action bar's own tooltips off the action bar, since
/// the row sits 24px from the bottom of an 600-tall reference viewport.
/// </summary>
[TestFixture]
public class TooltipPlacementTests
{
    private const int W = 160;
    private const int H = 80;

    [Test]
    public void WithRoomBelowItOpensBelowTheCursor()
    {
        var at = Tooltip.Place(mouseX: 100, mouseY: 100, W, H);

        Assert.That(at.Y, Is.GreaterThan(100), "below the cursor, not on it");
        Assert.That(at.X, Is.GreaterThan(100), "and offset to the right");
    }

    [Test]
    public void AgainstTheBottomEdgeItFlipsAboveTheCursor()
    {
        var at = Tooltip.Place(mouseX: 100, mouseY: UiHelper.RefH - 30, W, H);

        Assert.That(at.Y + H, Is.LessThan(UiHelper.RefH - 30), "the whole card sits above the cursor");
    }

    /// <summary>The case that prompted the flip: hovering any action-bar slot put the card on the bar.</summary>
    [Test]
    public void AnActionBarSlotTooltipClearsTheActionBar()
    {
        var slot = HotkeyBarPanel.SlotBounds(1);
        var at = Tooltip.Place(slot.Center.X, slot.Center.Y, W, H);

        Assert.That(at.Y + H, Is.LessThanOrEqualTo(slot.Top),
            "the tooltip must end above the row it describes");
    }

    [Test]
    public void ItNeverLeavesTheViewport()
    {
        foreach (var corner in new[]
                 {
                     new Point(0, 0), new Point(UiHelper.RefW, 0),
                     new Point(0, UiHelper.RefH), new Point(UiHelper.RefW, UiHelper.RefH),
                 })
        {
            var at = Tooltip.Place(corner.X, corner.Y, W, H);

            Assert.That(at.X, Is.InRange(2, UiHelper.RefW - 2 - W), $"x at {corner}");
            Assert.That(at.Y, Is.InRange(2, UiHelper.RefH - 2 - H), $"y at {corner}");
        }
    }
}
