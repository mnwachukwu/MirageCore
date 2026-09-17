using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Compact party-partner overlay, in the sidebar's free space below the Logout button.  Only drawn while
/// the local player has a partner — the snapshot lives on <c>state.Party</c> and is pushed by the
/// server.  Bars share fills and label format with the right-sidebar HUD (via
/// <see cref="UiHelper.DrawMeter"/>) and stack flush against each other inside a panel; a single
/// outline wraps the three-bar block.  Outline picks up the in-world bar treatment: amber while the
/// partner is in combat, cyan when the local player is targeting them, otherwise white.  Proximity
/// (partner's map in our 3×3 observable area, same rule the 1.2× party EXP bonus uses) drives a
/// 0.7/0.4 alpha tint on the whole overlay, so how close the partner is reads at a glance.
/// </summary>
public sealed class PartyOverlayPanel
{
    // Layout — the sidebar's free space, centered under the Logout button. Everything below is measured
    // from (X,Y), so the whole panel and its hit boxes travel together.
    // A property rather than a `static readonly` field: the button block this hangs under grows when a
    // game declares buttons of its own, and an anchor frozen at type init would draw this on top of them.
    private static Point Anchor => HudPanel.FreeSpaceAnchor(PanelW);
    private static int X => Anchor.X;
    private static int Y => Anchor.Y;

    /// <summary>The panel's outer rectangle, where it is drawn.</summary>
    internal static Rectangle Bounds => new(X, Y, PanelW, PanelH);

    // How many bars the loaded game declared, for the layout that has to answer without a state to ask.
    // Static for the same reason the HUD's button count is: there is one of these, drawn on one thread.
    private static int _declaredBars;

    /// <summary>How many bars the layout is currently sized for. Settable so a test can ask what the
    /// panel would be for a game that declares two, without standing a client up to declare them.</summary>
    internal static int DeclaredBars
    {
        get => _declaredBars;
        set => _declaredBars = value;
    }

    /// <summary>The panel's height for what is currently declared. Zero bars is a header and a close
    /// glyph, which is all Core declares on its own: a name, and a way out of the party.</summary>
    private static int PanelH => HeightFor(_declaredBars);

    private const int InnerW = 152;
    private const int HeaderH = 14;
    private const int BarH = 12;
    private const int BarGap = 1;
    private const int Pad = 4;
    private const int HeaderBarGap = Pad;              // gap between header and bars, matches top/bottom pad
    private const int PanelW = InnerW + Pad * 2;
    /// <summary>How tall the panel is for <paramref name="bars"/> declared rows.
    ///
    /// <para>Measured rather than fixed: this used to reserve three rows for HP, MP and SP whatever a
    /// game had to say, so a game declaring one bar got an inch of empty panel under it and a game
    /// declaring none got the whole block. Core on its own declares none, and then this is a name.</para>
    /// </summary>
    private static int HeightFor(int bars)
        => HeaderH + Pad * 2 + (bars > 0 ? HeaderBarGap + bars * (BarH + BarGap) - BarGap : 0);

    // Panel chrome — subtle dark backing with a thin border, both alpha-tinted by proximity.
    private static readonly Color PanelBg = new(10, 19, 16, 200);
    private static readonly Color PanelBorder = new(58, 90, 82);

    // ── Animated bar ratios (mirror HudPanel.Tick) ────────────────────────────
    private const float LerpSpeed = 5f;

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

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, InputState input,
        TargetRef tabTarget, long nowMs)
    {
        var party = state.Party;
        if (!party.Active)
        {
            _confirmingLeave = false;
            return;
        }

        if (_confirmingLeave)
        {
            DrawLeaveConfirm(sb, font);
            return;
        }

        _declaredBars = state.OverheadBars.Count;

        // Nearby = partner's map is one of the nine this client is observing.
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

        // 2) Header row — the partner's name. White for contrast against the dark panel backing, but
        //    the PK red still wins so the partner's status reads at a glance; access-level coloring is
        //    intentionally dropped (rarely matters here). Grayed when not nearby.
        Color headerColor = !nearby ? Color.DimGray
            : party.ShowAsPk ? ChatPanel.GetColor(GameColor.BrightRed)
            : Color.White;
        // The name stops short of the close glyph so a long one cannot slide under it.
        _closeBtnRect = new Rectangle(X + PanelW - CloseSize - 2, Y + 2, CloseSize, CloseSize);
        int innerX = X + Pad;
        int headerY = Y + Pad;
        string fittedName = UiHelper.FitText(font, party.Name, _closeBtnRect.Left - CloseGap - innerX - 6);
        sb.DrawString(font, fittedName, new Vector2(innerX, headerY), headerColor * alpha);

        // 5) Close (×) glyph in the panel's top-right (rect computed in the header step above).
        //    Click opens the leave-party confirmation.
        bool closeHover = _closeBtnRect.Contains(input.MousePosition);
        var closeColor = (closeHover ? Color.White : Color.LightGray) * alpha;
        sb.DrawString(font, "x", new Vector2(_closeBtnRect.X + 3, _closeBtnRect.Y - 2), closeColor);

        DrawDeclaredBars(sb, font, state, party, outline, alpha, innerX, headerY);
    }

    /// <summary>The bars the GAME declared, read off the partner's own attributes.
    ///
    /// <para>This used to draw three of its own, hardcoded to HP, MP and SP — in an engine with no
    /// vitals. It reads <see cref="ClientState.OverheadBars"/> now, which is the same declaration the
    /// bars over a body's head come from, so a game describes its bars once and both surfaces follow.</para>
    ///
    /// <para>🔴 <b>The values arrive with the snapshot; they are not read off a bag here.</b> A
    /// partner is usually somebody this client cannot see, and a client holds attributes only for bodies
    /// in its own neighbourhood — so there is no bag to read, and there never will be. The server reads
    /// the declared bars off the partner and sends what each row draws, every tick, which is how the
    /// rows follow a partner two maps away.</para>
    ///
    /// <para>A row the partner has nothing to say about arrives negative, the same way
    /// <see cref="OverheadBar.FractionIn"/> says it, and is drawn empty.</para></summary>
    private static void DrawDeclaredBars(SpriteBatch sb, SpriteFont font, ClientState state,
        PartySnapshot party, Color outline, float alpha, int innerX, int headerY)
    {
        var bars = state.OverheadBars;
        if (bars.Count == 0) return;

        int y = headerY + HeaderH + HeaderBarGap;
        var block = new Rectangle(innerX - 1, y - 1, InnerW + 2,
                                  bars.Count * (BarH + BarGap) - BarGap + 2);
        UiHelper.DrawBorder(sb, block, outline * alpha);

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars.At(i);
            if (bar is null) continue;

            // Absent reads as empty rather than as full, and a snapshot that has not arrived yet is
            // absent: an overlay drawn full for a partner nobody has heard from would say the opposite
            // of what it knows.
            float fill = i < party.Bars.Count ? party.Bars[i] : OverheadBar.Absent;
            if (fill < 0f) fill = 0f;

            // Keyed by the partner and the bar, so a partner's health eases the way your own does
            // rather than stepping every time a snapshot lands.
            UiHelper.DrawMeter(sb, font, new Rectangle(innerX, y, InnerW, BarH), fill,
                ChatPanel.GetColor(bar.Rgb) * alpha, Color.Black * alpha, "", Color.White * alpha,
                ease: $"party/{party.Name}/{bar.ValueKey}");
            y += BarH + BarGap;
        }
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

}
