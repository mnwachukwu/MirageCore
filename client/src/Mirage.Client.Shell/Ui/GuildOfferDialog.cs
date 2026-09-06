using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Shared;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Modal accept/decline overlay for a guild offer — either an invite we received (accept = we join)
/// or a join-request we (as an officer/leader) were asked to approve (accept = the requester joins).
/// </summary>
public sealed class GuildOfferDialog
{
    public bool IsVisible { get; private set; }

    private Action<bool>? _onRespond;   // true = accept, false = decline
    private string _guildName = "";
    private string _otherName = "";
    private GuildOfferKind _kind;
    private readonly Button _acceptBtn = new();
    private readonly Button _declineBtn = new();

    public void Show(string guildName, string otherName, GuildOfferKind kind, Action<bool> onRespond)
    {
        _guildName = guildName;
        _otherName = otherName;
        _kind = kind;
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

        string key = _kind switch
        {
            GuildOfferKind.Request => ClientStrings.GuildOffer_Request,
            GuildOfferKind.Transfer => ClientStrings.GuildOffer_Transfer,
            _ => ClientStrings.GuildOffer_Invite,
        };
        string msg = ClientStrings.Format(key, ("Name", _otherName), ("GuildName", _guildName));
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
