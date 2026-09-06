using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Panels;

public enum HudAction { None, ToggleInventory, ToggleSpells, ToggleStats, ToggleTraining, ToggleQuestLog, ToggleSocial, Quit }

/// <summary>
/// Right sidebar drawn while in-game.
/// Layout (x=513..800, y=0..600):
///   Player name → Lv. N ClassName → Map name → HP/MP/SP/EXP bars → panel buttons → Quit.
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

    private static Rectangle BtnRect(int col, int row, int baseY)
    {
        int x = InnerLeft + col * (BtnW + Pad);
        int y = baseY + row * (BtnH + 4);
        return new Rectangle(x, y, BtnW, BtnH);
    }

    // 7 buttons: row0=Inventory/Spells, row1=Stats/Train, row2=QuestLog/Social, row3=Logout (lone, centered).
    // Shop/Inn buttons retired (shops open by interacting with their keeper NPC now).
    private readonly Button _invBtn = new();
    private readonly Button _spellBtn = new();
    private readonly Button _statsBtn = new();
    private readonly Button _trainBtn = new();
    private readonly Button _questLogBtn = new();
    private readonly Button _socialBtn = new();
    private readonly Button _quitBtn = new();
    private int _labelsGeneration = -1;

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
    private BarSlot _hpSlot = new(UiHelper.VitalHpColor, ClientStrings.Stats_Hp);
    private BarSlot _mpSlot = new(UiHelper.VitalMpColor, ClientStrings.Stats_Mp);
    private BarSlot _spSlot = new(UiHelper.VitalSpColor, ClientStrings.Stats_Sp);
    private BarSlot _expSlot = new(UiHelper.ExpBarColor, ClientStrings.Stats_Exp);
    private int _cachedLevel = -1;
    private string _cachedClassName = "";
    private string _cachedLevelStr = "";
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
    private bool _initialized;
    private float _dispHp, _dispMp, _dispSp, _dispExp;
    private long _nextLevel;
    public float DispHp => _dispHp;
    public float DispMp => _dispMp;
    public float DispSp => _dispSp;
    public float DispExp => _dispExp;

    // Exponential lerp spd — ~95% of gap closed in ~0.6 s
    private const float LerpSpeed = 5f;

    // Top Y of the 2-column action-button grid, measured from the sidebar top.
    // +18 vs original: the ToD phase label sits between map name and HP bar, adding one NameRowH row.
    private const int ButtonBaseY = 175;
    // Vertical spacing between stacked name rows (player name, class+level, map name).
    private const int NameRowH = 18;

    public HudPanel()
    {
        _invBtn.Bounds = BtnRect(0, 0, ButtonBaseY);
        _spellBtn.Bounds = BtnRect(1, 0, ButtonBaseY);
        _statsBtn.Bounds = BtnRect(0, 1, ButtonBaseY);
        _trainBtn.Bounds = BtnRect(1, 1, ButtonBaseY);
        _questLogBtn.Bounds = BtnRect(0, 2, ButtonBaseY);
        _socialBtn.Bounds = BtnRect(1, 2, ButtonBaseY);
        // Logout sits alone on row 3, centered across the two columns.
        _quitBtn.Bounds = new Rectangle(InnerLeft + (InnerWidth - BtnW) / 2, LogoutY, BtnW, BtnH);
    }

    // The lone Logout button's row, and the top of the empty sidebar below it.
    private const int LogoutRow = 3;
    private static int LogoutY => ButtonBaseY + LogoutRow * (BtnH + 4);

    /// <summary>Where a panel of <paramref name="width"/> sits when it hangs in the sidebar's free space:
    /// centered across the sidebar, one padding gap under the Logout button. The sidebar's layout is private
    /// to this panel, so anything placed against it asks here rather than restating the arithmetic.</summary>
    public static Point FreeSpaceAnchor(int width) =>
        new(SidebarLeft + (SidebarWidth - width) / 2, LogoutY + BtnH + Pad);

    /// <summary>The Logout button's rectangle — the bottom edge of the button block.</summary>
    internal Rectangle LogoutBounds => _quitBtn.Bounds;

    // ── Tick — animation only, called every frame regardless of mouse position ──

    public void Tick(ClientState state, float deltaSeconds)
    {
        var me = state.Me;
        if (me is null) return;

        // Defer until valid data arrives; snapping at MaxHp=0 would animate to full later.
        if (me.MaxHp <= 0) return;

        _nextLevel = ExpFormulas.TnlForLevel(me.Level);
        long expFloor = ExpFormulas.ExpFloorForLevel(me.Level);
        long withinLevel = me.Exp - expFloor;

        float targetHp = me.MaxHp > 0 ? Math.Clamp((float)me.Hp / me.MaxHp, 0f, 1f) : 0f;
        float targetMp = me.MaxMp > 0 ? Math.Clamp((float)me.Mp / me.MaxMp, 0f, 1f) : 0f;
        float targetSp = me.MaxSp > 0 ? Math.Clamp((float)me.Sp / me.MaxSp, 0f, 1f) : 0f;
        float targetExp = _nextLevel > 0 ? Math.Clamp((float)withinLevel / _nextLevel, 0f, 1f) : 0f;

        bool snap = !_initialized || state.SnapVitals;
        state.SnapVitals = false;
        _initialized = true;

        if (snap)
        {
            _dispHp = targetHp;
            _dispMp = targetMp;
            _dispSp = targetSp;
            _dispExp = targetExp;
            return;
        }

        float t = Math.Min(1f, LerpSpeed * deltaSeconds);
        _dispHp += (targetHp - _dispHp) * t;
        _dispMp += (targetMp - _dispMp) * t;
        _dispSp += (targetSp - _dispSp) * t;
        _dispExp += (targetExp - _dispExp) * t;
    }

    // ── Update — button clicks only, skipped when mouse is over a floating panel

    public HudAction Update(InputState input)
    {
        if (_invBtn.IsClicked(input)) return HudAction.ToggleInventory;
        if (_spellBtn.IsClicked(input)) return HudAction.ToggleSpells;
        if (_statsBtn.IsClicked(input)) return HudAction.ToggleStats;
        if (_trainBtn.IsClicked(input)) return HudAction.ToggleTraining;
        if (_questLogBtn.IsClicked(input)) return HudAction.ToggleQuestLog;
        if (_socialBtn.IsClicked(input)) return HudAction.ToggleSocial;
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
            _spellBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_SpellsButton);
            _statsBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_StatsButton);
            _trainBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_TrainingButton);
            _questLogBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_QuestLogButton);
            _socialBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_SocialButton);
            _quitBtn.Label = ClientStrings.Get(ClientStrings.HudPanel_LogoutButton);
            // The caches below bake a localized string into a value keyed on the DATA that produced
            // it — the vital bars on current/max, the level line on level+class, the map name on the
            // map. None of those keys move when the language does, so the text would hold the old
            // language until the underlying number happened to change. Clearing the keys lets each
            // one rebuild through its normal path rather than duplicating the formatting here.
            _hpSlot.Current = _mpSlot.Current = _spSlot.Current = _expSlot.Current = -1;
            _cachedLevel = -1;
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

        string className = (me.Class > 0 && me.Class < state.Classes.Length)
            ? state.Classes[me.Class]?.Name ?? ""
            : "";
        if (me.Level != _cachedLevel || className != _cachedClassName)
        {
            _cachedLevel = me.Level;
            _cachedClassName = className;
            _cachedLevelStr = className.Length > 0
                ? ClientStrings.Format(ClientStrings.Common_LevelWithClassFormat, ("Level", me.Level), ("Class", className))
                : ClientStrings.Format(ClientStrings.Common_LevelFormat, ("Level", me.Level));
        }
        UiHelper.DrawLabelCentered(sb, font, _cachedLevelStr, SidebarLeft, y, SidebarWidth, Color.Gold);
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
            var mapNameColor = state.MoralOf(state.Map) switch
            {
                MapMoral.Safe => UiHelper.SafeMapNameColor,
                MapMoral.Arena => UiHelper.ArenaMapNameColor,
                _ => Color.DimGray,
            };
            UiHelper.DrawLabelCentered(sb, font, _cachedMapName, SidebarLeft, y, SidebarWidth, mapNameColor);
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

        DrawBar(sb, font, new Rectangle(x, y, barW, BarH), _dispHp, me.Hp, me.MaxHp, ref _hpSlot, input);
        DrawBar(sb, font, new Rectangle(x, y + 18, barW, BarH), _dispMp, me.Mp, me.MaxMp, ref _mpSlot, input);
        DrawBar(sb, font, new Rectangle(x, y + 36, barW, BarH), _dispSp, me.Sp, me.MaxSp, ref _spSlot, input);
        long expFloor = ExpFormulas.ExpFloorForLevel(me.Level);
        DrawBar(sb, font, new Rectangle(x, y + 54, barW, BarH), _dispExp, me.Exp - expFloor, _nextLevel, ref _expSlot, input);

        // Panel buttons: row0=Inventory/Spells, row1=Stats/Train, row2=QuestLog/Social, row3=Logout (centered)
        _invBtn.Draw(sb, font, input);
        _spellBtn.Draw(sb, font, input);
        _statsBtn.Draw(sb, font, input);
        _trainBtn.Draw(sb, font, input);
        _questLogBtn.Draw(sb, font, input);
        _socialBtn.Draw(sb, font, input);
        _quitBtn.Draw(sb, font, input);
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

    private static void DrawBar(SpriteBatch sb, SpriteFont font, Rectangle bounds,
        float fillRatio, long current, long max, ref BarSlot slot, InputState input)
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
        UiHelper.DrawVitalBar(sb, font, bounds, fillRatio, slot.Fill, Color.DimGray, text, Color.White);
    }
}
