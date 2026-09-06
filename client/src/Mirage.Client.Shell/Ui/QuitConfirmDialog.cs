using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Modal quit-confirmation overlay.
/// Outside combat: Quit / Logout / Cancel.
/// In combat: Quit / Cancel (logout blocked to avoid leaving a ghost).
/// </summary>
public sealed class QuitConfirmDialog
{
    public bool IsVisible { get; private set; }

    private Action? _onConfirm;
    private Action? _onLogout;
    private bool _inCombat;
    private readonly Button _quitBtn = new();
    private readonly Button _logoutBtn = new();
    private readonly Button _cancelBtn = new();

    private static readonly Color LogoutButtonNormal = new(30, 50, 80);
    private static readonly Color LogoutButtonHover = new(50, 80, 120);

    public void Show(Action onConfirm, bool inCombat = false, Action? onLogout = null)
    {
        _onConfirm = onConfirm;
        _onLogout = onLogout;
        _inCombat = inCombat;
        IsVisible = true;
    }

    public void Dismiss()
    {
        IsVisible = false;
        _onConfirm = null;
        _onLogout = null;
    }

    public void Update(InputState input)
    {
        if (!IsVisible) return;
        if (_quitBtn.IsClicked(input))
        {
            var cb = _onConfirm;
            Dismiss();
            cb?.Invoke();
            return;
        }
        if (!_inCombat && _logoutBtn.IsClicked(input))
        {
            var cb = _onLogout;
            Dismiss();
            cb?.Invoke();
            return;
        }
        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
            Dismiss();
    }

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, GraphicsDevice gfx)
    {
        if (!IsVisible) return;

        _quitBtn.Label = ClientStrings.Get(ClientStrings.QuitConfirm_Quit);
        _logoutBtn.Label = ClientStrings.Get(ClientStrings.QuitConfirm_Logout);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);

        var vp = gfx.Viewport.Bounds;
        UiHelper.DrawFilledRect(sb, vp, Color.Black * 0.65f);

        const int DialogW = 340;
        int DialogH = _inCombat ? 140 : 110;
        int dlgX = (vp.Width - DialogW) / 2;
        int dlgY = (vp.Height - DialogH) / 2;
        var dlg = new Rectangle(dlgX, dlgY, DialogW, DialogH);
        UiHelper.DrawFilledRect(sb, dlg, UiHelper.PopupBg);
        UiHelper.DrawBorder(sb, dlg, Color.Gray, 2);

        string msg = ClientStrings.Get(ClientStrings.QuitConfirm_Prompt);
        var msgSize = font.MeasureString(msg);
        sb.DrawString(font, msg, new Vector2(dlgX + (DialogW - msgSize.X) / 2f, dlgY + 18), Color.White);

        if (_inCombat)
        {
            string warnLine1 = ClientStrings.Get(ClientStrings.QuitConfirm_CombatWarnLine1);
            string warnLine2 = ClientStrings.Get(ClientStrings.QuitConfirm_CombatWarnLine2);
            var w1Size = font.MeasureString(warnLine1);
            var w2Size = font.MeasureString(warnLine2);
            var warnColor = Color.Red;
            sb.DrawString(font, warnLine1, new Vector2(dlgX + (DialogW - w1Size.X) / 2f, dlgY + 44), warnColor);
            sb.DrawString(font, warnLine2, new Vector2(dlgX + (DialogW - w2Size.X) / 2f, dlgY + 62), warnColor);
        }

        const int BtnW = 88;
        const int BtnH = 28;
        const int BtnGap = 12;
        int btnY = dlgY + DialogH - 14 - BtnH;

        if (_inCombat)
        {
            // Two-button layout: Quit | Cancel
            int totalBtnW = BtnW * 2 + BtnGap;
            int btnX = dlgX + (DialogW - totalBtnW) / 2;
            _quitBtn.Bounds = new Rectangle(btnX, btnY, BtnW, BtnH);
            _cancelBtn.Bounds = new Rectangle(btnX + BtnW + BtnGap, btnY, BtnW, BtnH);
            _quitBtn.Draw(sb, font, input, UiHelper.DangerButtonNormal, UiHelper.DangerButtonHover);
            _cancelBtn.Draw(sb, font, input);
        }
        else
        {
            // Three-button layout: Quit | Logout | Cancel
            int totalBtnW = BtnW * 3 + BtnGap * 2;
            int btnX = dlgX + (DialogW - totalBtnW) / 2;
            _quitBtn.Bounds = new Rectangle(btnX, btnY, BtnW, BtnH);
            _logoutBtn.Bounds = new Rectangle(btnX + BtnW + BtnGap, btnY, BtnW, BtnH);
            _cancelBtn.Bounds = new Rectangle(btnX + (BtnW + BtnGap) * 2, btnY, BtnW, BtnH);
            _quitBtn.Draw(sb, font, input, UiHelper.DangerButtonNormal, UiHelper.DangerButtonHover);
            _logoutBtn.Draw(sb, font, input, LogoutButtonNormal, LogoutButtonHover);
            _cancelBtn.Draw(sb, font, input);
        }
    }
}
