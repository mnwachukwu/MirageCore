using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

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

    // What this game asks at creation, and the list drawn for each. Two lists in step: the choice
    // says what to send back, the list says what was picked.
    private readonly List<CreationChoice> _asked = [];
    private readonly List<ListBox> _askedLists = [];
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
    //
    // The WIDE dialog, which exists for this screen: it is the only one carrying a text field, two
    // lists and a sprite preview at once, and the narrow template has one content column to put them
    // in. Everything below is measured from the content column so nothing is ever drawn over the art
    // panel, which is what the narrow rectangle did to all of it.
    private static readonly Rectangle Dlg = UiHelper.WideMenuDialogRect;

    // Where the art stops and the screen's own space begins.
    private static readonly int ContentX = Dlg.X + UiHelper.MenuDlgArtW;

    private const int Pad = 16;
    private const int RowH = 16;

    // Two columns of controls under a full-width name row, with the preview framed in the top right.
    private const int ColW = 200;
    private static readonly int ColAX = ContentX + Pad;
    private static readonly int ColBX = ColAX + ColW + 24;

    private static readonly Rectangle NameRect = new(ColAX, Dlg.Y + 62, 300, 24);

    private static readonly Rectangle AppearanceListRect = new(ColAX, Dlg.Y + 126, ColW, 100);

    // Framed, so it reads as part of the dialog instead of floating loose beside it.
    private static readonly Rectangle SpriteFrame =
        new(Dlg.Right - Pad - 80, Dlg.Y + 44, 80, 80);
    private static readonly Rectangle SpriteRect =
        new(SpriteFrame.X + (SpriteFrame.Width - Constants.PicX * 2) / 2,
            SpriteFrame.Y + (SpriteFrame.Height - Constants.PicY * 2) / 2,
            Constants.PicX * 2, Constants.PicY * 2);

    public NewCharScreen(ShellContext ctx)
    {
        _ctx = ctx;
        // Centred under the content column, which is where every other menu dialog puts its buttons.
        int pairX = ContentX + (Dlg.Right - ContentX - 200) / 2;
        _createBtn = new Button { Bounds = new Rectangle(pairX, Dlg.Bottom - 46, 96, 30), Label = ClientStrings.Get(ClientStrings.Common_Create) };
        _cancelBtn = new Button { Bounds = new Rectangle(pairX + 104, Dlg.Bottom - 46, 96, 30), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
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

        BuildAskedLists();
    }

    /// <summary>A list per thing this game asks at creation, filled from what the server resolved.
    ///
    /// <para>🔴 <b>Built here rather than in the constructor</b>, because the greeting arrives before
    /// this screen exists but a reconnect to a different server replaces it — so the lists are the ones
    /// the server this player is on actually sent.</para>
    ///
    /// <para>⚠ A question whose list came back EMPTY is dropped rather than drawn. A world that declared
    /// classes and authored none would otherwise show a heading over nothing, and the player could not
    /// get past a list with no rows in it.</para></summary>
    private void BuildAskedLists()
    {
        _asked.Clear();
        _askedLists.Clear();

        foreach (CreationChoice choice in _ctx.State.Asked)
        {
            if (choice.Options.Count == 0) continue;

            var list = new ListBox();
            foreach (CreationOption option in choice.Options) list.Items.Add(option.Name);
            list.SelectedIndex = 0;

            _asked.Add(choice);
            _askedLists.Add(list);
        }
    }

    /// <summary>Where the list for the Nth question sits: the second column, stacked. The dialog has
    /// room for two — a game asking more than that wants a screen of its own.</summary>
    private static Rectangle AskedRect(int which) =>
        new(ColBX, AppearanceListRect.Y + which * (AskedListH + RowH + 6), ColW, AskedListH);

    private const int AskedListH = 44;

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

        for (int i = 0; i < _askedLists.Count; i++) _askedLists[i].Update(input, AskedRect(i));

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
        // The record's own number, not the row - a list drops the blank slots, so the two differ.
        var chose = new int[_asked.Count];
        for (int i = 0; i < _asked.Count; i++)
        {
            int row = _askedLists[i].SelectedIndex;
            chose[i] = row >= 0 && row < _asked[i].Options.Count ? _asked[i].Options[row].Num : 0;
        }

        _ctx.Sender.SendAddChar(_nameField.Text, _appearanceList.SelectedIndex, chose);
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
                      new Vector2(ColAX, NameRect.Y - RowH), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_AppearanceLabel),
                      new Vector2(ColAX, AppearanceListRect.Y - RowH), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, focused: true, now);
        _appearanceList.Draw(sb, font, AppearanceListRect);

        for (int i = 0; i < _askedLists.Count; i++)
        {
            var at = AskedRect(i);
            sb.DrawString(font, _asked[i].LabelKey, new Vector2(at.X, at.Y - RowH),
                          UiHelper.DlgLabelColor);
            _askedLists[i].Draw(sb, font, at);
        }

        DrawAppearancePreview(sb, now);

        if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red, Dlg);

        _createBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);
    }

    /// <summary>The selected appearance, walking in place.</summary>
    private void DrawAppearancePreview(SpriteBatch sb, long nowMs)
    {
        UiHelper.DrawFilledRect(sb, SpriteFrame, UiHelper.DlgArtColor);
        UiHelper.DrawBorder(sb, SpriteFrame, UiHelper.DlgBorderColor);

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
