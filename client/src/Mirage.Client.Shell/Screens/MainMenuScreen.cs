using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>Top-level menu: Login / New Account / Delete Account / Credits / Quit.</summary>
public sealed class MainMenuScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly Button[] _buttons;
    private InputState _input = new();
    // Captions are captured once, so a language switch made while this screen is already showing
    // would otherwise leave them stale (a transition rebuilds the screen; sitting on it does not).
    // Trailing ClientStrings.Generation re-labels exactly once per switch, as the panels do.
    private int _labelsGeneration = -1;

    private static string[] Labels() =>
    [
        ClientStrings.Get(ClientStrings.MainMenuScreen_LoginButton),
        ClientStrings.Get(ClientStrings.MainMenuScreen_NewAccountButton),
        ClientStrings.Get(ClientStrings.MainMenuScreen_DeleteAccountButton),
        ClientStrings.Get(ClientStrings.MainMenuScreen_CreditsButton),
        ClientStrings.Get(ClientStrings.MainMenuScreen_QuitButton),
    ];

    private void RefreshLabels()
    {
        string[] labels = Labels();
        for (int i = 0; i < _buttons.Length && i < labels.Length; i++)
            _buttons[i].Label = labels[i];
    }

    public MainMenuScreen(ShellContext ctx)
    {
        _ctx = ctx;

        // Distribute buttons evenly — derive horizontal pad from the vertical gap so all four edges are equal.
        var dlg = UiHelper.MenuDialogRect;
        const int BtnH = 48;
        string[] labels = Labels();
        float gap = (dlg.Height - labels.Length * BtnH) / (labels.Length + 1f);
        int pad = (int)Math.Round(gap);
        int cX = dlg.X + UiHelper.MenuDlgArtW + pad;
        int cW = dlg.Width - UiHelper.MenuDlgArtW - pad * 2;
        _buttons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int y = dlg.Y + (int)Math.Round(gap * (i + 1) + BtnH * i);
            _buttons[i] = new Button { Bounds = new Rectangle(cX, y, cW, BtnH), Label = labels[i] };
        }
    }

    /// <summary>Back at the main menu we are in no world, so the client drops whatever name the last
    /// server gave it and wears the engine's again. Without this the menu would still be advertising a
    /// server you have already left. Empty resets it — see <see cref="ClientState.GameName"/>.</summary>
    public void OnEnter()
    {
        _ctx.State.GameName = "";
        _ctx.PlayMenuMusic();
    }
    public void OnExit() { }

    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        if (_buttons[0].IsClicked(input)) _ctx.Screens.Replace(new LoginScreen(_ctx));
        if (_buttons[1].IsClicked(input)) _ctx.Screens.Replace(new NewAccountScreen(_ctx));
        if (_buttons[2].IsClicked(input)) _ctx.Screens.Replace(new DeleteAccountScreen(_ctx));
        if (_buttons[3].IsClicked(input)) _ctx.Screens.Replace(new CreditsScreen(_ctx));
        if (_buttons[4].IsClicked(input)) _ctx.ExitGame();
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        SpriteFont btnFont = _ctx.MenuFont ?? font;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, _ctx.State.GameName);
        _buttons[0].Draw(sb, btnFont, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _buttons[1].Draw(sb, btnFont, _input, UiHelper.AccentButtonNormal, UiHelper.AccentButtonHover);
        _buttons[2].Draw(sb, btnFont, _input, UiHelper.DangerButtonNormal, UiHelper.DangerButtonHover);
        _buttons[3].Draw(sb, btnFont, _input);
        _buttons[4].Draw(sb, btnFont, _input);
    }
}
