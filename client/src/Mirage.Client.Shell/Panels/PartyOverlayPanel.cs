using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Compact party-partner overlay, in the sidebar's free space below the Logout button.  Only drawn while
/// the local player has a partner — the snapshot lives on <c>state.Party</c> and is pushed by the
/// server.  Bars share fills and label format with the right-sidebar HUD (via
/// <see cref="UiHelper.DrawVitalBar"/>) and stack flush against each other inside a panel; a single
/// outline wraps the three-bar block.  Outline picks up the in-world bar treatment: amber while the
/// partner is in combat, cyan when the local player is targeting them, otherwise white.  Proximity
/// (partner's map in our 3×3 observable area, same rule the 1.2× party EXP bonus uses) drives a
/// 0.7/0.4 alpha tint on the whole overlay, so how close the partner is reads at a glance.
/// </summary>
public sealed class PartyOverlayPanel
{
    // Layout — the sidebar's free space, centered under the Logout button. Everything below is measured
    // from (X,Y), so the whole panel and its hit boxes travel together.
    private static readonly Point Anchor = HudPanel.FreeSpaceAnchor(PanelW);
    private static int X => Anchor.X;
    private static int Y => Anchor.Y;

    /// <summary>The panel's outer rectangle, where it is drawn.</summary>
    internal static Rectangle Bounds => new(X, Y, PanelW, PanelH);

    private const int InnerW = 152;
    private const int HeaderH = 14;
    private const int BarH = 12;
    private const int Pad = 4;
    private const int HeaderBarGap = Pad;              // gap between header and bars, matches top/bottom pad
    private const int BarsH = BarH * 3;                // bars touch, no row gap
    private const int PanelW = InnerW + Pad * 2;
    private const int PanelH = HeaderH + HeaderBarGap + BarsH + Pad * 2;

    // Panel chrome — subtle dark backing with a thin border, both alpha-tinted by proximity.
    private static readonly Color PanelBg = new(15, 15, 25, 200);
    private static readonly Color PanelBorder = new(80, 80, 120);

    // ── Animated bar ratios (mirror HudPanel.Tick) ────────────────────────────
    private const float LerpSpeed = 5f;

    // ── Bar slot text cache — recomputed only when current/max changes ────────
    private struct BarSlot
    {
        public readonly string LabelKey;
        public long Current = -1, Max = -1;
        public string Text = "";
        public BarSlot(string labelKey) { LabelKey = labelKey; }
    }
    private BarSlot _hpSlot = new(ClientStrings.Stats_Hp);
    private BarSlot _mpSlot = new(ClientStrings.Stats_Mp);
    private BarSlot _spSlot = new(ClientStrings.Stats_Sp);

    private int _cachedLevel = -1;
    private string _cachedLevelStr = "";

    // Trails ClientStrings.Generation so a language switch invalidates the two caches above, both of
    // which hold a resolved string keyed on a number. See the block at the top of Draw.
    private int _labelsGeneration = -1;

    // ── Leave-party close button + inline confirmation ───────────────────────
    // Showing the × glyph and the "Leave the party?" Yes/No dialog when toggled. The
    // confirmation reuses the same panel rect — title becomes "Party" while active.
    private bool _confirmingLeave;
    private Rectangle _closeBtnRect;
    private Rectangle _yesBtnRect;
    private Rectangle _noBtnRect;
    private const int CloseSize = 12;
    private const int CloseGap = 3;   // gap between the right-aligned "Lv. N" and the × glyph
    private const int ConfirmBtnW = 56;
    private const int ConfirmBtnH = 16;

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!state.Party.Active)
        {
            _confirmingLeave = false;
            return;
        }

        if (_confirmingLeave)
        {
            if (input.IsClickIn(_yesBtnRect))
            {
                _confirmingLeave = false;
                sender.SendLeaveParty();
                input.ConsumeMouseClick();
            }
            else if (input.IsClickIn(_noBtnRect))
            {
                _confirmingLeave = false;
                input.ConsumeMouseClick();
            }
        }
        else
        {
            if (input.IsClickIn(_closeBtnRect))
            {
                _confirmingLeave = true;
                input.ConsumeMouseClick();
            }
        }
    }

    public void Tick(ClientState state, float deltaSeconds)
    {
        var party = state.Party;
        if (!party.Active || party.MaxHp <= 0) return;

        float targetHp = party.MaxHp > 0 ? Math.Clamp((float)party.Hp / party.MaxHp, 0f, 1f) : 0f;
        float targetMp = party.MaxMp > 0 ? Math.Clamp((float)party.Mp / party.MaxMp, 0f, 1f) : 0f;
        float targetSp = party.MaxSp > 0 ? Math.Clamp((float)party.Sp / party.MaxSp, 0f, 1f) : 0f;

        bool snap = party.DispHp < 0f || party.SnapVitals;
        party.SnapVitals = false;

        if (snap)
        {
            party.DispHp = targetHp;
            party.DispMp = targetMp;
            party.DispSp = targetSp;
            return;
        }

        float t = Math.Min(1f, LerpSpeed * deltaSeconds);
        party.DispHp += (targetHp - party.DispHp) * t;
        party.DispMp += (targetMp - party.DispMp) * t;
        party.DispSp += (targetSp - party.DispSp) * t;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, InputState input,
        TargetRef tabTarget, long nowMs)
    {
        var party = state.Party;
        if (!party.Active)
        {
            _confirmingLeave = false;
            return;
        }

        // The bar text and the level line cache a localized string against the number that produced
        // it, so neither moves when the language does. Clear the keys and let the normal rebuilds run.
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _hpSlot.Current = _mpSlot.Current = _spSlot.Current = -1;
            _cachedLevel = -1;
        }

        if (_confirmingLeave)
        {
            DrawLeaveConfirm(sb, font);
            return;
        }

        // Nearby = partner's map is in our 3×3 ObservableArea — same rule the party EXP bonus uses.
        bool nearby = state.CellForMap(party.MapNum) is not null;
        float alpha = nearby ? 0.7f : 0.4f;

        // Outline rule mirrors the in-world bars at GameplayScreen.cs lines 696–701: combat (amber) >
        // targeted (cyan, gray when out of range) > white default.  Drawn once around the three-bar
        // group, not per bar.
        bool inCombat = party.LastCombatTickMs > 0 && (nowMs - party.LastCombatTickMs) < 10_000;
        bool targeted = tabTarget.Kind == TargetKind.Player && tabTarget.A == party.Index;
        Color outline;
        if (inCombat) outline = UiHelper.WorldBarCombatColor;
        else if (targeted) outline = nearby ? Color.Cyan : Color.Gray;
        else outline = Color.White;

        // 1) Drop shadow (shared with DraggablePanel + dialog popups + chat bubbles), then panel
        //    backing + border. Shadow multiplies through the proximity alpha along with the rest.
        var shadowRect = new Rectangle(X + UiHelper.PanelShadowOffset, Y + UiHelper.PanelShadowOffset, PanelW, PanelH);
        UiHelper.DrawFilledRect(sb, shadowRect, UiHelper.PanelShadowColor * alpha);
        var panelRect = new Rectangle(X, Y, PanelW, PanelH);
        UiHelper.DrawFilledRect(sb, panelRect, PanelBg * alpha);
        UiHelper.DrawBorder(sb, panelRect, PanelBorder * alpha);

        // 2) Header row — name left, "Lv. N" right-aligned.  White for contrast against the dark
        //    panel backing, but the PK red still wins so the partner's status reads at a glance;
        //    access-level coloring is intentionally dropped (rarely matters here).  Grayed when not
        //    nearby.
        if (party.Level != _cachedLevel)
        {
            _cachedLevel = party.Level;
            _cachedLevelStr = ClientStrings.Format(ClientStrings.Common_LevelFormat, ("Level", party.Level));
        }
        Color headerColor = !nearby ? Color.DimGray
            : party.ShowAsPk ? ChatPanel.GetColor(GameColor.BrightRed)
            : Color.White;
        // Close (×) rect is computed up-front so the right-aligned level can stop short of it — a
        // wide "Lv. 255" would otherwise slide under the glyph in the top-right corner.
        _closeBtnRect = new Rectangle(X + PanelW - CloseSize - 2, Y + 2, CloseSize, CloseSize);
        int innerX = X + Pad;
        int headerY = Y + Pad;
        float levelW = font.MeasureString(_cachedLevelStr).X;
        float levelX = _closeBtnRect.Left - CloseGap - levelW;
        string fittedName = UiHelper.FitText(font, party.Name, levelX - innerX - 6);
        sb.DrawString(font, fittedName, new Vector2(innerX, headerY), headerColor * alpha);
        sb.DrawString(font, _cachedLevelStr, new Vector2(levelX, headerY), headerColor * alpha);

        // 3) Three flush vital bars (no per-bar outline; group outline drawn after).
        int barY = Y + Pad + HeaderH + HeaderBarGap;
        DrawBar(sb, font, new Rectangle(innerX, barY, InnerW, BarH), party.DispHp, party.Hp, party.MaxHp, UiHelper.VitalHpColor, alpha, ref _hpSlot, input);
        DrawBar(sb, font, new Rectangle(innerX, barY + BarH, InnerW, BarH), party.DispMp, party.Mp, party.MaxMp, UiHelper.VitalMpColor, alpha, ref _mpSlot, input);
        DrawBar(sb, font, new Rectangle(innerX, barY + BarH * 2, InnerW, BarH), party.DispSp, party.Sp, party.MaxSp, UiHelper.VitalSpColor, alpha, ref _spSlot, input);

        // 4) One outline around the whole three-bar block.
        UiHelper.DrawBorder(sb, new Rectangle(innerX, barY, InnerW, BarsH), outline * alpha);

        // 5) Close (×) glyph in the panel's top-right (rect computed in the header step above).
        //    Click opens the leave-party confirmation.
        bool closeHover = _closeBtnRect.Contains(input.MousePosition);
        var closeColor = (closeHover ? Color.White : Color.LightGray) * alpha;
        sb.DrawString(font, "x", new Vector2(_closeBtnRect.X + 3, _closeBtnRect.Y - 2), closeColor);
    }

    /// <summary>"Party — Leave the party?" confirmation that replaces the bars in-place. Yes
    /// fires LeavePartyPacket via the sender (same as the `/leave` slash command). Drawn at full
    /// opacity so the confirmation reads regardless of proximity-fade state.</summary>
    private void DrawLeaveConfirm(SpriteBatch sb, SpriteFont font)
    {
        var shadowRect = new Rectangle(X + UiHelper.PanelShadowOffset, Y + UiHelper.PanelShadowOffset, PanelW, PanelH);
        UiHelper.DrawFilledRect(sb, shadowRect, UiHelper.PanelShadowColor);
        var panelRect = new Rectangle(X, Y, PanelW, PanelH);
        UiHelper.DrawFilledRect(sb, panelRect, PanelBg);
        UiHelper.DrawBorder(sb, panelRect, PanelBorder);

        int innerX = X + Pad;
        int headerY = Y + Pad;
        sb.DrawString(font, ClientStrings.Get(ClientStrings.PartyOverlay_ConfirmTitle), new Vector2(innerX, headerY), Color.White);

        string body = ClientStrings.Get(ClientStrings.PartyOverlay_ConfirmBody);
        int bodyY = Y + Pad + HeaderH + HeaderBarGap;
        sb.DrawString(font, body, new Vector2(innerX, bodyY), Color.White);

        int btnY = Y + PanelH - Pad - ConfirmBtnH;
        _yesBtnRect = new Rectangle(innerX, btnY, ConfirmBtnW, ConfirmBtnH);
        _noBtnRect = new Rectangle(innerX + ConfirmBtnW + 8, btnY, ConfirmBtnW, ConfirmBtnH);
        UiHelper.DrawFilledRect(sb, _yesBtnRect, UiHelper.DangerButtonNormal);
        UiHelper.DrawBorder(sb, _yesBtnRect, Color.Gray);
        UiHelper.DrawFilledRect(sb, _noBtnRect, UiHelper.ButtonNormalBg);
        UiHelper.DrawBorder(sb, _noBtnRect, Color.Gray);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_Yes),
            UiHelper.CenterText(font, ClientStrings.Get(ClientStrings.Common_Yes), _yesBtnRect), Color.White);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_No),
            UiHelper.CenterText(font, ClientStrings.Get(ClientStrings.Common_No), _noBtnRect), Color.White);
    }

    private static void DrawBar(SpriteBatch sb, SpriteFont font, Rectangle bounds,
        float fillRatio, int current, int max, Color fill, float alpha, ref BarSlot slot, InputState input)
    {
        if (slot.Current != current || slot.Max != max)
        {
            slot.Current = current;
            slot.Max = max;
            slot.Text = UiHelper.VitalBarText(ClientStrings.Get(slot.LabelKey), current, max);
        }
        string text = slot.Text;
        if (input.IsHoverIn(bounds))
        {
            int pct = max > 0 ? (int)Math.Round((double)current * 100.0 / max) : 0;
            text = $"{pct}%";
        }
        // outlineThickness=0 so the group outline (drawn later, around all three bars) is the only border.
        UiHelper.DrawVitalBar(sb, font, bounds, fillRatio, fill * alpha, Color.Transparent,
            text, Color.White * alpha, outlineThickness: 0, bgColor: UiHelper.BarBg * alpha);
    }
}
