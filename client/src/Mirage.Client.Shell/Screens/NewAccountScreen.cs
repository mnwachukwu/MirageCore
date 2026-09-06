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

/// <summary>Account creation: name, password, and confirmation. On success the server replies and the
/// player lands in character select.</summary>
public sealed class NewAccountScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly TextInputField _nameField = new() { MaxLength = Constants.NameLength };
    private readonly TextInputField _passwordField = new() { MaxLength = int.MaxValue, IsPassword = true };
    private readonly TextInputField _confirmField = new() { MaxLength = int.MaxValue, IsPassword = true };
    private readonly Button _createBtn;
    private readonly Button _cancelBtn;
    private readonly IdentityChangedPrompt _identityPrompt = new();
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
        _createBtn.Label = ClientStrings.Get(ClientStrings.Common_Create);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }

    private static readonly Rectangle Dlg = new(127, 148, 546, 304);
    private static readonly Rectangle NameRect = new(439, 206, 225, 26);
    private static readonly Rectangle PassRect = new(439, 246, 225, 26);
    private static readonly Rectangle ConfirmRect = new(439, 286, 225, 26);

    public NewAccountScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _createBtn = new Button { Bounds = new Rectangle(399, 322, 200, 34), Label = ClientStrings.Get(ClientStrings.Common_Create) };
        _cancelBtn = new Button { Bounds = new Rectangle(399, 360, 200, 34), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
    }

    public void OnEnter()
    {
        _nameField.Clear();
        _passwordField.Clear();
        _confirmField.Clear();
        _errorMsg = "";
        _connecting = false;
        _draggingField = -1;
        _identityPrompt.Close();
    }
    /// <summary>Nothing to release — the screen holds no resources beyond its fields.</summary>
    public void OnExit() { }

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
                    DoCreate();
                else if (ConnectFailure.IdentityChange(_connectTask) is { } identity)
                    _identityPrompt.Open(identity);
                else
                    _errorMsg = ConnectFailure.Describe(_connectTask);
            }
            return;
        }

        long nowMs = Environment.TickCount64;
        if (_focusedField == 0) _nameField.Feed(input, nowMs);
        else if (_focusedField == 1) _passwordField.Feed(input, nowMs);
        else _confirmField.Feed(input, nowMs);

        if (input.IsKeyPressed(Keys.Tab))
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _focusedField = shift ? (_focusedField - 1 + 3) % 3 : (_focusedField + 1) % 3;
        }

        if (input.IsKeyPressed(Keys.Enter)) TryCreate();

        if (_createBtn.IsClicked(input)) TryCreate();
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new MainMenuScreen(_ctx));

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
            else if (ConfirmRect.Contains(input.MousePosition))
            {
                _focusedField = 2;
                _confirmField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 2;
            }
        }
        if (input.IsMouseDown() && !input.IsMouseJustPressed() && _draggingField >= 0)
        {
            if (_draggingField == 0) _nameField.HandleMouseClick(input.MousePosition.X, true);
            else if (_draggingField == 1) _passwordField.HandleMouseClick(input.MousePosition.X, true);
            else _confirmField.HandleMouseClick(input.MousePosition.X, true);
        }
    }

    /// <summary>Validate the fields, then create the account: sends immediately when the transport is already
    /// connected, otherwise starts an async connect that <c>Update</c> completes before sending.
    /// A validation failure sets the error text and returns without contacting the server.</summary>
    private void TryCreate()
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
        if (_passwordField.Text != _confirmField.Text)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.Common_PasswordsDoNotMatch);
            return;
        }

        _errorMsg = "";
        if (_ctx.Transport.IsConnected)
        {
            DoCreate();
        }
        else
        {
            _connecting = true;
            _connectTask = _ctx.Transport.ConnectAsync(_ctx.ServerHost, _ctx.ServerPort, CancellationToken.None);
        }
    }

    /// <summary>Send the account-create request on an established connection and hand off to the loading
    /// screen. Assumes validation already passed.</summary>
    private void DoCreate()
    {
        _ctx.State.AccountName = _nameField.Text;
        _ctx.Menu.LastAuthFlow = AuthFlow.NewAccount;
        _ctx.Sender.SendNewAccount(_nameField.Text, _passwordField.Text);
        _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.NewAccountScreen_CreatingAccount));
        _ctx.Screens.Replace(new LoadingScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long now = Environment.TickCount64;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.NewAccountScreen_Title));

        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewAccountScreen_Instruction),
            new Vector2(Dlg.X + 216, Dlg.Y + 20), UiHelper.DlgLabelColor);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_NameLabel), new Vector2(Dlg.X + 216, Dlg.Y + 58), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_PasswordLabel), new Vector2(Dlg.X + 216, Dlg.Y + 98), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewAccountScreen_ConfirmLabel), new Vector2(Dlg.X + 216, Dlg.Y + 138), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, _focusedField == 0, now);
        _passwordField.Draw(sb, font, PassRect, _focusedField == 1, now);
        _confirmField.Draw(sb, font, ConfirmRect, _focusedField == 2, now);

        _createBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);

        if (_connecting)
            UiHelper.DrawMenuAlert(sb, font, ClientStrings.Get(ClientStrings.Common_Connecting), Color.Yellow);
        else if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red);

        _identityPrompt.Draw(sb, font, _ctx.Graphics.Viewport.Bounds);
    }
}
