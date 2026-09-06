using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>Account login: name and password, with links to account creation, password change, and
/// account deletion. Entry point for a returning player.</summary>
public sealed class LoginScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly TextInputField _nameField = new() { MaxLength = Constants.NameLength };
    private readonly TextInputField _passwordField = new() { MaxLength = int.MaxValue, IsPassword = true };
    private readonly Button _connectBtn;
    private readonly Button _cancelBtn;
    private readonly Checkbox _rememberBox = new();
    private readonly IdentityChangedPrompt _identityPrompt = new();
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("login");
    private readonly bool _clearName;
    private readonly bool _clearPassword;
    private int _focusedField;
    private int _draggingField = -1;
    private string _errorMsg = "";
    private bool _connecting;
    private Task? _connectTask;
    private InputState _input = new();
    // Button captions are captured in the constructor, so a language switch made while this screen
    // is showing would leave them stale — a menu transition rebuilds the screen, but sitting on it
    // does not. Everything else here is fetched inline at draw time and needs no refresh.
    private int _labelsGeneration = -1;

    private void RefreshLabels()
    {
        _connectBtn.Label = ClientStrings.Get(ClientStrings.LoginScreen_ConnectButton);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        _rememberBox.Label = ClientStrings.Get(ClientStrings.LoginScreen_RememberMe);
    }

    private static readonly Rectangle Dlg = new(127, 148, 546, 304);
    private static readonly Rectangle NameRect = new(439, 220, 225, 26);
    private static readonly Rectangle PassRect = new(439, 260, 225, 26);
    private static readonly Rectangle RememberBox = new(PassRect.X, PassRect.Y + PassRect.Height + 4, 225, 16);
    private static readonly Rectangle ChangePwdLink = new(PassRect.X, RememberBox.Bottom + 4, 140, 16);

    public LoginScreen(ShellContext ctx, bool clearName = true, bool clearPassword = true)
    {
        _ctx = ctx;
        _clearName = clearName;
        _clearPassword = clearPassword;
        _connectBtn = new Button { Bounds = new Rectangle(399, 336, 200, 34), Label = ClientStrings.Get(ClientStrings.LoginScreen_ConnectButton) };
        _cancelBtn = new Button { Bounds = new Rectangle(399, 374, 200, 34), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
        _rememberBox.Bounds = RememberBox;
        _rememberBox.Label = ClientStrings.Get(ClientStrings.LoginScreen_RememberMe);
    }

    /// <summary>Clear or preserve the name and password fields per the constructor flags, so returning
    /// from a sibling screen doesn't force a re-type. A remembered name fills a field the flags would have
    /// cleared, and focus follows the same rule either way: a filled name sends the caret to the password.</summary>
    public void OnEnter()
    {
        if (!_clearName) _nameField.SetText(_ctx.State.AccountName);
        else if (_ctx.RememberLogin && _ctx.RememberedLogin.Length > 0) _nameField.SetText(_ctx.RememberedLogin);
        else _nameField.Clear();
        if (_clearPassword) _passwordField.Clear();
        _rememberBox.Checked = _ctx.RememberLogin;
        _focusedField = (_nameField.Text.Length > 0 && _clearPassword) ? 1 : 0;
        _errorMsg = "";
        _connecting = false;
        _draggingField = -1;
        _identityPrompt.Close();
    }
    /// <summary>Dismiss the remember-me tooltip so it can't outlive the screen.</summary>
    public void OnExit() => Tooltip.CloseScope(_tooltipScope);

    /// <summary>Handle typing, field focus, link clicks, and the submit key; also completes any
    /// in-flight connection attempt started by the submit handler.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }

        if (_identityPrompt.IsOpen)
        {
            if (_identityPrompt.Update(input)) _errorMsg = ClientStrings.Get(ClientStrings.Common_ServerPinCleared);
            return;
        }

        if (_connecting)
        {
            if (_connectTask!.IsCompleted)
            {
                _connecting = false;
                // Succeeded, not merely finished: a cancelled attempt completes without faulting, and
                // treating that as a connection sends the request into a socket that is already gone.
                if (_connectTask.IsCompletedSuccessfully)
                    DoLogin();
                else if (ConnectFailure.IdentityChange(_connectTask) is { } identity)
                    _identityPrompt.Open(identity);
                else
                    _errorMsg = ConnectFailure.Describe(_connectTask);
            }
            _connectBtn.Enabled = !_connecting;
            return;
        }

        long nowMs = Environment.TickCount64;
        if (_focusedField == 0) _nameField.Feed(input, nowMs);
        else _passwordField.Feed(input, nowMs);

        if (input.IsKeyPressed(Keys.Tab))
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _focusedField = shift ? (_focusedField - 1 + 2) % 2 : (_focusedField + 1) % 2;
        }

        if (_rememberBox.Update(input))
        {
            _ctx.RememberLogin = _rememberBox.Checked;
            // Unticking forgets NOW rather than at the next login, so clearing a stored name never
            // requires logging in again to take effect.
            if (!_rememberBox.Checked) _ctx.RememberedLogin = "";
            _ctx.SaveSettings();
        }

        if (input.IsKeyPressed(Keys.Enter)) TryLogin();

        if (_connectBtn.IsClicked(input)) TryLogin();
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new MainMenuScreen(_ctx));

        _connectBtn.Enabled = !_connecting;

        if (input.IsMouseJustPressed())
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _draggingField = -1;
            if (NameRect.Contains(input.MousePosition))
            {
                _focusedField = 0;
                _nameField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 0;
            }
            else if (PassRect.Contains(input.MousePosition))
            {
                _focusedField = 1;
                _passwordField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 1;
            }
        }
        if (input.IsMouseDown() && !input.IsMouseJustPressed() && _draggingField >= 0)
        {
            if (_draggingField == 0) _nameField.HandleMouseClick(input.MousePosition.X, true);
            else _passwordField.HandleMouseClick(input.MousePosition.X, true);
        }
        if (input.IsClickIn(ChangePwdLink))
            _ctx.Screens.Replace(new ChangePasswordScreen(_ctx));
    }

    /// <summary>Validate the fields, then log in: sends immediately when the transport is already
    /// connected, otherwise starts an async connect that <c>Update</c> completes before sending.
    /// A validation failure sets the error text and returns without contacting the server.</summary>
    private void TryLogin()
    {
        if (_nameField.Text.Length < Constants.MinFieldLength)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.Common_NameTooShort);
            return;
        }
        if (_passwordField.Text.Length < Constants.MinFieldLength)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.Common_PasswordTooShort);
            return;
        }
        _errorMsg = "";
        if (_ctx.Transport.IsConnected)
        {
            DoLogin();
        }
        else
        {
            _connecting = true;
            _connectTask = _ctx.Transport.ConnectAsync(_ctx.ServerHost, _ctx.ServerPort, CancellationToken.None);
        }
    }

    /// <summary>Send the login request on an established connection and hand off to the loading
    /// screen. Assumes validation already passed.</summary>
    private void DoLogin()
    {
        _ctx.State.AccountName = _nameField.Text;
        if (_rememberBox.Checked)
        {
            _ctx.RememberedLogin = _nameField.Text;
            _ctx.SaveSettings();
        }
        _ctx.Menu.LastAuthFlow = AuthFlow.Login;
        _ctx.Sender.SendLogin(_nameField.Text, _passwordField.Text);
        _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.LoginScreen_LoggingIn));
        _ctx.Screens.Replace(new LoadingScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long now = Environment.TickCount64;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.LoginScreen_Title));

        sb.DrawString(font, ClientStrings.Get(ClientStrings.LoginScreen_Instruction),
            new Vector2(Dlg.X + 216, Dlg.Y + 20), UiHelper.DlgLabelColor);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_NameLabel), new Vector2(Dlg.X + 216, Dlg.Y + 72), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_PasswordLabel), new Vector2(Dlg.X + 216, Dlg.Y + 112), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, _focusedField == 0, now);
        _passwordField.Draw(sb, font, PassRect, _focusedField == 1, now);

        // The checkbox and the link gray out with the buttons while a login is in flight. Update
        // already returns early in that state, so they were inert but still drew as though they were
        // not — the one control still lighting up on hover reads as the only thing still working.
        _rememberBox.Draw(sb, font, _input, disabled: _connecting);
        if (!_connecting && RememberBox.Contains(_input.MousePosition))
        {
            Tooltip.NotifyHoverText(_tooltipScope, (_tooltipScope, "remember"),
                ClientStrings.Get(ClientStrings.LoginScreen_RememberMeWarning), _input.MousePosition);
        }

        _connectBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);

        bool linkHovered = !_connecting && ChangePwdLink.Contains(_input.MousePosition);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.LoginScreen_ChangePasswordLink), new Vector2(ChangePwdLink.X, ChangePwdLink.Y),
            _connecting ? UiHelper.DisabledColor : (linkHovered ? Color.White : Color.Gray));

        if (_connecting)
            UiHelper.DrawMenuAlert(sb, font, ClientStrings.Get(ClientStrings.Common_Connecting), Color.Yellow);
        else if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red);

        _identityPrompt.Draw(sb, font, _ctx.Graphics.Viewport.Bounds);

        Tooltip.TickAndDraw(sb, font, now, _input.MousePosition);
    }
}
