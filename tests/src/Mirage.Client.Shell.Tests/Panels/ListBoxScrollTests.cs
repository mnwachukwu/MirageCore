using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// A list's scroll position against contents it does not own.
///
/// <para>Consumers refill <see cref="ListBox.Items"/> in place and nothing tells the control that happened. If
/// the new contents are shorter than the old offset skipped, every remaining row sits above the viewport and
/// the list draws EMPTY — which is what a second shop looked like after scrolling the first.</para>
/// </summary>
[TestFixture]
public class ListBoxScrollTests
{
    // 10 rows of 20px, plus the 8px scrollbar gutter on the right.
    private static readonly Rectangle Bounds = new(0, 0, 200, 200);

    private static ListBox Filled(int rows)
    {
        var lb = new ListBox();
        for (int i = 0; i < rows; i++) lb.Items.Add($"row {i}");
        return lb;
    }

    // A click is a press frame then a release frame at the same point, as the panels drive it.
    private static void Click(InputState input, ListBox lb, Point pos)
    {
        input.PumpMouseForTest(pos, leftDown: true);
        lb.Update(input, Bounds);
        input.PumpMouseForTest(pos, leftDown: false);
        lb.Update(input, Bounds);
    }

    // Paging down the scrollbar track is the one way to move the offset without a mouse wheel, which has no
    // test seam. Bottom of the track, which is below the thumb while the list is at the top.
    private static void PageDown(InputState input, ListBox lb)
        => Click(input, lb, new Point(Bounds.Right - 4, Bounds.Bottom - 4));

    [Test]
    public void ARefillWithFewerRowsCannotLeaveTheListScrolledPastItsEnd()
    {
        var lb = Filled(40);
        var input = new InputState();
        PageDown(input, lb);
        Assert.That(lb.ScrollOffset, Is.GreaterThan(0), "precondition: the list is scrolled down");

        lb.Items.Clear();                       // a different, shorter set of contents
        for (int i = 0; i < 3; i++) lb.Items.Add($"short {i}");
        input.PumpMouseForTest(new Point(400, 400), leftDown: false);
        lb.Update(input, Bounds);

        Assert.That(lb.ScrollOffset, Is.Zero,
            "all three rows fit, so none of them may be scrolled out of sight");
    }

    /// <summary>The clamp must not undo an honest scroll — a list longer than its viewport still scrolls.</summary>
    [Test]
    public void AListLongerThanItsViewportStillScrolls()
    {
        var lb = Filled(40);
        var input = new InputState();

        PageDown(input, lb);
        int afterPage = lb.ScrollOffset;
        input.PumpMouseForTest(new Point(400, 400), leftDown: false);
        lb.Update(input, Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(afterPage, Is.GreaterThan(0));
            Assert.That(lb.ScrollOffset, Is.EqualTo(afterPage), "an idle frame does not reset the position");
        });
    }

    /// <summary>Opening on unrelated contents starts fresh — see ShopPanel.Open.</summary>
    [Test]
    public void Reset_ReturnsToTheTopWithNothingSelected()
    {
        var lb = Filled(40);
        var input = new InputState();
        PageDown(input, lb);
        lb.SelectedIndex = 7;

        lb.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(lb.ScrollOffset, Is.Zero);
            Assert.That(lb.SelectedIndex, Is.EqualTo(-1),
                "a stale index would arm a Buy button against whatever now occupies that row");
        });
    }
}
