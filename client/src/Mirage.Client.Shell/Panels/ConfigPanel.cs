using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Client.Shell.Ui;
using Mirage.Shared.Security;
using System.Globalization;
using System.Net.Sockets;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Pre-connect dialog for editing Server.Host / Server.Port, and the list of servers this
/// installation knows about. Test button runs a one-shot TCP probe against a throwaway TcpClient —
/// never touches the real ShellContext.Transport.
/// </summary>
public sealed class ConfigPanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 320, 372), minH: 352, minW: 260);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);
    public string Host => _hostField.Text;
    public int PortValue => int.TryParse(_portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;

    private readonly TextInputField _hostField = new() { MaxLength = 128 };
    private readonly TextInputField _portField = new() { MaxLength = 5 };
    private readonly TextInputField _nameField = new() { MaxLength = 48 };
    private readonly Button _cancelBtn = new();
    private readonly Button _testBtn = new();
    private readonly Button _saveBtn = new();
    private readonly Button _addBtn = new();
    private readonly Button _forgetBtn = new();
    private readonly Button _clearPinBtn = new();
    private readonly ListBox _serverList = new();
    private List<ServerEntry> _servers = [];
    private int _labelsGeneration = -1;

    private int _focusedField;
    private int _draggingField = -1;
    private string _status = "";
    private Color _statusColor = Color.LightGray;

    private Task<string?>? _testTask;
    private CancellationTokenSource? _testCts;

    private Rectangle _hostFieldRect;
    private Rectangle _portFieldRect;
    private Rectangle _nameFieldRect;
    private Rectangle _listRect;

    private const int FieldCount = 3;

    public void Open(string host, int port)
    {
        _hostField.SetText(host ?? "");
        _portField.SetText(port.ToString(CultureInfo.InvariantCulture));
        _status = "";
        _focusedField = 0;
        _draggingField = -1;
        RefreshServers();
        CancelTest();
        IsOpen = true;
    }

    /// <summary>Rereads the known servers and selects whichever one matches the fields.</summary>
    private void RefreshServers()
    {
        ServerBookStore.Book.Reload();
        _servers = [.. ServerBookStore.Book.All];
        _serverList.Items.Clear();
        _serverList.Items.AddRange(_servers.Select(Describe));
        SyncSelectionToFields();
    }

    private TextInputField FieldAt(int index) => index switch
    {
        0 => _hostField,
        1 => _portField,
        _ => _nameField,
    };

    private static string Describe(ServerEntry e) =>
        e.Name.Length > 0 ? $"{e.Name}  ({e.Host}:{e.Port})" : $"{e.Host}:{e.Port}";

    // The name box follows the selection, so it shows what the highlighted row is called rather than
    // whatever was last typed against a different server.
    private void SyncSelectionToFields()
    {
        string key = ServerBook.KeyFor(_hostField.Text, PortValue);
        _serverList.SelectedIndex = _servers.FindIndex(e => e.Key == key);
        _nameField.SetText(_serverList.SelectedIndex >= 0 ? _servers[_serverList.SelectedIndex].Name : "");
    }

    public void Close()
    {
        CancelTest();
        _status = "";
        IsOpen = false;
    }

    public (bool save, bool cancel) Update(InputState input)
    {
        if (!IsOpen) return default;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            Close();
            return (false, true);
        }

        LayoutControls();
        PollTestTask();

        bool testing = _testTask is not null;
        _testBtn.Enabled = !testing;
        _saveBtn.Enabled = !testing;

        // The list drives the fields, not the other way round: picking a row is how a player chooses a
        // server without retyping it. Arrow keys would fight the text fields, so the list gets none.
        int wasSelected = _serverList.SelectedIndex;
        _serverList.Update(input, _listRect, keyboardActive: false);
        if (_serverList.SelectedIndex != wasSelected && _serverList.SelectedIndex >= 0)
        {
            var picked = _servers[_serverList.SelectedIndex];
            _hostField.SetText(picked.Host);
            _portField.SetText(picked.Port.ToString(CultureInfo.InvariantCulture));
            _nameField.SetText(picked.Name);
            _status = "";
            CancelTest();
        }

        _addBtn.Enabled = _hostField.Text.Length > 0 && PortValue > 0;
        if (_addBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            AddOrRename();
        }

        _forgetBtn.Enabled = _serverList.SelectedIndex >= 0;
        if (_forgetBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            if (_serverList.SelectedIndex >= 0)
            {
                var gone = _servers[_serverList.SelectedIndex];
                ServerBookStore.Book.Forget(gone.Host, gone.Port);
                _nameField.Clear();
                RefreshServers();
            }
        }

        // Separate from Forget: dropping a server from the list leaves its certificate on record, so
        // re-adding it still refuses to connect. This is the only other way to clear one.
        _clearPinBtn.Enabled = _serverList.SelectedIndex >= 0;
        if (_clearPinBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            if (_serverList.SelectedIndex >= 0) ClearPin(_servers[_serverList.SelectedIndex]);
        }

        if (input.IsMouseJustPressed())
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _draggingField = -1;
            if (_hostFieldRect.Contains(input.MousePosition))
            {
                _focusedField = 0;
                _hostField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 0;
            }
            else if (_portFieldRect.Contains(input.MousePosition))
            {
                _focusedField = 1;
                _portField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 1;
            }
            else if (_nameFieldRect.Contains(input.MousePosition))
            {
                _focusedField = 2;
                _nameField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 2;
            }
        }
        if (input.IsMouseDown() && !input.IsMouseJustPressed() && _draggingField >= 0)
            FieldAt(_draggingField).HandleMouseClick(input.MousePosition.X, true);

        long nowMs = Environment.TickCount64;
        string prevHost = _hostField.Text;
        string prevPort = _portField.Text;
        FieldAt(_focusedField).Feed(input, nowMs);

        // Digits-only filter on the port field — clamp any pasted/typed non-digit chars.
        string portText = _portField.Text;
        if (portText.Any(c => !char.IsDigit(c)))
            _portField.SetText(new string(portText.Where(char.IsDigit).ToArray()));

        if (input.IsKeyPressed(Keys.Tab))
        {
            bool back = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _focusedField = back
                ? (_focusedField - 1 + FieldCount) % FieldCount
                : (_focusedField + 1) % FieldCount;
            input.ConsumeKey(Keys.Tab);
        }

        // Edits invalidate any in-flight test AND any prior success/failure status —
        // a stale "succeeded" against the previously-typed host would mislead.
        if (_hostField.Text != prevHost || _portField.Text != prevPort)
        {
            CancelTest();
            _status = "";
            SyncSelectionToFields();
        }

        if (input.IsKeyPressed(Keys.Enter) && !testing)
        {
            input.ConsumeKey(Keys.Enter);
            return TrySave();
        }

        if (_cancelBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            Close();
            return (false, true);
        }
        if (_testBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            StartTest();
        }
        if (_saveBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            return TrySave();
        }

        return default;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, bool isActive = false)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _testBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_TestButton);
            _saveBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_SaveButton);
            _forgetBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_ForgetButton);
            _addBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_AddButton);
            _clearPinBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_ClearPinButton);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ConfigPanel_Title), isActive);
        LayoutControls();
        long nowMs = Environment.TickCount64;

        var c = _panel.ContentBounds;
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_HostLabel), new Vector2(c.X + 6, c.Y + 4), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_PortLabel), new Vector2(c.X + 6, c.Y + 46), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_NameLabel), new Vector2(c.X + 6, c.Y + 88), UiHelper.DlgLabelColor);

        _hostField.Draw(sb, font, _hostFieldRect, _focusedField == 0, nowMs);
        _portField.Draw(sb, font, _portFieldRect, _focusedField == 1, nowMs);
        _nameField.Draw(sb, font, _nameFieldRect, _focusedField == 2, nowMs);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_KnownServersLabel),
            new Vector2(c.X + 6, ListHeaderY(c) + 1), UiHelper.DlgLabelColor);
        _addBtn.Draw(sb, font, input);
        _forgetBtn.Draw(sb, font, input);
        _clearPinBtn.Draw(sb, font, input);
        _serverList.Draw(sb, font, _listRect);

        if (_status.Length > 0)
            sb.DrawString(font, _status, new Vector2(c.X + 6, _listRect.Bottom + 4), _statusColor);

        _cancelBtn.Draw(sb, font, input);
        _testBtn.Draw(sb, font, input, UiHelper.AccentButtonNormal, UiHelper.AccentButtonHover);
        _saveBtn.Draw(sb, font, input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);

        _panel.DrawOverlay(sb);
    }

    private (bool save, bool cancel) TrySave()
    {
        if (!Validate(out string err))
        {
            _status = err;
            _statusColor = Color.Red;
            return default;
        }
        CancelTest();
        IsOpen = false;
        return (true, false);
    }

    /// <summary>Puts the typed address in the list under the typed name. Adding a server is deliberate
    /// here; connecting to one adds it on its own.</summary>
    private void AddOrRename()
    {
        if (!Validate(out string err))
        {
            _status = err;
            _statusColor = Color.Red;
            return;
        }
        ServerBookStore.Book.Rename(_nameField.Text, _hostField.Text, PortValue);
        RefreshServers();
        if (_serverList.SelectedIndex < 0) return;
        _status = ClientStrings.Format(ClientStrings.ConfigPanel_ServerAdded,
            ("Server", Describe(_servers[_serverList.SelectedIndex])));
        _statusColor = Color.LightGreen;
    }

    /// <summary>Drops the certificate on record for one server, so the next connection to it records
    /// whatever is offered. The address book entry stays where it is.</summary>
    private void ClearPin(ServerEntry server)
    {
        bool had = ServerPinStore.Store.Forget(server.Host, server.Port);
        _status = ClientStrings.Format(
            had ? ClientStrings.ConfigPanel_PinCleared : ClientStrings.ConfigPanel_NoPinStored,
            ("Server", Describe(server)));
        _statusColor = had ? Color.LightGreen : Color.LightGray;
    }

    private bool Validate(out string err)
    {
        if (_hostField.Text.Length == 0)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_HostEmptyError);
            return false;
        }
        if (_portField.Text.Length == 0)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortEmptyError);
            return false;
        }
        if (!int.TryParse(_portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortNotNumberError);
            return false;
        }
        if (p < 1 || p > 65535)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortRangeError);
            return false;
        }
        err = "";
        return true;
    }

    private void StartTest()
    {
        if (!Validate(out string err))
        {
            _status = err;
            _statusColor = Color.Red;
            return;
        }
        CancelTest();
        string host = _hostField.Text;
        int port = int.Parse(_portField.Text, CultureInfo.InvariantCulture);
        _status = ClientStrings.Format(ClientStrings.ConfigPanel_TestingConnection, ("Host", host), ("Port", port));
        _statusColor = Color.LightGray;
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var token = _testCts.Token;
        string timedOut = ClientStrings.Get(ClientStrings.ConfigPanel_ConnectionTimedOut);
        _testTask = Task.Run<string?>(async () =>
        {
            try
            {
                using var tc = new TcpClient();
                await tc.ConnectAsync(host, port, token);
                return null;
            }
            catch (OperationCanceledException) { return timedOut; }
            catch (Exception ex) { return ex.Message; }
        }, token);
    }

    private void PollTestTask()
    {
        if (_testTask is null || !_testTask.IsCompleted) return;
        string? result;
        if (_testTask.IsFaulted)
            result = _testTask.Exception?.GetBaseException().Message ?? "Unknown error.";
        else
            result = _testTask.Result;
        if (result is null)
        {
            _status = ClientStrings.Get(ClientStrings.ConfigPanel_ConnectionSucceeded);
            _statusColor = Color.LightGreen;
        }
        else
        {
            _status = ClientStrings.Format(ClientStrings.ConfigPanel_ConnectionFailed, ("Error", result));
            _statusColor = Color.Red;
        }
        _testCts?.Dispose();
        _testCts = null;
        _testTask = null;
    }

    private void CancelTest()
    {
        try { _testCts?.Cancel(); } catch { }
        _testCts?.Dispose();
        _testCts = null;
        _testTask = null;
    }

    private void LayoutControls()
    {
        var c = _panel.ContentBounds;
        const int pad = 6;
        const int fieldH = 22;
        _hostFieldRect = new Rectangle(c.X + pad, c.Y + 18, c.Width - pad * 2, fieldH);
        _portFieldRect = new Rectangle(c.X + pad, c.Y + 60, c.Width - pad * 2, fieldH);
        _nameFieldRect = new Rectangle(c.X + pad, c.Y + 102, c.Width - pad * 2, fieldH);

        // Buttons split the inner width with two gaps, so they always fit no matter
        // how narrow the panel gets dragged. Right edge stays clear of the resize handle.
        const int btnH = 24, gap = 8, rightReserve = 14;
        int availW = c.Width - pad * 2 - rightReserve;
        int btnW = Math.Max(40, (availW - gap * 2) / 3);
        int totalW = btnW * 3 + gap * 2;
        int startX = c.X + pad + ((availW - totalW) / 2);
        int btnY = c.Bottom - btnH - 8;
        _cancelBtn.Bounds = new Rectangle(startX, btnY, btnW, btnH);
        _testBtn.Bounds = new Rectangle(startX + btnW + gap, btnY, btnW, btnH);
        _saveBtn.Bounds = new Rectangle(startX + (btnW + gap) * 2, btnY, btnW, btnH);

        // Add, Forget and Clear Pin sit on the list's header line, right-aligned, so the list keeps the
        // full width below them. They share whatever the caption leaves rather than taking a fixed width:
        // three fixed buttons overrun the caption once the panel is dragged near its minimum.
        const int smallH = 18, captionReserve = 86;
        int headerY = ListHeaderY(c);
        int smallRight = c.Right - pad - rightReserve;
        int smallW = Math.Max(40, (smallRight - (c.X + pad + captionReserve) - gap * 2) / 3);
        _clearPinBtn.Bounds = new Rectangle(smallRight - smallW, headerY, smallW, smallH);
        _forgetBtn.Bounds = new Rectangle(_clearPinBtn.Bounds.X - gap - smallW, headerY, smallW, smallH);
        _addBtn.Bounds = new Rectangle(_forgetBtn.Bounds.X - gap - smallW, headerY, smallW, smallH);

        // Whatever is left between the header and the status line, rounded down to whole rows.
        int listTop = headerY + smallH + 4;
        int listH = Math.Max(ListBox.RowPixels, (btnY - 22 - listTop) / ListBox.RowPixels * ListBox.RowPixels);
        _listRect = new Rectangle(c.X + pad, listTop, c.Width - pad * 2 - rightReserve, listH);
    }

    private static int ListHeaderY(Rectangle content) => content.Y + 130;
}
