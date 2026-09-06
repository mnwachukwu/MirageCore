using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Shared.Security;

namespace Mirage.Client.Shell.Ui;

/// <summary>The refusal a changed server certificate earns, as a prompt rather than a dead end: it names
/// the server, shows the fingerprint on record beside the one just offered, and offers to drop the pin so
/// the next connection records the new certificate. Confirming is the only supported way to clear a pin
/// from the client, so it is drawn in the danger colors and never the default action.</summary>
public sealed class IdentityChangedPrompt
{
    private const int TextPad = 12;

    private readonly Button _trustBtn = new();
    private readonly Button _cancelBtn = new();
    private InputState _input = new();
    private ServerIdentityChangedException? _change;
    private int _labelsGeneration = -1;
    private string _message = "";

    public bool IsOpen => _change is not null;

    public void Open(ServerIdentityChangedException change)
    {
        _change = change;
        // Assembled here rather than held as one string: the SpriteFont draws no newline, so a localized
        // value may not contain one.
        _message = string.Join("\n\n",
            ClientStrings.Format(ClientStrings.Common_ServerIdentityChangedDetail,
                ("Host", change.Host), ("Port", change.Port)),
            ClientStrings.Get(ClientStrings.Common_ServerIdentityOnRecord) + "\n"
                + ServerPins.ForDisplay(change.Expected),
            ClientStrings.Get(ClientStrings.Common_ServerIdentityOffered) + "\n"
                + ServerPins.ForDisplay(change.Actual),
            ClientStrings.Get(ClientStrings.Common_ServerIdentityAdvice));
    }

    public void Close()
    {
        _change = null;
        _message = "";
    }

    /// <summary>True on the frame the pin was dropped, so the caller can say so and let the player retry.</summary>
    public bool Update(InputState input)
    {
        if (_change is not { } change) return false;
        _input = input;

        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            Close();
            return false;
        }

        if (!_trustBtn.IsClicked(input)) return false;
        input.ConsumeMouseClick();
        ServerPinStore.Store.Forget(change.Host, change.Port);
        Close();
        return true;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle viewport)
    {
        if (!IsOpen) return;

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _trustBtn.Label = ClientStrings.Get(ClientStrings.Common_TrustNewCertificate);
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }

        UiHelper.DrawMenuDialog(sb, viewport, out _, out var content, rect: UiHelper.WideMenuDialogRect);
        UiHelper.DrawMenuTitle(sb, font, ClientStrings.Get(ClientStrings.Common_ServerIdentityChangedTitle),
            UiHelper.WideMenuDialogRect);
        UiHelper.DrawWrapped(sb, font, _message, content.X + TextPad, content.Y + TextPad,
            content.Width - TextPad * 2, Color.White, font.LineSpacing);

        _trustBtn.Bounds = UiHelper.PanelBottomButton(content, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(content, 1);
        _trustBtn.Draw(sb, font, _input, UiHelper.DangerButtonNormal, UiHelper.DangerButtonHover);
        _cancelBtn.Draw(sb, font, _input);
    }
}
