using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;

namespace Mirage.Client.Shell.Ui;

/// <summary>Reusable free-text prompt overlay — the text-entry cousin of <see cref="NumberPromptDialog"/>.
/// One title line, a text field, and Confirm/Cancel; Esc cancels, clicking positions the caret. Used for
/// the guild-name prompt and the guild MOTD editor. Confirm passes the trimmed text to the callback;
/// empty text is treated as Cancel unless <see cref="AllowEmpty"/> was set at open time (MOTD can be cleared).</summary>
public sealed class TextPromptDialog
{
    private const int OverlayInset = 2;
    private const int TextTop = 12;
    private const int TextX = 8;
    private const int TextInset = 16;
    private const int FieldAboveBtn = 28;
    private const int FieldPad = 4;
    private const int FieldInset = 8;
    private const int FieldH = 22;

    public bool IsOpen { get; private set; }
    public bool IsCapturingInput => IsOpen;
    private bool AllowEmpty { get; set; }

    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    // Rebuilt per Open so each prompt gets its own MaxLength (which is init-only on TextInputField).
    private TextInputField _field = new();
    private Rectangle _fieldRect;
    private InputState _input = new();
    private int _labelsGeneration = -1;

    private string _title = "";
    private Action<string>? _onConfirm;

    public void Open(string title, string initialText, int maxLength, bool allowEmpty, Action<string> onConfirm)
    {
        _title = title;
        _onConfirm = onConfirm;
        AllowEmpty = allowEmpty;
        _field = new TextInputField { MaxLength = maxLength };
        _field.SetText(initialText);
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _onConfirm = null;
    }

    public void Update(InputState input, Rectangle hostBounds, long nowMs)
    {
        if (!IsOpen) return;
        _input = input;

        LayoutButtons(hostBounds);
        _field.Feed(input, nowMs);
        if (input.IsClickIn(_fieldRect))
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _field.HandleMouseClick(input.MousePosition.X, shift);
        }

        // Enter confirms (a natural single-line-field shortcut alongside the Confirm button).
        if (_confirmBtn.IsClicked(input) || input.IsKeyPressed(Keys.Enter))
        {
            string text = _field.Text.Trim();
            if (text.Length > 0 || AllowEmpty) _onConfirm?.Invoke(text);
            Close();
            return;
        }

        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            Close();
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle hostBounds, long nowMs)
    {
        if (!IsOpen) return;

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _confirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }

        LayoutButtons(hostBounds);

        var bgRect = new Rectangle(hostBounds.X + OverlayInset, hostBounds.Y + OverlayInset,
            hostBounds.Width - OverlayInset * 2, hostBounds.Height - OverlayInset * 2);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        UiHelper.DrawLabel(sb, font, _title, new Vector2(hostBounds.X + TextX, hostBounds.Y + TextTop),
            Color.Yellow, hostBounds.Width - TextInset);

        _field.Draw(sb, font, _fieldRect, focused: true, nowMs);
        _confirmBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);
    }

    private void LayoutButtons(Rectangle hostBounds)
    {
        _confirmBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 1);
        int fieldY = _confirmBtn.Bounds.Y - FieldAboveBtn;
        _fieldRect = new Rectangle(hostBounds.X + FieldPad, fieldY, hostBounds.Width - FieldInset, FieldH);
    }
}
