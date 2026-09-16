using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Client.Shell.Panels;

public enum HudAction
{
    None,
    ToggleInventory,
    ToggleSocial,
    Quit,
    GameVerb,

    /// <summary>The player right-clicked a verb this game said may go on the action bar. The caller owns
    /// the context menu, so it opens the submenu; <see cref="HudPanel.PickedAction"/> names the verb.</summary>
    AssignVerb,
}

/// <summary>
/// Right sidebar drawn while in-game.
/// Layout (x=513..800, y=0..600):
///   Player name → whatever rows the game declared → Map name → panel buttons → Quit.
/// </summary>
public sealed class HudPanel
{
    // ── Layout constants (absolute screen coords) ─────────────────────────────
    private const int SidebarLeft = 513;

    // Link strip at the bottom of the sidebar. Three layouts share the same strip:
    //   - Pre-connect screens (MainMenu/Login/NewAccount/ChangePassword/DeleteAccount):
    //     Configure and Options as a centered pair (see ComputePregameLinkLayout).
    //   - Other non-gameplay screens (CharSelect/NewChar/Loading): lone Options, its tight
    //     box centered in the strip (OptionsLink).
    //   - Gameplay: Options (O) and Help (H) as a centered pair (see ComputeLinkLayout).
    // Each link is a shared Link widget — labels live in localized strings WITHOUT brackets;
    // the widget wraps them with "[…]" at draw time so every site reads the same way.
    private const int LinkStripX = 519, LinkStripW = 275, LinkH = 14;
    // Internal rather than private: HotkeyBarPanel stacks the action bar directly above this strip and
    // centers on it, so the two share one definition of where the sidebar's bottom furniture lives.
    internal const int LinkStripY = 582;
    internal const int LinkStripCenterX = LinkStripX + LinkStripW / 2;
    private const int LinkGap = 16;

    // Lone Options, its tight box centered in the strip — used on CharSelect/NewChar/Loading.
    // Bounds are set in ComputePregameLinkLayout once the label width is known.
    public static readonly Link OptionsLink = new();
    // Pre-connect paired layout — Options left, Configure right. Refined by
    // ComputePregameLinkLayout once the font is known.
    public static readonly Link OptionsLinkPregame = new();
    public static readonly Link ConfigureLink = new();
    // In-game layout — Mail / Options / Help, refined by ComputeLinkLayout.
    // Mail is leftmost (first, before Options); GameplayScreen tints it gold while mail is unread.
    public static readonly Link MailLink = new();
    public static readonly Link OptionsLinkInGame = new();
    public static readonly Link HelpLink = new();

    /// <summary>
    /// Lays out the in-game Mail (M) / Options (O) / Help (H) link triple so the group sits centered
    /// in the sidebar strip, using the actual rendered text widths. Call once the font loads.
    /// </summary>
    public static void ComputeLinkLayout(SpriteFont font)
    {
        MailLink.Label = ClientStrings.Get(ClientStrings.HudPanel_MailLinkInGame);
        OptionsLinkInGame.Label = ClientStrings.Get(ClientStrings.HudPanel_OptionsLinkInGame);
        HelpLink.Label = ClientStrings.Get(ClientStrings.HudPanel_HelpLink);
        int mw = (int)Link.MeasureSize(font, MailLink.Label).X;
        int ow = (int)Link.MeasureSize(font, OptionsLinkInGame.Label).X;
        int hw = (int)Link.MeasureSize(font, HelpLink.Label).X;
        int startX = LinkStripCenterX - (mw + LinkGap + ow + LinkGap + hw) / 2;
        MailLink.Bounds = new Rectangle(startX, LinkStripY, mw, LinkH);
        OptionsLinkInGame.Bounds = new Rectangle(startX + mw + LinkGap, LinkStripY, ow, LinkH);
        HelpLink.Bounds = new Rectangle(startX + mw + LinkGap + ow + LinkGap, LinkStripY, hw, LinkH);
    }

    /// <summary>
    /// Lays out the pre-connect Configure / Options link pair so the group sits centered
    /// in the sidebar strip, using the actual rendered text widths. Call once the font loads.
    /// Also primes the lone Options link's label for the post-connect CharSelect path.
    /// </summary>
    public static void ComputePregameLinkLayout(SpriteFont font)
    {
        OptionsLinkPregame.Label = ClientStrings.Get(ClientStrings.HudPanel_OptionsLinkPregame);
        ConfigureLink.Label = ClientStrings.Get(ClientStrings.HudPanel_ConfigureLink);
        OptionsLink.Label = OptionsLinkPregame.Label;
        int ow = (int)Link.MeasureSize(font, OptionsLinkPregame.Label).X;
        int cw = (int)Link.MeasureSize(font, ConfigureLink.Label).X;
        int startX = LinkStripCenterX - (ow + LinkGap + cw) / 2;
        OptionsLinkPregame.Bounds = new Rectangle(startX, LinkStripY, ow, LinkH);
        ConfigureLink.Bounds = new Rectangle(startX + ow + LinkGap, LinkStripY, cw, LinkH);

        // Lone Options (CharSelect/NewChar/Loading) — its own tight box, centered in the strip.
        int olw = (int)Link.MeasureSize(font, OptionsLink.Label).X;
        OptionsLink.Bounds = new Rectangle(LinkStripCenterX - olw / 2, LinkStripY, olw, LinkH);
    }

    private const int SidebarWidth = 287;
    private const int Pad = 6;     // horizontal padding inside sidebar
    private const int BarH = 14;
    private const int BtnH = 26;

    // Inner width and left edge
    private static int InnerLeft => SidebarLeft + Pad;
    private static int InnerWidth => SidebarWidth - Pad * 2;

    // Button grid (2 columns)
    private static int BtnW => (InnerWidth - Pad) / 2;

    // One button in the stack, centered across the sidebar. Half the inner width because that is a
    // comfortable size for a caption, and centered because there is one column: a button pinned to the
    // left of a two-column grid sits off to one side once the grid has one column in it.
    private static Rectangle BtnRect(int row) =>
        new(InnerLeft + (InnerWidth - BtnW) / 2, ButtonBaseY + row * (BtnH + 4), BtnW, BtnH);

    // row0=Inventory, row1=Social, then one row per verb the game declared for this surface, then
    // Logout last. Logout stays at the bottom of the block whatever a game adds, because it is the one
    // button a player must always be able to find.
    private readonly Button _invBtn = new();
    private readonly Button _socialBtn = new();
    private readonly Button _quitBtn = new();
    private int _labelsGeneration = -1;

    // What the game declared for the HUD, laid out. Rebuilt only when the declaration changes, which is
    // once per session: a stock client is told on join and never again.
    private readonly List<(Button Btn, string Id, string Opens, GameAction Verb)> _gameBtns = [];

    // The ones drawn right now, in declaration order. Rebuilt each frame, since what a button is gated
    // on changes under the player: joining a guild puts one here.
    private readonly List<(Button Btn, string Id, string Opens, GameAction Verb)> _shownBtns = [];
    private int _gameBtnsFor = -1;

    /// <summary>The verb whose button was last clicked. Read when <see cref="Update"/> answers
    /// <see cref="HudAction.GameVerb"/>, because a click is consumed where it is detected and the caller
    /// cannot go looking for it a second time.</summary>
    public string PickedAction { get; private set; } = string.Empty;

    /// <summary><inheritdoc cref="PickedAction"/> The panel it opens, or blank.</summary>
    public string PickedOpens { get; private set; } = string.Empty;

    // ── Bar slot — static config (color/label) + lazy text cache ────────────
    private struct BarSlot
    {
        public readonly Color Fill;
        public readonly string LabelKey;
        public long Current = -1, Max = -1;
        public string Text = "";
        public BarSlot(Color fill, string labelKey)
        {
            Fill = fill;
            LabelKey = labelKey;
        }
    }
    private string _cachedPlayerNameRaw = "";
    private string _cachedPlayerName = "";
    private string _cachedMapNameRaw = "";
    private string _cachedMapDisplayNameRaw = "";
    // The resolved map name also folds in the map's GROUP display name, which can change under a live editor
    // save, so it's part of the cache key too — a group rename refreshes the HUD name without a map reload.
    private string _cachedGroupDisplayNameRaw = "";
    private int _cachedMapNum = -1;
    private string _cachedMapName = "";

    // ── Animated bar ratios ───────────────────────────────────────────────────

    // Exponential lerp spd — ~95% of gap closed in ~0.6 s
    private const float LerpSpeed = 5f;

    // Where the button block starts when a game declares nothing to show here.
    //
    // It is a FLOOR rather than a fixed line. The buttons used to sit here whatever a game declared, so
    // the rows got the gap above and a game with more to say than fits lost the rest silently: MSR
    // declares twelve rows and six fitted, which cost it intelligence and all three vital bars while
    // the sidebar below the buttons sat empty.
    private const int ButtonFloorY = 175;

    // Where the rows actually ended last frame, so the buttons can start under them. Static for the
    // same reason _declaredButtons is: there is one HUD, drawn on one thread.
    private static int _rowsBottom;

    private static int ButtonBaseY => Math.Max(ButtonFloorY, _rowsBottom);
    // Vertical spacing between stacked name rows (player name, class+level, map name).
    private const int NameRowH = 18;

    // One declared row. Shorter than a name row: these are read as a block rather than one at a time,
    // and the block has a fixed ceiling — the buttons below it.
    private const int DisplayRowH = 14;

    public HudPanel()
    {
        _invBtn.Bounds = BtnRect(0);
        _socialBtn.Bounds = BtnRect(1);
        _quitBtn.Bounds = BtnRect(LogoutRow);
    }

    // Logout's row, which moves down as a game declares buttons above it, and the top of the empty
    // sidebar below it.
    //
    // The count is static because the layout helper below it is, and that one is asked by a panel that
    // has no HUD to ask. There is one HUD, drawn on one thread, so the shared value is the same value.
    private static int _declaredButtons;
    private int LogoutRow => 2 + _shownBtns.Count;
    private static int LogoutY => ButtonBaseY + (2 + _declaredButtons) * (BtnH + 4);

    /// <summary>Where a panel of <paramref name="width"/> sits when it hangs in the sidebar's free space:
    /// centered across the sidebar, one padding gap under the Logout button. The sidebar's layout is private
    /// to this panel, so anything placed against it asks here rather than restating the arithmetic.
    ///
    /// <para>Asked per frame rather than once, because the button block grows: a game declaring three
    /// buttons pushes Logout down three rows, and anything anchored to a remembered answer would then be
    /// drawn on top of them.</para></summary>
    public static Point FreeSpaceAnchor(int width) =>
        new(SidebarLeft + (SidebarWidth - width) / 2, LogoutY + BtnH + Pad);

    /// <summary>The Logout button's rectangle — the bottom edge of the button block.</summary>
    internal Rectangle LogoutBounds => _quitBtn.Bounds;

    /// <summary>Lay out one button per verb the game declared for the HUD.
    ///
    /// <para>Keyed on the declaration's own count and ids, so this does nothing on every frame but the
    /// one where a game's list arrives.</para></summary>
    private void SyncGameButtons(ClientState state)
    {
        var declared = state.Actions.For(ActionSurface.Hud);
        int stamp = declared.Count;
        foreach (var a in declared) stamp = HashCode.Combine(stamp, a.Id);
        if (stamp == _gameBtnsFor) return;

        _gameBtnsFor = stamp;
        _gameBtns.Clear();

        for (int i = 0; i < declared.Count; i++)
        {
            var action = declared[i];
            string shortcut = action.Key.Length > 0
                ? action.Key
                : state.Panels.Find(action.OpensPanel)?.Key ?? string.Empty;

            var button = new Button
            {
                Bounds = BtnRect(2 + i),
                Label = ClientStrings.GetOrFallback(action.LabelKey, action.LabelKey)
                        + GameKeyMap.Hint(shortcut),
            };
            _gameBtns.Add((button, action.Id, action.OpensPanel, action));
        }
    }

    /// <summary>Works out which of the game's buttons are up, and puts them in rows with no gaps.
    ///
    /// <para>A verb that asked to be hidden leaves no row behind while its condition does not hold: the
    /// buttons below it come up one, and Logout with them, so the block is as long as what the player
    /// can press. One that did not is kept in place and drawn dim.</para></summary>
    private void ShowGameButtons(ClientState state)
    {
        var mine = state.AttributesOf(EntityHandle.ForPlayer(state.MyIndex));

        _shownBtns.Clear();
        foreach (var entry in _gameBtns)
        {
            bool holds = entry.Verb.When.Holds(mine);
            if (!holds && entry.Verb.Unmet == ActionUnmet.Hide) continue;

            entry.Btn.Enabled = holds;
            _shownBtns.Add(entry);
        }

        for (int i = 0; i < _shownBtns.Count; i++) _shownBtns[i].Btn.Bounds = BtnRect(2 + i);

        _declaredButtons = _shownBtns.Count;
        _quitBtn.Bounds = BtnRect(LogoutRow);
    }

    // ── Tick — animation only, called every frame regardless of mouse position ──

    // ── Update — button clicks only, skipped when mouse is over a floating panel

    public HudAction Update(InputState input, ClientState state)
    {
        SyncGameButtons(state);
        ShowGameButtons(state);

        if (_invBtn.IsClicked(input)) return HudAction.ToggleInventory;
        if (_socialBtn.IsClicked(input)) return HudAction.ToggleSocial;

        foreach (var (button, id, opens, _) in _shownBtns)
        {
            if (!button.IsClicked(input)) continue;

            PickedAction = id;
            PickedOpens = opens;
            return HudAction.GameVerb;
        }

        // Right-click a verb to put it on the action bar — offered only where there IS a bar, and only
        // on a verb the game said may go there. The click is consumed here so it cannot also reach the
        // world behind the sidebar.
        if (HotkeyAssignMenu.IsOffered(state) && input.IsRightMouseClicked())
        {
            foreach (var (button, id, _, verb) in _shownBtns)
            {
                if (!verb.Hotkeyable || !button.Bounds.Contains(input.MousePosition)) continue;

                input.ConsumeRightMouseClick();
                PickedAction = id;
                return HudAction.AssignVerb;
            }
        }

        if (_quitBtn.IsClicked(input)) return HudAction.Quit;
        return HudAction.None;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, SpriteFont font, SpriteFont titleFont, ClientState state, InputState input)
    {
        var me = state.Me;
        if (me is null) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _invBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_InventoryButton);
            _socialBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_SocialButton);
            _quitBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_LogoutButton);
            // The map-name cache bakes a localized string into a value keyed on the map, which does
            // not move when the language does — so the text would hold the old language until the map
            // happened to change. Clearing the key lets it rebuild through its normal path.
            _cachedMapNum = -1;
        }

        int x = InnerLeft;
        int barW = InnerWidth;
        int y = 5;

        var titleSize = titleFont.MeasureString(state.GameName);
        float titleX = SidebarLeft + SidebarWidth / 2f - titleSize.X / 2f;
        sb.DrawString(titleFont, state.GameName, new Vector2(titleX, y), UiHelper.DlgLabelColor);
        y += titleFont.LineSpacing + 2;

        if (me.Name != _cachedPlayerNameRaw)
        {
            _cachedPlayerNameRaw = me.Name;
            _cachedPlayerName = me.Name.Trim();
        }
        if (_cachedPlayerName.Length > 0)
        {
            // Same rule as the overhead name + party overlay (PlayerNameColor over the shared palette),
            // so your name reads identically everywhere instead of a HUD-only white/red special case.
            long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool showAsPk = me.IsPk(nowUtc) && me.PkGraceUntilUtc <= nowUtc;
            var nameColor = TextArea.GetColor(PlayerNameColor.For(showAsPk, me.Access));
            UiHelper.DrawLabelCentered(sb, font, _cachedPlayerName, SidebarLeft, y, SidebarWidth, nameColor);
        }
        y += NameRowH;

        string groupDisplayName = state.GroupOf(state.Map)?.DisplayName ?? "";
        if (state.Map.DisplayName != _cachedMapDisplayNameRaw || state.Map.Name != _cachedMapNameRaw
            || state.CenterMapNum != _cachedMapNum || groupDisplayName != _cachedGroupDisplayNameRaw)
        {
            _cachedMapDisplayNameRaw = state.Map.DisplayName;
            _cachedMapNameRaw = state.Map.Name;
            _cachedGroupDisplayNameRaw = groupDisplayName;
            _cachedMapNum = state.CenterMapNum;
            _cachedMapName = UiHelper.ResolveMapDisplayName(state.Map, state.CenterMapNum, state.GroupOf(state.Map));
        }
        if (_cachedMapName.Length > 0)
        {
            UiHelper.DrawLabelCentered(sb, font, _cachedMapName, SidebarLeft, y, SidebarWidth, Color.DimGray);
        }
        y += NameRowH;

        // Time-of-Day / Weather status line. Hovering shows countdown to the next major phase.
        string todPhaseKey = state.TimePhase switch
        {
            TimePhase.Dusk => ClientStrings.HudPanel_TimeDusk,
            TimePhase.Night => ClientStrings.HudPanel_TimeNight,
            TimePhase.Dawn => ClientStrings.HudPanel_TimeDawn,
            _ => ClientStrings.HudPanel_TimeDay,
        };
        string wxAdjKey = state.Weather switch
        {
            WeatherType.Rain => ClientStrings.HudPanel_WeatherRainy,
            WeatherType.Snow => ClientStrings.HudPanel_WeatherSnowy,
            WeatherType.HeatWave => ClientStrings.HudPanel_WeatherHot,
            WeatherType.HeavyWind => ClientStrings.HudPanel_WeatherWindy,
            _ => ClientStrings.HudPanel_WeatherClear,
        };
        string wxAdj = ClientStrings.Get(wxAdjKey);
        var todRowRect = new Rectangle(SidebarLeft, y, SidebarWidth, NameRowH);
        bool todHovered = input.IsHoverIn(todRowRect);
        // At rest: "{Weather} {Phase}" e.g. "Windy Night". On hover: weather + the unchanged ToD
        // countdown to the next major phase (Day/Night), e.g. "Windy, 12m til night".
        string todText = todHovered
            ? $"{wxAdj}, {FormatTodTooltip(state)}"
            : $"{wxAdj} {ClientStrings.Get(todPhaseKey)}";
        UiHelper.DrawLabelCentered(sb, font, todText, SidebarLeft, y, SidebarWidth, UiHelper.WeatherStatusColor);
        y += NameRowH;

        // What the loaded game shows here, read off the local player's own values. Core declares
        // nothing, so this space belongs to whatever a game put on the HUD surface — and to the buttons
        // below it when a game put nothing.
        DrawDisplayRows(sb, font, state, x, ref y, barW);

        // Placed now rather than in the constructor, because where the block starts is only known once
        // the rows above it have been laid out.
        _invBtn.Bounds = BtnRect(0);
        _socialBtn.Bounds = BtnRect(1);
        ShowGameButtons(state);

        // Panel buttons: row0=Inventory, row1=Social, row2=Logout (centered)
        _invBtn.Draw(sb, font, input);
        _socialBtn.Draw(sb, font, input);
        foreach (var (button, _, _, _) in _shownBtns) button.Draw(sb, font, input);
        _quitBtn.Draw(sb, font, input);
    }

    /// <summary>The rows a game declared for the HUD, in declaration order.
    ///
    /// <para>All of them. The buttons underneath move down to make room, so declaring a twelfth row
    /// costs a button position rather than the row — a game that says a character has intelligence and
    /// three vital pools gets to show them.</para>
    ///
    /// <para>The sidebar still ends somewhere, so a game that declares more than the screen holds loses
    /// the tail. That is a wall rather than an arbitrary line partway up an empty panel.</para></summary>
    private static void DrawDisplayRows(SpriteBatch sb, SpriteFont font, ClientState state,
                                        int x, ref int y, int width)
    {
        var rows = state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).Rows;
        for (int i = 0; i < rows.Count && y + DisplayRowH <= SidebarRowCeiling; i++)
        {
            DrawDisplayRow(sb, font, rows[i], x, y, width);
            y += DisplayRowH;
        }

        _rowsBottom = y + Pad;
    }

    /// <summary>How far down the sidebar rows may run before the buttons would have nowhere to go: the
    /// panel's own height, less the block they need.</summary>
    private static int SidebarRowCeiling =>
        UiHelper.RefH - (3 + _declaredButtons) * (BtnH + 4) - Pad * 2;

    private static void DrawDisplayRow(SpriteBatch sb, SpriteFont font, DisplayRow row,
                                       int x, int y, int width)
    {
        // A label is a localization key a GAME supplied, so an unknown one shows the key rather than
        // throwing: a missing translation is a cosmetic fault, and taking the HUD down over one is not.
        string label = row.LabelKey is { Length: > 0 } key ? ClientStrings.GetOrFallback(key, key) : "";
        var color = new Color(GameColor.RedOf(row.Color), GameColor.GreenOf(row.Color), GameColor.BlueOf(row.Color));

        switch (row.Style)
        {
            case DisplayStyle.Heading:
                UiHelper.DrawLabelCentered(sb, font, label, SidebarLeft, y, SidebarWidth, UiHelper.DlgLabelColor);
                break;

            case DisplayStyle.Meter:
                UiHelper.DrawMeter(sb, font, new Rectangle(x, y, width, DisplayRowH - 2),
                    (float)row.Fill, color, Color.Black,
                    label.Length > 0 ? UiHelper.MeterText(label, (long)row.Value, (long)row.Max) : row.Text,
                    Color.White, ease: row.Key);
                break;

            case DisplayStyle.Badge:
                UiHelper.DrawLabelCentered(sb, font, row.Text, SidebarLeft, y, SidebarWidth, color);
                break;

            default:
                // Caption left, value right — the reading a player scans down rather than across.
                if (label.Length > 0)
                    UiHelper.DrawLabel(sb, font, label, new Vector2(x, y), UiHelper.DlgLabelColor, width / 2f);
                float valueW = font.MeasureString(row.Text).X;
                UiHelper.DrawLabel(sb, font, row.Text, new Vector2(x + width - valueW, y), color, width / 2f);
                break;
        }
    }

    private static string FormatTodTooltip(ClientState state)
    {
        long phaseStartMs = state.TimePhase switch
        {
            TimePhase.Dusk => Constants.TodDayDurationMs,
            TimePhase.Night => Constants.TodNightStartMs,
            TimePhase.Dawn => Constants.TodDawnStartMs,
            _ => 0L,
        };
        long phaseDurationMs = state.TimePhase switch
        {
            TimePhase.Dusk => Constants.TodDuskDurationMs,
            TimePhase.Night => Constants.TodNightDurationMs,
            TimePhase.Dawn => Constants.TodDawnDurationMs,
            _ => Constants.TodDayDurationMs,
        };
        long cyclePos = phaseStartMs + (long)(state.GetInterpolatedProgress() * phaseDurationMs);
        bool towardNight = state.TimePhase is TimePhase.Day or TimePhase.Dusk;
        long remainingMs = towardNight
            ? Constants.TodNightStartMs - cyclePos
            : Constants.TodCycleDurationMs - cyclePos;
        // Round up so the label reads "2m" through the whole second-to-last minute and only ever
        // shows "0m" at the exact instant of transition (no minute-long lingering "0m").
        int totalMinutes = (int)Math.Ceiling(Math.Max(remainingMs, 0L) / 60_000.0);
        int h = totalMinutes / 60;
        int m = totalMinutes % 60;
        string timeStr = h > 0 ? $"{h}h {m}m" : $"{m}m";
        string key = towardNight ? ClientStrings.HudPanel_TimeToNight : ClientStrings.HudPanel_TimeToDay;
        return ClientStrings.Format(key, ("Time", timeStr));
    }

}
