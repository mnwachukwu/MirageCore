using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>Character creation: a name, and one of the appearances this world offers.
///
/// <para>The list comes from the server's greeting, never from the art this client happens to have
/// loaded. Which looks a game offers is authored beside its records — a world may offer one, or four
/// named ones, or a roster drawn from three different sheets — and the screen shows exactly that.</para>
///
/// <para>Whatever an appearance MEANS is the game's business. The engine sends a position in the list
/// and the server turns it into a sprite; nothing here attaches a trait, a role or a statistic to the
/// choice.</para></summary>
public sealed class NewCharScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly TextInputField _nameField = new() { MaxLength = Constants.NameLength };
    private readonly ListBox _appearanceList = new();
    private readonly Button _createBtn;
    private readonly Button _cancelBtn;
    private InputState _input = new();

    // Button captions are captured in the constructor, so a language switch made while this screen
    // is showing would leave them stale — a menu transition rebuilds the screen, but sitting on it
    // does not. Everything else here is fetched inline at draw time and needs no refresh.
    private int _labelsGeneration = -1;
    private string _errorMsg = "";
    private long _lastAnimToggleMs;
    private int _animFrame;

    /// <summary>How long each of the two walk frames is held in the preview. Matches the character
    /// list's resting cadence, so a character looks the same being chosen as being made.</summary>
    private const long WalkAnimMs = 250;

    // ── Layout ────────────────────────────────────────────────────────────────
    // The standard menu dialog: there is a name, a list, and a preview, and nothing that needs the
    // wide template's second column.
    private static readonly Rectangle Dlg = UiHelper.MenuDialogRect;

    private const int ColLX = 261;
    private const int ColLW = 210;
    private const int RowH = 16;

    private static readonly Rectangle NameRect = new(305, 152, 166, 22);
    private static readonly Rectangle AppearanceListRect = new(ColLX, 220, ColLW, 100);   // 5 rows
    private static readonly Rectangle SpriteRect =
        new(ColLX + ColLW + 40, 220, Constants.PicX * 2, Constants.PicY * 2);

    public NewCharScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _createBtn = new Button { Bounds = new Rectangle(295, 414, 96, 28), Label = ClientStrings.Get(ClientStrings.Common_Create) };
        _cancelBtn = new Button { Bounds = new Rectangle(395, 414, 96, 28), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
    }

    private void RefreshLabels()
    {
        _createBtn.Label = ClientStrings.Get(ClientStrings.Common_Create);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }

    /// <summary>Reset the name and list the appearances this world offers.</summary>
    public void OnEnter()
    {
        _nameField.Clear();
        _errorMsg = "";
        _appearanceList.Items.Clear();

        var offered = _ctx.State.Appearances;
        for (int i = 0; i < offered.Count; i++)
        {
            // A world may offer looks without naming them. An unnamed one still needs a row to click,
            // so it is numbered rather than left blank.
            string name = offered[i].Name.TrimEnd();
            _appearanceList.Items.Add(name.Length > 0
                ? name
                : ClientStrings.Format(ClientStrings.NewCharScreen_UnnamedAppearance, ("Number", i + 1)));
        }

        _appearanceList.SelectedIndex = _appearanceList.Items.Count > 0 ? 0 : -1;
    }

    public void OnExit() { }

    /// <summary>Handle typing, list selection, and the submit key.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }

        _nameField.Feed(input, Environment.TickCount64);
        _appearanceList.Update(input, AppearanceListRect);

        if (input.IsKeyPressed(Keys.Enter)) TryCreate();
        if (_createBtn.IsClicked(input)) TryCreate();
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new CharSelectScreen(_ctx));
    }

    /// <summary>Validate the name and selection, then send the create request. The account is already
    /// connected and logged in at this point, so there is no connect step.</summary>
    private void TryCreate()
    {
        if (_nameField.Text.Length < Constants.MinFieldLength)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_NameTooShort);
            return;
        }

        if (_appearanceList.SelectedIndex < 0)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_SelectAppearance);
            return;
        }

        _errorMsg = "";
        _ctx.Sender.SendAddChar(_nameField.Text, _appearanceList.SelectedIndex);
        _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.NewCharScreen_CreatingCharacter));
        _ctx.Screens.Replace(new LoadingScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, the chosen appearance, and any error text.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long now = Environment.TickCount64;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt, Dlg);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.NewCharScreen_Title), Dlg);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_NameLabel),
                      new Vector2(ColLX, NameRect.Y + 4), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_AppearanceLabel),
                      new Vector2(ColLX, AppearanceListRect.Y - RowH), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, focused: true, now);
        _appearanceList.Draw(sb, font, AppearanceListRect);

        DrawAppearancePreview(sb, now);

        if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red, Dlg);

        _createBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);
    }

    /// <summary>The selected appearance, walking in place.</summary>
    private void DrawAppearancePreview(SpriteBatch sb, long nowMs)
    {
        var offered = _ctx.State.Appearances;
        int i = _appearanceList.SelectedIndex;
        if (i < 0 || i >= offered.Count) return;

        if (nowMs - _lastAnimToggleMs >= WalkAnimMs)
        {
            _lastAnimToggleMs = nowMs;
            _animFrame = _animFrame == 0 ? 1 : 0;
        }

        UiHelper.DrawMenuSpritePreview(sb, _ctx.Sprites, offered[i].Sprite, offered[i].SpriteSheet,
                                       _animFrame, SpriteRect);
    }
}
