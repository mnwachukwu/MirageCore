using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// Where the party overlay is drawn: the sidebar's free space, under the Logout button.
///
/// <para>Layout is the one thing a green build says nothing about. A rectangle placed off-screen, over the
/// world, or on top of the buttons compiles perfectly and is only found by looking at it, so these pin the
/// four things that make that space usable — the panel is inside the sidebar, centred across it, clear of
/// the button block above and clear of the link strip below.</para>
///
/// <para>Everything is read off the real panels rather than restated here, so the checks follow the layout
/// instead of freezing one measurement of it.</para>
/// </summary>
[TestFixture]
public class PartyOverlayPlacementTests
{
    // The sidebar is everything right of the 512-wide game view, out to the reference width.
    private const int SidebarLeft = 513;

    [Test]
    public void ThePanelSitsInTheSidebar_NotOverTheWorld()
    {
        var r = PartyOverlayPanel.Bounds;

        Assert.Multiple(() =>
        {
            Assert.That(r.Left, Is.GreaterThanOrEqualTo(SidebarLeft), "the panel overlaps the game view");
            Assert.That(r.Right, Is.LessThanOrEqualTo(UiHelper.RefW), "the panel runs off the right edge");
        });
    }

    [Test]
    public void ThePanelIsCentredAcrossTheSidebar()
    {
        var r = PartyOverlayPanel.Bounds;
        int leftGap = r.Left - SidebarLeft;
        int rightGap = UiHelper.RefW - r.Right;

        // Off by at most one: the sidebar's width and the panel's cannot both be even.
        Assert.That(leftGap, Is.EqualTo(rightGap).Within(1), $"left gap {leftGap}, right gap {rightGap}");
    }

    /// <summary>Below the buttons, not over them — measured against the Logout button itself, so moving the
    /// button block moves this with it.</summary>
    [Test]
    public void ThePanelClearsTheButtonsAbove()
    {
        var logout = new HudPanel().LogoutBounds;

        Assert.That(PartyOverlayPanel.Bounds.Top, Is.GreaterThanOrEqualTo(logout.Bottom),
            "the panel covers the Logout button");
    }

    /// <summary>And above the Options / Help link strip at the bottom of the sidebar.</summary>
    [Test]
    public void ThePanelClearsTheLinkStripBelow()
    {
        Assert.That(PartyOverlayPanel.Bounds.Bottom, Is.LessThanOrEqualTo(HudPanel.LinkStripY),
            "the panel runs into the Options / Help links");
    }

    /// <summary>The free space is a rule, not one measurement: a panel of any width centres in it and hangs
    /// from the same line.</summary>
    [Test]
    public void AnyWidthCentresAndHangsFromTheSameLine()
    {
        var narrow = HudPanel.FreeSpaceAnchor(100);
        var wide = HudPanel.FreeSpaceAnchor(200);

        Assert.Multiple(() =>
        {
            Assert.That(narrow.X + 50, Is.EqualTo(wide.X + 100).Within(1), "they do not share a centre line");
            Assert.That(narrow.Y, Is.EqualTo(wide.Y), "the top of the free space depends on width");
        });
    }
}
