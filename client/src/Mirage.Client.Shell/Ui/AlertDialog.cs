using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Modal alert overlay — a simple message box.
/// Draw on top of the current screen. Blocks all input while visible.
/// </summary>
public sealed class AlertDialog
{
    public bool IsVisible { get; private set; }
    public string Message { get; private set; } = "";

    private Action? _onOk;
    private Button _okBtn = new();

    private static readonly Color OkButtonBg = new(50, 50, 90);

    public void Show(string message, Action? onOk = null)
    {
        Message = message;
        _onOk = onOk;
        IsVisible = true;
    }

    public void Dismiss()
    {
        IsVisible = false;
        Message = "";
        _onOk = null;
    }

    public void Update(InputState input)
    {
        if (!IsVisible) return;
        if (_okBtn.IsClicked(input))
        {
            var cb = _onOk;
            Dismiss();
            cb?.Invoke();
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, GraphicsDevice gfx)
    {
        if (!IsVisible) return;
        _okBtn.Label = Mirage.Client.Shell.Localization.ClientStrings.Get(
            Mirage.Client.Shell.Localization.ClientStrings.Common_OK);

        var vp = gfx.Viewport.Bounds;

        // Compute dialog height to fit the message.
        int lineH = font.LineSpacing;
        int lines = CountLines(Message);
        int dialogH = PadTop + lines * lineH + TextBtnGap + BtnH + PadBottom;
        dialogH = Math.Max(dialogH, MinDialogH);
        dialogH = Math.Min(dialogH, vp.Height - 40);

        UiHelper.DrawFilledRect(sb, vp, Color.Black * 0.65f);

        int dlgX = (vp.Width - DialogW) / 2;
        int dlgY = (vp.Height - dialogH) / 2;
        var dlg = new Rectangle(dlgX, dlgY, DialogW, dialogH);
        UiHelper.DrawFilledRect(sb, dlg, UiHelper.PopupBg);
        UiHelper.DrawBorder(sb, dlg, Color.Gray, 2);

        // Message — break at word boundaries, advance by LineSpacing (never throws).
        float textX = dlgX + 16f;
        float textY = dlgY + PadTop;
        float textBottom = dlgY + dialogH - PadBottom - BtnH - TextBtnGap;
        string remaining = Message;
        while (remaining.Length > 0 && textY + lineH <= textBottom + 1)
        {
            string chunk;
            if (remaining.Length <= MaxCharsPerLine)
            {
                chunk = remaining;
                remaining = "";
            }
            else
            {
                int cut = remaining.LastIndexOf(' ', MaxCharsPerLine - 1);
                if (cut <= 0) cut = MaxCharsPerLine;
                chunk = remaining[..cut].TrimEnd();
                remaining = remaining[cut..].TrimStart();
            }
            sb.DrawString(font, chunk, new Vector2(textX, textY), Color.White);
            textY += lineH;
        }

        // OK button — centered at the bottom of the dialog.
        _okBtn.Bounds = new Rectangle(dlgX + (DialogW - 100) / 2, dlgY + dialogH - PadBottom - BtnH, 100, BtnH);
        UiHelper.DrawFilledRect(sb, _okBtn.Bounds, OkButtonBg);
        UiHelper.DrawBorder(sb, _okBtn.Bounds, Color.Gray);
        sb.DrawString(font, _okBtn.Label, UiHelper.CenterText(font, _okBtn.Label, _okBtn.Bounds), Color.White);
    }

    private static int CountLines(string message)
    {
        if (message.Length == 0) return 1;
        int count = 0;
        string rem = message;
        while (rem.Length > 0)
        {
            count++;
            if (rem.Length <= MaxCharsPerLine) break;
            int cut = rem.LastIndexOf(' ', MaxCharsPerLine - 1);
            if (cut <= 0) cut = MaxCharsPerLine;
            rem = rem[cut..].TrimStart();
        }
        return count;
    }

    // Approximate chars per line for the dialog width at the default font size.
    // Character-count wrapping avoids per-word MeasureString calls inside the draw loop.
    private const int MaxCharsPerLine = 42;

    private const int DialogW = 360;
    private const int PadTop = 20;
    private const int PadBottom = 12;
    private const int BtnH = 32;
    private const int TextBtnGap = 10;
    private const int MinDialogH = 90;
}
