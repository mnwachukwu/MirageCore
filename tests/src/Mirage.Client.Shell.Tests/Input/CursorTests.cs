using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Tests.Panels;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Input;

/// <summary>
/// Regression coverage for every place the OS mouse cursor changes shape. All cursor changes funnel through
/// the one <see cref="UiHelper"/> request bus (Request*Cursor → highest priority wins → CommitFrameCursor),
/// so these pin (a) the bus arbitration itself and (b) each widget's PURE geometric "do I want cursor X here?"
/// decision. The hardware input path (InputState / actual hover) stays a manual playtest — same boundary the
/// table tests draw — but the geometry + priority that decide the cursor are all exercised here.
///
/// These deliberately never call <see cref="UiHelper.CommitFrameCursor"/>: committing maps a request to a real
/// MonoGame <c>MouseCursor</c>, which creates an SDL system cursor and is unavailable in a headless test. The
/// bus tracks the pending request as a plain enum precisely so the arbitration is observable without one.
/// </summary>
public class CursorBusTests
{
    [SetUp]
    public void Reset() => UiHelper.ResetFrameCursor();   // isolate cases over the static bus

    [Test]
    public void NoRequest_LeavesTheDefaultArrow()
        => Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.Arrow));

    [Test]
    public void RequestHand_WinsHand()   // a link / a chat name
    {
        UiHelper.RequestHandCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.Hand));
    }

    [Test]
    public void RequestResizeWe_WinsResizeWe()   // a table column divider
    {
        UiHelper.RequestResizeWeCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.ResizeWe));
    }

    [Test]
    public void RequestResizeNwse_WinsResizeNwse()   // a panel resize handle
    {
        UiHelper.RequestResizeNwseCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.ResizeNwse));
    }

    // The scenario the bus exists for: a link hover and a panel resize handle wanting different cursors in the
    // same frame. Hand must win no matter which widget asked first.
    [Test]
    public void Hand_BeatsDiagonalResize_EitherOrder()
    {
        UiHelper.RequestResizeNwseCursor();
        UiHelper.RequestHandCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.Hand));

        UiHelper.ResetFrameCursor();
        UiHelper.RequestHandCursor();
        UiHelper.RequestResizeNwseCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.Hand), "a later lower-priority request must not override Hand");
    }

    [Test]
    public void ColumnResize_BeatsDiagonalResize_EitherOrder()
    {
        UiHelper.RequestResizeNwseCursor();
        UiHelper.RequestResizeWeCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.ResizeWe));

        UiHelper.ResetFrameCursor();
        UiHelper.RequestResizeWeCursor();
        UiHelper.RequestResizeNwseCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.ResizeWe));
    }

    [Test]
    public void Priority_Ranks_Hand_Above_We_Above_Nwse_Above_Arrow()
    {
        Assert.That((int)UiHelper.CursorRequest.Hand, Is.GreaterThan((int)UiHelper.CursorRequest.ResizeWe));
        Assert.That((int)UiHelper.CursorRequest.ResizeWe, Is.GreaterThan((int)UiHelper.CursorRequest.ResizeNwse));
        Assert.That((int)UiHelper.CursorRequest.ResizeNwse, Is.GreaterThan((int)UiHelper.CursorRequest.Arrow));
    }

    [Test]
    public void ResetFrameCursor_ClearsBackToArrow()
    {
        UiHelper.RequestHandCursor();
        UiHelper.ResetFrameCursor();
        Assert.That(UiHelper.RequestedCursor, Is.EqualTo(UiHelper.CursorRequest.Arrow));
    }
}

/// <summary>The panel resize-handle NW-SE cursor rule (<see cref="DraggablePanel.WantsResizeCursor"/>): the
/// diagonal arrow shows over the bottom-right resize triangle of a resizable panel, and nowhere else.</summary>
public class PanelResizeCursorTests
{
    // A 300x320 panel at (40,40): bottom-right corner (340,360), so the 12px resize handle is (328,348)-(340,360).
    private static DraggablePanel Panel(bool resizable = true)
        => new(new Rectangle(40, 40, 300, 320), resizable: resizable);

    [Test]
    public void OverTheResizeHandle_WantsResizeCursor()
        => Assert.That(Panel().WantsResizeCursor(new Point(334, 354)), Is.True);

    [Test]
    public void InsideThePanelButNotTheHandle_DoesNot()
        => Assert.That(Panel().WantsResizeCursor(new Point(100, 100)), Is.False);

    [Test]
    public void OutsideThePanelEntirely_DoesNot()
        => Assert.That(Panel().WantsResizeCursor(new Point(0, 0)), Is.False);

    [Test]
    public void AtTheHandleTopLeftCorner_WantsResizeCursor()   // inclusive edge (328,348)
        => Assert.That(Panel().WantsResizeCursor(new Point(328, 348)), Is.True);

    [Test]
    public void ANonResizablePanel_NeverWantsTheCursor_EvenOverTheHandleSpot()
        => Assert.That(Panel(resizable: false).WantsResizeCursor(new Point(334, 354)), Is.False);
}

/// <summary>The table column-divider WE cursor rule (<see cref="Table{T}.IsOverColumnDivider"/>): the
/// left/right arrow shows when the pointer is within the grab band of a column's right-edge divider, inside
/// the header strip.</summary>
public class TableResizeCursorTests
{
    private sealed record Row(int V);

    // Two columns 100 + 80 wide → right-edge dividers at content x = 100 and x = 180. Header at x=0 so the
    // screen x maps straight through (no horizontal scroll). DividerGrab is +/-4 px.
    private static Table<Row> Grid() => new Table<Row>()
        .Column("A", r => r.V, width: 100)
        .Column("B", r => r.V, width: 80);

    private static readonly Rectangle Header = new(0, 0, 200, 16);

    [Test]
    public void OverTheFirstDivider_WantsResizeCursor()
        => Assert.That(Grid().IsOverColumnDivider(new Point(100, 8), Header), Is.True);

    [Test]
    public void OverTheSecondDivider_WantsResizeCursor()
        => Assert.That(Grid().IsOverColumnDivider(new Point(180, 8), Header), Is.True);

    [Test]
    public void WithinTheGrabBand_WantsResizeCursor()   // 4px band around x=100
        => Assert.That(Grid().IsOverColumnDivider(new Point(96, 8), Header), Is.True);

    [Test]
    public void MidColumn_DoesNot()
        => Assert.That(Grid().IsOverColumnDivider(new Point(50, 8), Header), Is.False);

    [Test]
    public void JustOutsideTheGrabBand_DoesNot()   // 6px from x=100, past the 4px grab
        => Assert.That(Grid().IsOverColumnDivider(new Point(94, 8), Header), Is.False);

    [Test]
    public void OverADividerXButBelowTheHeaderStrip_DoesNot()   // the header.Contains gate
        => Assert.That(Grid().IsOverColumnDivider(new Point(100, 30), Header), Is.False);
}
