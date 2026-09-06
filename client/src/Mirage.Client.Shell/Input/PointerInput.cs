using Microsoft.Xna.Framework;

namespace Mirage.Client.Shell.Input;

/// <summary>
/// Pure, headless-testable mouse state machine: button edges, press-origin, pointer capture, and
/// per-frame consumption. <see cref="InputState"/> owns one and feeds it each frame via
/// <see cref="NextFrame"/> with the reference-space cursor position and raw button states; the rest of
/// the client queries clicks / hover / drags exclusively through the helpers here (no MonoGame statics,
/// no <c>GraphicsDevice</c>, so it unit-tests headless).
///
/// The two invariants that kill mouse bleed-through:
///   • A CLICK requires the press AND the release to land in the same rect (<see cref="IsClickIn"/>),
///     so a press that starts on A and releases on B fires neither.
///   • A DRAG CAPTURES the pointer (<see cref="Capture"/>): while captured, every click / press / hover
///     / scroll query returns "nothing" for non-owners, and the release that ends the drag is suppressed
///     for everyone — no widget can steal it. Capture auto-releases the frame after the button goes up.
/// </summary>
public sealed class PointerInput
{
    private bool _leftPrev, _leftCurr;
    private bool _rightPrev, _rightCurr;
    private Point _position;
    private Point _leftPressOrigin;
    private Point _rightPressOrigin;

    private bool _clickConsumed, _rightClickConsumed, _downConsumed, _rightDownConsumed, _hoverConsumed;

    private object? _captureOwner;

    /// <summary>Advance one frame. <paramref name="suppressEdges"/> syncs prev=curr so no press/release
    /// edge is reported this frame — used after an OS resize/focus pause that could otherwise surface a
    /// phantom click.</summary>
    public void NextFrame(bool leftDown, bool rightDown, Point position, bool suppressEdges = false)
    {
        _clickConsumed = _rightClickConsumed = _downConsumed = _rightDownConsumed = _hoverConsumed = false;

        _leftPrev = _leftCurr;
        _leftCurr = leftDown;
        _rightPrev = _rightCurr;
        _rightCurr = rightDown;
        _position = position;

        if (suppressEdges)
        {
            _leftPrev = _leftCurr;
            _rightPrev = _rightCurr;
        }

        if (LeftPressEdge) _leftPressOrigin = position;
        if (RightPressEdge) _rightPressOrigin = position;

        // Capture is held through the release frame (so that release is suppressed for everyone), then
        // cleared once the button has been up for a full frame.
        if (_captureOwner != null && !_leftCurr && !_leftPrev)
            _captureOwner = null;
    }

    // ── Raw edges ────────────────────────────────────────────────────────────
    private bool LeftPressEdge => _leftCurr && !_leftPrev;
    private bool LeftReleaseEdge => !_leftCurr && _leftPrev;
    private bool RightPressEdge => _rightCurr && !_rightPrev;
    private bool RightReleaseEdge => !_rightCurr && _rightPrev;

    public Point Position => _position;
    public Point LeftPressOrigin => _leftPressOrigin;
    public Point RightPressOrigin => _rightPressOrigin;

    public bool IsMouseDown => _leftCurr;
    public bool IsRightMouseDown => _rightCurr;
    public bool IsMouseCaptured => _captureOwner != null;

    // Raw release/press edges, gated by consume + capture. WIDGETS SHOULD NOT USE THESE — they exist
    // only for the world-click gate (which adds its own press-origin test) and the panel Z-order
    // consume. Widgets use IsClickIn / IsRightClickIn / IsPressIn / IsHoverIn.
    public bool LeftClickEdge => !_clickConsumed && !IsMouseCaptured && LeftReleaseEdge;
    public bool RightClickEdge => !_rightClickConsumed && !IsMouseCaptured && RightReleaseEdge;
    public bool LeftPressEdgeActive => !_downConsumed && !IsMouseCaptured && LeftPressEdge;
    public bool RightPressEdgeActive => !_rightDownConsumed && !IsMouseCaptured && RightPressEdge;

    // ── Press-origin-aware widget API ─────────────────────────────────────────
    /// <summary>A left click credited to <paramref name="r"/>: the button released this frame AND the
    /// press began inside <paramref name="r"/> AND the cursor is still inside it. Not captured/consumed.</summary>
    public bool IsClickIn(Rectangle r)
        => LeftClickEdge && r.Contains(_leftPressOrigin) && r.Contains(_position);

    public bool IsRightClickIn(Rectangle r)
        => RightClickEdge && r.Contains(_rightPressOrigin) && r.Contains(_position);

    /// <summary>A left press-edge inside <paramref name="r"/> — for press-based focus and drag starts.
    /// Not down-consumed and not already captured, so only one widget claims a given press.</summary>
    public bool IsPressIn(Rectangle r)
        => LeftPressEdgeActive && r.Contains(_position);

    public bool IsHoverIn(Rectangle r)
        => !IsHoverConsumed && r.Contains(_position);

    // ── Consumption (per-frame; reset by NextFrame) ────────────────────────────
    public void ConsumeClick() => _clickConsumed = true;
    public void ConsumeRightClick() => _rightClickConsumed = true;
    public void ConsumeDown() => _downConsumed = true;
    public void ConsumeRightDown() => _rightDownConsumed = true;
    public void ConsumeHover() => _hoverConsumed = true;
    public void ResetHover() => _hoverConsumed = false;
    /// <summary>Hover is consumed either explicitly (a panel drawn on top) or implicitly while the
    /// pointer is captured (a drag suppresses every other widget's highlight).</summary>
    public bool IsHoverConsumed => _hoverConsumed || IsMouseCaptured;

    // ── Capture ────────────────────────────────────────────────────────────────
    /// <summary>Claim the pointer for a drag. The owner then drives itself from <see cref="Position"/> +
    /// <see cref="IsMouseDown"/> while <see cref="HasCapture"/> is true; capture auto-releases the frame
    /// after the button goes up (no explicit <see cref="Release"/> needed for button-ended drags).</summary>
    public void Capture(object owner) => _captureOwner = owner;
    public void Release(object owner) { if (ReferenceEquals(_captureOwner, owner)) _captureOwner = null; }
    public bool HasCapture(object owner) => ReferenceEquals(_captureOwner, owner);

    /// <summary>Clear all state — called on window focus-regain so no stale button edge or capture
    /// survives the gap.</summary>
    public void Reset()
    {
        _leftPrev = _leftCurr = _rightPrev = _rightCurr = false;
        _position = _leftPressOrigin = _rightPressOrigin = Point.Zero;
        _clickConsumed = _rightClickConsumed = _downConsumed = _rightDownConsumed = _hoverConsumed = false;
        _captureOwner = null;
    }
}
