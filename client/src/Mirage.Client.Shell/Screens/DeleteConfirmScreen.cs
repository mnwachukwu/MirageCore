using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Screens;

/// <summary>Final confirmation before deleting one character, naming the character explicitly so a
/// mis-clicked slot is caught before the request is sent.</summary>
public sealed class DeleteConfirmScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly int _slot;
    private readonly string _charName;
    private readonly Button _deleteBtn;
    private readonly Button _cancelBtn;
    private InputState _input = new();
    // Button captions are captured in the constructor, so a language switch made while this screen
    // is showing would leave them stale. The prompt and warning lines are fetched inline at draw
    // time and need no refresh.
    private int _labelsGeneration = -1;

    private void RefreshLabels()
    {
        _deleteBtn.Label = ClientStrings.Get(ClientStrings.Common_Delete);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }

    private const int DlgW = 380;
    private const int DlgH = 120;
    private static readonly Color WarningTextColor = new(200, 80, 80);

    public DeleteConfirmScreen(ShellContext ctx, int slot, string charName)
    {
        _ctx = ctx;
        _slot = slot;
        _charName = charName;

        int dlgX = (UiHelper.RefW - DlgW) / 2;
        int dlgY = (UiHelper.RefH - DlgH) / 2;
        const int BtnW = 110;
        const int BtnH = 28;
        const int BtnGap = 12;
        int totalBtnW = BtnW * 2 + BtnGap;
        int btnY = dlgY + DlgH - 14 - BtnH;
        int btnX = dlgX + (DlgW - totalBtnW) / 2;

        _deleteBtn = new Button { Bounds = new Rectangle(btnX, btnY, BtnW, BtnH), Label = ClientStrings.Get(ClientStrings.Common_Delete) };
        _cancelBtn = new Button { Bounds = new Rectangle(btnX + BtnW + BtnGap, btnY, BtnW, BtnH), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
    }

    /// <summary>No setup needed — the slot and character name are supplied by the constructor.</summary>
    public void OnEnter() { }
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

        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            _ctx.Screens.Pop();
            return;
        }

        if (_deleteBtn.IsClicked(input))
        {
            _ctx.Sender.SendDelChar(_slot);
            _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.DeleteConfirmScreen_DeletingCharacter));
            _ctx.Screens.Replace(new LoadingScreen(_ctx));
        }
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        int dlgX = (UiHelper.RefW - DlgW) / 2;
        int dlgY = (UiHelper.RefH - DlgH) / 2;
        var dlg = new Rectangle(dlgX, dlgY, DlgW, DlgH);

        UiHelper.DrawFilledRect(sb, dlg, UiHelper.PopupBg);
        UiHelper.DrawBorder(sb, dlg, UiHelper.UiControlBorder, 2);

        string line1 = ClientStrings.Format(ClientStrings.DeleteConfirmScreen_PromptFormat, ("Name", _charName));
        string line2 = ClientStrings.Get(ClientStrings.DeleteConfirmScreen_Warning);
        var sz1 = font.MeasureString(line1);
        var sz2 = font.MeasureString(line2);
        sb.DrawString(font, line1, new Vector2(dlgX + (DlgW - sz1.X) / 2f, dlgY + 18), Color.White);
        sb.DrawString(font, line2, new Vector2(dlgX + (DlgW - sz2.X) / 2f, dlgY + 38), WarningTextColor);

        _deleteBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);
    }
}
