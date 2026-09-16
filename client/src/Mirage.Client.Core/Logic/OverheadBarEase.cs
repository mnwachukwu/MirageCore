using Mirage.Shared.Extensibility;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Where an overhead bar is drawn NOW, easing toward where it should be.
///
/// <para>The same rule the meters in a panel follow, in the other place bars are drawn. A bar over a
/// head is read the way a bar on the sidebar is — without looking straight at it — and a value that
/// jumps from full to half carries none of what somebody reads it for.</para>
///
/// <para><b>Self-timing, keyed per body and per row.</b> There is no tick to call: the render build
/// asks for each bar once a frame and this works out how long it has been since it last answered for
/// that one. A body off screen simply stops being asked, and the gap is capped, so it catches up in one
/// step when it comes back rather than easing through everything it missed.</para>
///
/// <para>⚠ Held by the client's own state rather than statically. Two clients in one process — which is
/// what a test is — would otherwise ease each other's bars, and a bar that starts at whatever the last
/// one left is a bar nobody can write a test about.</para>
/// </summary>
public sealed class OverheadBarEase
{
    // The share of the remaining gap closed per second, matching the meters so the same health does not
    // move at two speeds depending on which bar somebody is looking at.
    private const float Speed = 5f;

    // Under this the bar is at its value. A slide that never quite lands leaves a hairline of the old
    // fill showing forever.
    private const float Settled = 0.001f;

    private readonly Dictionary<(EntityHandle Who, int Row), (float Shown, long At)> _shown = [];

    /// <summary>The fraction to draw for one body's one bar, given where it should be.
    ///
    /// <para>⚠ A NEGATIVE target is <see cref="OverheadBar.Absent"/> — the row is not drawn at all — and
    /// is handed back untouched. Easing it would turn "nothing to say" into a bar at zero, which reads
    /// as a body about to die rather than one the client is not tracking.</para></summary>
    public float Toward(EntityHandle who, int row, float target)
    {
        var key = (who, row);

        if (target < 0f)
        {
            // Nothing to hold on to, and holding it would make the row reappear mid-slide if it came
            // back — from wherever it happened to be rather than from its value.
            _shown.Remove(key);
            return target;
        }

        target = Math.Min(target, 1f);
        long now = Environment.TickCount64;

        if (!_shown.TryGetValue(key, out var last))
        {
            // A body nobody has drawn yet starts AT its value. Easing up from empty would say it was
            // nearly dead a moment ago, which is a sentence about something that never happened.
            _shown[key] = (target, now);
            return target;
        }

        float delta = Math.Clamp((now - last.At) / 1000f, 0f, 0.25f);
        float moved = last.Shown + (target - last.Shown) * Math.Min(1f, Speed * delta);
        if (Math.Abs(target - moved) < Settled) moved = target;

        _shown[key] = (moved, now);
        return moved;
    }

    /// <summary>Forgets every bar, so the next draw of each starts at its value.
    ///
    /// <para>⚠ Also what keeps this from growing without end. A slot a body left is a key nothing asks
    /// about again, and the world changing under the client is the moment every one of them goes stale
    /// at once — so the same call answers both.</para></summary>
    public void Snap() => _shown.Clear();
}
