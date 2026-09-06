using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mirage.Shared;

namespace Mirage.Client.Shell.Input;

/// <summary>
/// Which physical input device currently "owns" gameplay input.  Set on the first rising edge
/// after both devices are idle and held until that device fully releases everything it cares
/// about; the other device's gameplay inputs are ignored while another device is active.
/// </summary>
public enum ActiveInputDevice { None, Keyboard, Gamepad }

public sealed class InputState
{
    private KeyboardState _prevKey;
    private KeyboardState _currKey;
    private MouseState _prevMouse;
    private MouseState _currMouse;
    // _rawBuffer accumulates chars from Window.TextInput (fires before Update).
    // _frameBuffer is the snapshot visible to screens during the same tick.
    private string _rawBuffer = "";
    private string _frameBuffer = "";
    private int _mouseOffX;
    private int _mouseOffY;
    private float _mouseScaleX = 1f;
    private float _mouseScaleY = 1f;
    // All mouse button/click/press/hover/capture/press-origin state lives in this pure state machine;
    // InputState feeds it each frame and forwards the query API. Scroll stays here (below).
    private readonly PointerInput _pointer = new();
    private bool _scrollWheelConsumed;
    private bool _suppressMouseTransition;
    private bool _suppressScrollDelta;
    private bool _suppressScrollDeltaNext;
    private HashSet<Keys> _consumedKeys = new();
    private GamePadState _prevPad;
    private GamePadState _currPad;
    private bool _isPlayStationController;
    // Which controller slot currently drives gameplay; re-chosen each frame by SelectActivePad so a
    // controller on any slot (e.g. one plugged into a dock) can take over from an idle native pad.
    private PlayerIndex _activePadIndex = PlayerIndex.One;

    // Key-repeat state: time the key was first pressed, time of the last synthetic repeat.
    private readonly Dictionary<Keys, long> _keyPressTime = new();
    private readonly Dictionary<Keys, long> _keyRepeatTime = new();
    private const long RepeatDelay = 450; // ms before repeat starts
    private const long RepeatInterval = 40; // ms between repeats

    public string TextInput => _frameBuffer;

    public Point MousePosition => _pointer.Position;

    private Point RefMousePos(MouseState m)
        => new((int)((m.X - _mouseOffX) / _mouseScaleX), (int)((m.Y - _mouseOffY) / _mouseScaleY));

    /// <summary>
    /// Suppresses the scroll-wheel delta for one frame. Call just before Update() on the frame
    /// the window regains focus, to neutralize the spurious delta produced when Reset() zeroed
    /// the previous scroll position but the accumulator hasn't changed.
    /// </summary>
    public void NotifyFocusGained() => _suppressScrollDeltaNext = true;

    /// <summary>Clears all input state. Call when the window regains focus to avoid stale key/click events.</summary>
    public void Reset()
    {
        _prevKey = default;
        _currKey = default;
        _prevMouse = default;
        _currMouse = default;
        _rawBuffer = "";
        _frameBuffer = "";
        _keyPressTime.Clear();
        _keyRepeatTime.Clear();
        _consumedKeys.Clear();
        _prevPad = default;
        _currPad = default;
        _activePadIndex = PlayerIndex.One;
        _pointer.Reset();
        ActiveDevice = ActiveInputDevice.None;
    }

    /// <summary>
    /// Call when the window is resized to prevent a phantom click on the next frame.
    /// A modal OS resize loop can pause Update() while the mouse button is held, leaving
    /// _currMouse in a Pressed state. Without this, the first Update() after the resize
    /// sees _prevMouse=Pressed / _currMouse=Released and fires a spurious IsMouseClicked().
    /// </summary>
    public void NotifyResize() => _suppressMouseTransition = true;

    /// <summary>Called each frame before Update() to map window-space mouse into reference-space (800×600).</summary>
    public void SetMouseTransform(int offsetX, int offsetY, float scaleX, float scaleY)
    {
        _mouseOffX = offsetX;
        _mouseOffY = offsetY;
        _mouseScaleX = scaleX;
        _mouseScaleY = scaleY;
    }

    public void Accumulate(char c) { if (TextValidation.IsValidChar(c) || c == '\b') _rawBuffer += c; }

    public void Update()
    {
        // Snapshot the chars accumulated since the last Update, then reset the raw buffer
        // so chars typed next frame don't bleed into this one.
        _frameBuffer = _rawBuffer;
        _rawBuffer = "";
        _scrollWheelConsumed = false;
        _consumedKeys.Clear();
        _suppressScrollDelta = _suppressScrollDeltaNext;
        _suppressScrollDeltaNext = false;

        _prevKey = _currKey;
        _currKey = Keyboard.GetState();
        _prevMouse = _currMouse;
        _currMouse = Mouse.GetState();
        _prevPad = _currPad;
        var prevPadIndex = _activePadIndex;
        _activePadIndex = SelectActivePad();
        _currPad = GamePad.GetState(_activePadIndex);
        // Re-sniff PS-vs-Xbox whenever the active pad changes slot or (re)connects; the bare
        // !_prevPad.IsConnected edge alone would miss a takeover between two connected pads.
        if (_currPad.IsConnected && (_activePadIndex != prevPadIndex || !_prevPad.IsConnected))
        {
            var caps = GamePad.GetCapabilities(_activePadIndex);
            var n = caps.DisplayName?.ToLowerInvariant() ?? "";
            _isPlayStationController = n.Contains("playstation") || n.Contains("dual") || n.Contains("sony") || n.Contains("ps4") || n.Contains("ps5");
        }
        else if (!_currPad.IsConnected)
        {
            _isPlayStationController = false;
        }

        // Feed the pointer state machine this frame's transformed position + button states. suppressEdges
        // neutralizes the phantom press/release a modal OS resize loop would surface (it can pause
        // Update() with the button held).
        _pointer.NextFrame(
            _currMouse.LeftButton == ButtonState.Pressed,
            _currMouse.RightButton == ButtonState.Pressed,
            RefMousePos(_currMouse),
            suppressEdges: _suppressMouseTransition);
        _suppressMouseTransition = false;

        UpdateActiveDevice();
    }

    public bool IsKeyPressed(Keys key) => !_consumedKeys.Contains(key) && _currKey.IsKeyDown(key) && !_prevKey.IsKeyDown(key);
    public bool IsKeyDown(Keys key) => _currKey.IsKeyDown(key);

    /// <summary>True while either Shift key is held — the modifier that turns the scroll wheel into
    /// horizontal scrolling on controls that expose a horizontal scrollbar (e.g. a wide <c>Table</c>).</summary>
    public bool IsShiftDown() => _currKey.IsKeyDown(Keys.LeftShift) || _currKey.IsKeyDown(Keys.RightShift);

    // Prevents subsequent IsKeyPressed() checks from seeing this key this frame.
    public void ConsumeKey(Keys key) => _consumedKeys.Add(key);

    /// <summary>
    /// Returns true on the initial key-down edge, then again at <see cref="RepeatDelay"/> ms
    /// and every <see cref="RepeatInterval"/> ms thereafter while the key is held.
    /// </summary>
    public bool IsKeyPressedOrRepeating(Keys key, long nowMs)
    {
        bool down = _currKey.IsKeyDown(key);
        bool wasDown = _prevKey.IsKeyDown(key);

        if (!down)
        {
            _keyPressTime.Remove(key);
            _keyRepeatTime.Remove(key);
            return false;
        }

        if (!wasDown)
        {
            _keyPressTime[key] = nowMs;
            _keyRepeatTime[key] = nowMs;
            return true;
        }

        if (_keyPressTime.TryGetValue(key, out long pressTime) &&
            nowMs - pressTime >= RepeatDelay)
        {
            long lastRepeat = _keyRepeatTime.TryGetValue(key, out long lr) ? lr : pressTime;
            if (nowMs - lastRepeat >= RepeatInterval)
            {
                _keyRepeatTime[key] = nowMs;
                return true;
            }
        }

        return false;
    }

    // ── Mouse — everything routes through the PointerInput state machine ──────────
    // Widgets use the press-origin / capture helpers below (IsClickIn, IsRightClickIn, IsPressIn,
    // IsHoverIn, CaptureMouse). The raw edge queries survive only for GameplayScreen's world-click gate
    // and its panel Z-order consume — NOT for widget click detection.

    /// <summary>A left click on <paramref name="r"/>: the press AND the release both landed inside it
    /// (press-origin), not captured/consumed. The primary click test for every widget.</summary>
    public bool IsClickIn(Rectangle r) => _pointer.IsClickIn(r);
    public bool IsRightClickIn(Rectangle r) => _pointer.IsRightClickIn(r);
    /// <summary>A left press-edge inside <paramref name="r"/> — press-based focus and drag starts.</summary>
    public bool IsPressIn(Rectangle r) => _pointer.IsPressIn(r);
    /// <summary>Hovering <paramref name="r"/> with hover unconsumed (no overlay on top, no active drag).</summary>
    public bool IsHoverIn(Rectangle r) => _pointer.IsHoverIn(r);

    // Pointer capture — a drag widget claims the pointer at its start and drives itself from
    // MousePosition + IsMouseDown() while HasMouseCapture(this) holds. Capture auto-releases the frame
    // after the button goes up and suppresses every other widget's click/press/hover/scroll meanwhile.
    public void CaptureMouse(object owner) => _pointer.Capture(owner);
    public bool HasMouseCapture(object owner) => _pointer.HasCapture(owner);
    public bool IsMouseCaptured => _pointer.IsMouseCaptured;
    public Point LeftPressOrigin => _pointer.LeftPressOrigin;
    public Point RightPressOrigin => _pointer.RightPressOrigin;

    public bool IsMouseDown() => _pointer.IsMouseDown;
    public bool IsRightMouseDown() => _pointer.IsRightMouseDown;

    // Raw release/press edges — GameplayScreen only (world-click gate + Z-order consume). Do NOT use
    // these for widget clicks; use IsClickIn / IsRightClickIn instead.
    public bool IsMouseClicked() => _pointer.LeftClickEdge;
    public bool IsMouseJustPressed() => _pointer.LeftPressEdgeActive;
    public bool IsRightMouseClicked() => _pointer.RightClickEdge;
    public bool IsRightMouseJustPressed() => _pointer.RightPressEdgeActive;

    public void ConsumeMouseClick() => _pointer.ConsumeClick();
    public void ConsumeMouseDown() => _pointer.ConsumeDown();
    public void ConsumeRightMouseClick() => _pointer.ConsumeRightClick();
    public void ConsumeRightMouseDown() => _pointer.ConsumeRightDown();

    public void ConsumeMouseHover() => _pointer.ConsumeHover();
    public void ResetMouseHover() => _pointer.ResetHover();

    /// <summary>Test seam: drives the pointer state machine with one synthetic frame, bypassing
    /// <see cref="Update"/>'s <c>Mouse.GetState()</c> so widget mouse behavior (click edges, capture,
    /// per-frame consume) can be exercised headlessly. Production code feeds the pointer via Update().</summary>
    internal void PumpMouseForTest(Point position, bool leftDown)
        => _pointer.NextFrame(leftDown, rightDown: false, position);

    public int ScrollWheelDelta()
        => _scrollWheelConsumed || _suppressScrollDelta || _pointer.IsMouseCaptured
        ? 0
        : _currMouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    public void ConsumeScrollWheel() => _scrollWheelConsumed = true;

    public bool UseGamepad { get; set; }
    public bool IsPlayStationController => _isPlayStationController;
    public bool IsGamePadLeftTriggerDown() =>
        UseGamepad && _currPad.IsConnected && _currPad.Triggers.Left >= GamepadTriggerDeadzone;
    public bool IsGamePadRightTriggerDown() =>
        UseGamepad && _currPad.IsConnected && _currPad.Triggers.Right >= GamepadTriggerDeadzone;

    /// <summary>The self-target modifier: Ctrl on the keyboard, RIGHT TRIGGER on a gamepad. HELD rather
    /// than pressed, because it qualifies the action that follows it the way Ctrl does.
    ///
    /// <para>Both triggers open the action bar and the choice of trigger aims it: LT+face fires a slot at
    /// the target, RT+face fires the same slot at the caster. Only the bar and the spell book offer this —
    /// the prepared slot is SubHp and has nothing to gain by pointing inward.</para>
    ///
    /// <para>RT WINS WHEN BOTH ARE HELD, which is why this asks about the right trigger alone and never
    /// about the left. Switching aim mid-fight is then a matter of pressing the other trigger rather than
    /// releasing one first, and a grip that rolls across both never fires at the wrong thing.</para></summary>
    public bool IsSelfTargetHeld() =>
        IsKeyDown(Keys.LeftControl) || IsKeyDown(Keys.RightControl) || IsGamePadRightTriggerDown();
    public bool IsGamePadConnected => UseGamepad && _currPad.IsConnected;
    public bool IsGamePadButtonDown(Buttons button) => UseGamepad && _currPad.IsConnected && _currPad.IsButtonDown(button);
    public bool IsGamePadButtonPressed(Buttons button) =>
        UseGamepad &&
        _currPad.IsConnected &&
        _currPad.IsButtonDown(button) &&
        (!_prevPad.IsConnected || _prevPad.IsButtonUp(button));
    public Vector2 GamePadLeftStick => UseGamepad && _currPad.IsConnected ? _currPad.ThumbSticks.Left : Vector2.Zero;
    public Vector2 GamePadRightStick => UseGamepad && _currPad.IsConnected ? _currPad.ThumbSticks.Right : Vector2.Zero;

    public ActiveInputDevice ActiveDevice { get; private set; } = ActiveInputDevice.None;

    /// <summary>True when keyboard input should drive gameplay actions this frame.
    /// Used to suppress same-action double-fires when a key and a controller button both
    /// trigger the same command (e.g. attack on Space and Square/X).</summary>
    public bool IsKeyboardActive => ActiveDevice != ActiveInputDevice.Gamepad;

    /// <summary>True when gamepad input should drive gameplay actions this frame.
    /// Mirror of <see cref="IsKeyboardActive"/> for the other device.</summary>
    public bool IsGamepadActive => ActiveDevice != ActiveInputDevice.Keyboard;

    /// <summary>Whether on-screen prompts should name gamepad buttons rather than keys. Reads the
    /// options switch alone, never <see cref="ActiveDevice"/>, so a badge prints the same thing on
    /// every frame the option is on.</summary>
    public bool ShowGamepadPrompts => UseGamepad;

    // Shared gamepad analog thresholds — used both by the active-device tracker below and by
    // gameplay input building (GameplayScreen.BuildInputSnapshot). Public so a single value
    // governs every "did the player push the stick?" / "is the trigger held?" check.
    public const float GamepadStickDeadzone = 0.5f;
    public const float GamepadTriggerDeadzone = 0.5f;
    // MonoGame exposes four controller slots (PlayerIndex.One..Four); the active-pad arbiter scans them all.
    public const int GamePadSlots = 4;

    // True when a pad has any gameplay-meaningful input held (face/shoulder/system/d-pad buttons,
    // either trigger past the deadzone, or either stick past it). Level-triggered and drift-resistant;
    // shared by the slot arbiter (SelectActivePad) and the device arbiter (UpdateActiveDevice).
    private static bool PadHasInput(in GamePadState pad) =>
        pad.IsButtonDown(Buttons.A) || pad.IsButtonDown(Buttons.B) ||
        pad.IsButtonDown(Buttons.X) || pad.IsButtonDown(Buttons.Y) ||
        pad.IsButtonDown(Buttons.LeftShoulder) || pad.IsButtonDown(Buttons.RightShoulder) ||
        pad.IsButtonDown(Buttons.Start) || pad.IsButtonDown(Buttons.Back) ||
        pad.IsButtonDown(Buttons.DPadUp) || pad.IsButtonDown(Buttons.DPadDown) ||
        pad.IsButtonDown(Buttons.DPadLeft) || pad.IsButtonDown(Buttons.DPadRight) ||
        pad.Triggers.Left >= GamepadTriggerDeadzone || pad.Triggers.Right >= GamepadTriggerDeadzone ||
        pad.ThumbSticks.Left.LengthSquared() >= GamepadStickDeadzone * GamepadStickDeadzone ||
        pad.ThumbSticks.Right.LengthSquared() >= GamepadStickDeadzone * GamepadStickDeadzone;

    // Chooses which controller slot drives gameplay this frame. The active pad keeps ownership while it
    // has any input held; once it goes idle (or disconnects), the first other connected pad showing input
    // takes over. Gamepad-vs-gamepad twin of UpdateActiveDevice's keyboard-vs-gamepad arbitration, and by
    // reading exactly one slot into _currPad it guarantees two controllers can never drive input at once.
    private PlayerIndex SelectActivePad()
    {
        if (!UseGamepad) return PlayerIndex.One; // gamepad off: always slot one
        var active = GamePad.GetState(_activePadIndex);
        if (active.IsConnected && PadHasInput(active)) return _activePadIndex; // owner keeps it while used
        for (int i = 0; i < GamePadSlots; i++) // idle or gone: let another used pad take over
        {
            var idx = (PlayerIndex)i;
            if (idx == _activePadIndex) continue;
            var s = GamePad.GetState(idx);
            if (s.IsConnected && PadHasInput(s)) return idx;
        }
        if (active.IsConnected) return _activePadIndex; // nobody else wants it; stay put while idle
        for (int i = 0; i < GamePadSlots; i++) // active pad vanished: adopt the first connected slot
            if (GamePad.GetState((PlayerIndex)i).IsConnected) return (PlayerIndex)i;
        return PlayerIndex.One;
    }

    // First-to-press wins: while either device has any input held, it owns gameplay input and
    // the other device is ignored.  When the owning device fully releases, the next rising edge
    // claims ownership.  Drift-resistant: stick/trigger thresholds are well above neutral.
    private void UpdateActiveDevice()
    {
        bool kbHasInput = _currKey.GetPressedKeys().Length > 0;
        bool padHasInput = UseGamepad && _currPad.IsConnected && PadHasInput(_currPad);

        switch (ActiveDevice)
        {
            case ActiveInputDevice.None:
                if (kbHasInput) ActiveDevice = ActiveInputDevice.Keyboard;
                else if (padHasInput) ActiveDevice = ActiveInputDevice.Gamepad;
                break;
            case ActiveInputDevice.Keyboard:
                if (!kbHasInput)
                    ActiveDevice = padHasInput ? ActiveInputDevice.Gamepad : ActiveInputDevice.None;
                break;
            case ActiveInputDevice.Gamepad:
                if (!padHasInput)
                    ActiveDevice = kbHasInput ? ActiveInputDevice.Keyboard : ActiveInputDevice.None;
                break;
        }
    }
}
