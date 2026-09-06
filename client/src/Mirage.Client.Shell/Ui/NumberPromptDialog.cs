using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;

namespace Mirage.Client.Shell.Ui;

/// <summary>Reusable "How many?" overlay used by Bank deposit/withdraw X, Inventory drop X,
/// and any future site that needs a number prompt. Lifts the per-panel TextInputField + Confirm/
/// Cancel pattern into one place so the keyboard handling, Esc-cancels, mouse-click-to-position
/// caret, and overlay styling all live behind one widget.</summary>
public sealed class NumberPromptDialog
{
    private const int OverlayInset = 2;
    private const int TextTop = 12;
    private const int TextX = 8;
    private const int TextInset = 16;
    private const int LineGap = 18;
    private const int AmountPromptGap = 22;
    private const int AmountFieldAboveBtn = 28;
    private const int AmountFieldPad = 4;
    private const int AmountFieldInset = 8;
    private const int AmountFieldH = 22;
    private const int AmountFieldMaxLen = 10;

    public bool IsOpen { get; private set; }
    public bool IsCapturingInput => IsOpen;

    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    private readonly TextInputField _amountField = new() { MaxLength = AmountFieldMaxLen };
    private Rectangle _amountFieldRect;
    private InputState _input = new();
    private int _labelsGeneration = -1;

    private string _actionLabel = "";
    private string _subjectLabel = "";
    private int _max;
    private Action<int>? _onConfirm;

    // Whether an over-max entry is refused rather than quietly reduced, and the line shown when it is.
    private bool _rejectOverMax;
    private string? _warning;

    /// <summary>Open the dialog. <paramref name="actionLabel"/> is the top line (e.g.
    /// "Deposit item:"), <paramref name="subjectLabel"/> the second line (e.g. the item name).
    /// <paramref name="max"/> is shown in the "Amount (max N):" prompt and clamps the entered
    /// value; values outside 1..max silently cancel on confirm (existing convention).</summary>
    /// <param name="rejectOverMax">Refuse an entry above <paramref name="max"/> and say so, instead of
    /// silently sending <paramref name="max"/>.
    /// <para>Which mode a site wants follows from what its maximum MEANS. A cap on what EXISTS — the coins
    /// in the vault, the copies in the bag — clamps: "sell 100" of a stack of five plainly means all five,
    /// and substituting it answers the question asked. A cap on what FITS — bag room on a purchase or a
    /// barter — rejects: buying thirty-three potions is not buying fifty, and quietly doing the smaller
    /// one answers a question nobody asked.</para></param>
    public void Open(string actionLabel, string subjectLabel, int max, Action<int> onConfirm,
                     bool rejectOverMax = false)
    {
        _actionLabel = actionLabel;
        _subjectLabel = subjectLabel;
        _max = max;
        _onConfirm = onConfirm;
        _rejectOverMax = rejectOverMax;
        _warning = null;
        _amountField.SetText("1");
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _onConfirm = null;
        _warning = null;
    }

    public void Update(InputState input, Rectangle hostBounds, long nowMs)
    {
        if (!IsOpen) return;
        _input = input;

        LayoutButtons(hostBounds);
        FeedAmountField(input, nowMs);

        if (_confirmBtn.IsClicked(input))
        {
            // Clamp (not reject) is the default: entering 20 when 10 are available sends 10, so the
            // request flows through the same partial-move path as server-side capacity clamping, and
            // rejecting would trip server hacking detection on Drop currency, which validates
            // Value > inv.Value. A caller that opts into rejection gets told instead, because there the
            // smaller number is a different request rather than a lesser one.
            if (int.TryParse(_amountField.Text, out int amt) && amt >= 1)
            {
                if (_rejectOverMax && amt > _max)
                {
                    _warning = ClientStrings.Format(ClientStrings.NumberPrompt_OverMax, ("Max", _max));
                    return;
                }
                _onConfirm?.Invoke(Math.Min(amt, _max));
            }
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

        var bgRect = new Rectangle(
            hostBounds.X + OverlayInset,
            hostBounds.Y + OverlayInset,
            hostBounds.Width - OverlayInset * 2,
            hostBounds.Height - OverlayInset * 2);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        float textY = hostBounds.Y + TextTop;
        UiHelper.DrawLabel(sb, font, _actionLabel, new Vector2(hostBounds.X + TextX, textY), Color.LightGray, hostBounds.Width - TextInset);
        textY += LineGap;
        UiHelper.DrawLabel(sb, font, _subjectLabel, new Vector2(hostBounds.X + TextX, textY), Color.White, hostBounds.Width - TextInset);
        textY += AmountPromptGap;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.BankPanel_AmountPrompt, ("Max", _max)), new Vector2(hostBounds.X + TextX, textY), Color.Yellow, hostBounds.Width - TextInset);

        // Sits under the maximum it refers to, so the number and the objection to it read together.
        if (_warning is not null)
        {
            textY += LineGap;
            UiHelper.DrawLabel(sb, font, _warning, new Vector2(hostBounds.X + TextX, textY), Color.OrangeRed, hostBounds.Width - TextInset);
        }

        _amountField.Draw(sb, font, _amountFieldRect, focused: true, nowMs);
        _confirmBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);
    }

    private void LayoutButtons(Rectangle hostBounds)
    {
        _confirmBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(hostBounds, 1);
        int fieldY = _confirmBtn.Bounds.Y - AmountFieldAboveBtn;
        _amountFieldRect = new Rectangle(hostBounds.X + AmountFieldPad, fieldY, hostBounds.Width - AmountFieldInset, AmountFieldH);
    }

    private void FeedAmountField(InputState input, long nowMs)
    {
        _amountField.Feed(input, nowMs);
        if (input.IsClickIn(_amountFieldRect))
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _amountField.HandleMouseClick(input.MousePosition.X, shift);
        }
    }
}
