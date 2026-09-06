using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;

namespace Mirage.Client.Shell.Ui;

/// <summary>Modal accept/decline overlay for an incoming direct-trade invite. Mirrors
/// <see cref="GuildOfferDialog"/>: accept opens the trade window, decline dismisses the request.</summary>
public sealed class TradeRequestDialog
{
    public bool IsVisible { get; private set; }

    private string _fromName = "";
    private Action<bool>? _onRespond;
    private readonly Button _acceptBtn = new();
    private readonly Button _declineBtn = new();

    public void Show(string fromName, Action<bool> onRespond)
    {
        _fromName = fromName;
        _onRespond = onRespond;
        IsVisible = true;
    }

    public void Dismiss()
    {
        IsVisible = false;
        _onRespond = null;
    }

    public void Update(InputState input)
    {
        if (!IsVisible) return;
        if (_acceptBtn.IsClicked(input))
        {
            Respond(true);
            return;
        }
        if (_declineBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape)) Respond(false);
    }

    private void Respond(bool accept)
    {
        var cb = _onRespond;
        Dismiss();
        cb?.Invoke(accept);
    }

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, GraphicsDevice gfx)
    {
        if (!IsVisible) return;

        _acceptBtn.Label = ClientStrings.Get(ClientStrings.GuildOffer_Accept);
        _declineBtn.Label = ClientStrings.Get(ClientStrings.GuildOffer_Decline);

        var vp = gfx.Viewport.Bounds;
        UiHelper.DrawFilledRect(sb, vp, Color.Black * 0.65f);

        const int DialogW = 400;
        const int DialogH = 120;
        int dlgX = (vp.Width - DialogW) / 2;
        int dlgY = (vp.Height - DialogH) / 2;
        var dlg = new Rectangle(dlgX, dlgY, DialogW, DialogH);
        UiHelper.DrawFilledRect(sb, dlg, UiHelper.PopupBg);
        UiHelper.DrawBorder(sb, dlg, Color.Gray, 2);

        string msg = ClientStrings.Format(ClientStrings.TradeRequest_Format, ("Name", _fromName));
        var msgSize = font.MeasureString(msg);
        sb.DrawString(font, msg, new Vector2(dlgX + (DialogW - msgSize.X) / 2f, dlgY + 26), Color.White);

        const int BtnW = 100;
        const int BtnH = 28;
        const int BtnGap = 16;
        int btnY = dlgY + DialogH - 14 - BtnH;
        int totalBtnW = BtnW * 2 + BtnGap;
        int btnX = dlgX + (DialogW - totalBtnW) / 2;
        _acceptBtn.Bounds = new Rectangle(btnX, btnY, BtnW, BtnH);
        _declineBtn.Bounds = new Rectangle(btnX + BtnW + BtnGap, btnY, BtnW, BtnH);
        _acceptBtn.Draw(sb, font, input);
        _declineBtn.Draw(sb, font, input);
    }
}
