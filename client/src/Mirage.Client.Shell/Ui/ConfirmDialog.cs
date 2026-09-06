using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;

namespace Mirage.Client.Shell.Ui;

/// <summary>Reusable yes/no confirmation overlay — the message cousin of <see cref="TextPromptDialog"/>: a
/// wrapped message with Confirm / Cancel (Enter confirms, Esc cancels). Drawn inside a host panel's body
/// rect and gated by <see cref="IsCapturingInput"/> like the other in-panel overlays.</summary>
public sealed class ConfirmDialog
{
    private const int OverlayInset = 2;
    private const int TextTop = 14;
    private const int TextX = 10;
    private const int TextInset = 20;

    public bool IsOpen { get; private set; }
    public bool IsCapturingInput => IsOpen;

    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    private InputState _input = new();
    private int _labelsGeneration = -1;
    private string _message = "";
    private Action? _onConfirm;

    public void Open(string message, Action onConfirm)
    {
        _message = message;
        _onConfirm = onConfirm;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _onConfirm = null;
    }

    public void Update(InputState input)
    {
        if (!IsOpen) return;
        _input = input;

        if (_confirmBtn.IsClicked(input) || input.IsKeyPressed(Keys.Enter))
        {
            var cb = _onConfirm;
            Close();
            cb?.Invoke();
            return;
        }
        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            Close();
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle hostBounds)
    {
        if (!IsOpen) return;

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _confirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }

        _confirmBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 1);

        var bg = new Rectangle(hostBounds.X + OverlayInset, hostBounds.Y + OverlayInset,
            hostBounds.Width - OverlayInset * 2, hostBounds.Height - OverlayInset * 2);
        UiHelper.DrawFilledRect(sb, bg, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bg, UiHelper.ConfirmOverlayBorder);

        DrawWrapped(sb, font, _message, hostBounds.X + TextX, hostBounds.Y + TextTop,
            hostBounds.Width - TextInset, Color.White);

        _confirmBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);
    }

    private static void DrawWrapped(SpriteBatch sb, SpriteFont font, string text, float x, float y, float maxWidth, Color color)
    {
        UiHelper.DrawWrapped(sb, font, text, x, y, maxWidth, color, font.LineSpacing);
    }
}
