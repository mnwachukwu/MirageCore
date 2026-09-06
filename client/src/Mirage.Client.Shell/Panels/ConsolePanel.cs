using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Diagnostics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Serilog.Events;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The client's log, on screen. Opened with the backtick key from anywhere — the login screen, character
/// select, or in game — because the moments worth reading a log for include the ones before a world exists.
///
/// <para><b>It displays and nothing else.</b> There is no command line and no input of any kind: this
/// answers "what just happened", and a console that also DID things would need a permission model, a
/// parser, and a reason to trust the person typing. Scrolling is the only interaction.</para>
///
/// <para>It reads <see cref="ClientLog.Console"/>, which receives every event the log file does whether
/// this panel is open or not — so opening it shows the history rather than starting a recording. The
/// buffer is filled once on the first draw and appended live after that.</para>
///
/// <para>🔴 Everything here is visible to the player and screenshottable. What may be logged is decided
/// at the call site, and <see cref="ConsoleSink"/> states the rule: never a credential, a token, or a
/// server-side input the client is not otherwise given.</para>
/// </summary>
public sealed class ConsolePanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(40, 40, 520, 300), minW: 320, minH: 160);
    private readonly TextArea _log = new() { ReadOnly = true, ShowTimestamps = true };

    private bool _filled;

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    public ConsolePanel()
    {
        // Subscribed for the life of the client, not just while open, so a line logged with the console
        // closed is already in place when it opens. Raised from whichever thread logged, which is why the
        // append is queued rather than applied here.
        ClientLog.Console.Written += line =>
        {
            lock (_pending) _pending.Add(line);
        };
    }

    private readonly List<ConsoleSink.Line> _pending = [];

    public void Toggle() => IsOpen = !IsOpen;
    public void Close() => IsOpen = false;

    public void Update(InputState input)
    {
        if (!IsOpen) return;
        _panel.Update(input);
        if (_panel.WasClosed) IsOpen = false;

        _log.SetBounds(_panel.ContentBounds);
        // keyboardActive: false — the scroll wheel and the scrollbar work, the keyboard is not taken.
        // Nothing here reads text, and a console that swallowed keys would eat movement the moment it
        // was left open.
        _log.Update(input, keyboardActive: false);
    }

    public void Draw(SpriteBatch sb, SpriteFont font, long nowMs, bool isActive = false)
    {
        if (!IsOpen) return;

        Drain();

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ConsolePanel_Title), isActive);
        _log.SetBounds(_panel.ContentBounds);
        _log.Draw(sb, font, nowMs);
        _panel.DrawOverlay(sb);
    }

    /// <summary>Moves what has been logged into the view. The backlog lands once, on the first open;
    /// everything after it arrives through the event.</summary>
    private void Drain()
    {
        if (!_filled)
        {
            foreach (var line in ClientLog.Console.Snapshot()) Append(line);
            _filled = true;
            lock (_pending) _pending.Clear();   // the snapshot already covers anything queued so far
            return;
        }

        lock (_pending)
        {
            foreach (var line in _pending) Append(line);
            _pending.Clear();
        }
    }

    /// <summary>Severity as color, using the palette the chat log already speaks: a warning reads as a
    /// warning wherever it appears in this client.</summary>
    private void Append(ConsoleSink.Line line) => _log.AddLine(line.Text, line.Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => GameColor.BrightRed,
        LogEventLevel.Warning => GameColor.Warning,
        LogEventLevel.Debug or LogEventLevel.Verbose => GameColor.Gray,
        _ => GameColor.White,
    });
}
