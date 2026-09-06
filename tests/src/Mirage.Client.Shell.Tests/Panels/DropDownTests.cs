using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>Headless coverage for <see cref="DropDown"/>'s pointer handling, driving a real
/// <see cref="InputState"/> through synthetic press/release frames (no MonoGame window). The focus is the
/// regression that made the chat channel selector unclickable: it opens its popup UPWARD, on top of the
/// chat log's <c>TextArea</c>, which captures the pointer the instant you press inside it. A captured
/// pointer suppresses the release's click edge, so the dropdown's row-select (resolved on release) would
/// silently never fire. The fix has the open popup swallow the press so nothing beneath can capture.</summary>
[TestFixture]
public class DropDownTests
{
    // Header docked low on the screen; with OpenUp the popup opens ABOVE it (rows at y[145,200)).
    static readonly Rectangle Header = new(0, 200, 88, 20);

    static DropDown MakeOpenUp()
    {
        var dd = new DropDown { OpenUp = true };
        dd.Items.Add("A");
        dd.Items.Add("B");
        dd.Items.Add("C");
        dd.SelectedIndex = 0;
        return dd;
    }

    // Advance the pointer one frame then update the dropdown — the order ChatPanel uses (dropdown
    // before the log). A click is a press frame followed by a release frame at the same point.
    static void Frame(InputState input, DropDown dd, Point pos, bool leftDown)
    {
        input.PumpMouseForTest(pos, leftDown);
        dd.Update(input, Header);
    }

    static void OpenPopup(InputState input, DropDown dd)
    {
        Frame(input, dd, new Point(40, 210), true);   // press the header
        Frame(input, dd, new Point(40, 210), false);  // release → popup opens
    }

    [Test]
    public void UpwardPopup_RowPress_IsSwallowedSoLogCannotCapture_AndRowStillSelects()
    {
        var input = new InputState();
        var dd = MakeOpenUp();
        OpenPopup(input, dd);

        // Popup spans y[145,200); row 1 (item "B") centers near y=172. The chat log sits beneath,
        // overlapping the popup region.
        var rowPoint = new Point(40, 172);
        var logBeneath = new Rectangle(0, 100, 88, 100); // y[100,200) — under the popup

        // Press on the row. The dropdown updates before the log would, so by then the press must be gone.
        input.PumpMouseForTest(rowPoint, leftDown: true);
        dd.Update(input, Header);
        Assert.That(input.IsPressIn(logBeneath), Is.False,
            "an open popup must consume the press so a widget beneath can't start a capture");

        // Faithfully mimic TextArea.Update: it captures the pointer on a press inside its area. With the
        // press already consumed this is a no-op; without the fix it captures and eats the click edge.
        if (input.IsPressIn(logBeneath)) input.CaptureMouse(new object());

        // Release on the row. This selects only because the pointer was never captured.
        input.PumpMouseForTest(rowPoint, leftDown: false);
        dd.Update(input, Header);
        Assert.That(dd.SelectedIndex, Is.EqualTo(1), "row-select must fire on release");
    }

    [Test]
    public void UpwardPopup_ClickSelectsTheVisualRowUnderTheCursor()
    {
        var input = new InputState();
        var dd = MakeOpenUp();
        OpenPopup(input, dd);

        // Row 2 (item "C") spans y[181,199); click its middle.
        Frame(input, dd, new Point(40, 190), true);
        Frame(input, dd, new Point(40, 190), false);
        Assert.That(dd.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void HeaderReclick_ClosesPopup_ThenRowAreaClicksAreInert()
    {
        var input = new InputState();
        var dd = MakeOpenUp();

        OpenPopup(input, dd);                          // open
        Frame(input, dd, new Point(40, 210), true);    // press the header again
        Frame(input, dd, new Point(40, 210), false);   // release → closes

        // With the popup closed there are no rows; a click where a row used to be must not select.
        Frame(input, dd, new Point(40, 172), true);
        Frame(input, dd, new Point(40, 172), false);
        Assert.That(dd.SelectedIndex, Is.EqualTo(0), "with the popup closed, clicks above the header are inert");
    }
}
