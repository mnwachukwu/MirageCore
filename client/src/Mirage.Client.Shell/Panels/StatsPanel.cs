using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Panels;

/// <summary>Character sheet panel — the same information the /stats command prints, as a panel.</summary>
public sealed class StatsPanel : IGamePanel
{
    // Default bounds for a brand-new character with no saved layout.  Width and position are the
    // real defaults; the height (320) is only a close-enough placeholder, because on first Draw
    // the panel snaps to its exact content height — see the _minHSet block — so a fresh Stats
    // panel opens at its minimum height with no empty space below the last row.  A restored
    // layout (SetBounds) suppresses that snap so a player's explicit resize is preserved.
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 260, 320), minH: 260);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b)
    {
        _panel.SetBounds(b);
        _boundsFromConfig = true;
    }
    // Clearing _boundsFromConfig re-arms the first-Draw snap-to-content-height, so a reset returns the
    // panel to the exact height a brand-new character gets rather than to the 320 placeholder.
    public void ResetBounds()
    {
        _panel.ResetBounds();
        _boundsFromConfig = false;
    }
    public void Toggle() { IsOpen = !IsOpen; }
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    private const int BarH = 14;
    private const int BarGap = 3;
    private const int RowH = 20;
    private readonly record struct RowLayout(int X, int W, int HalfW, int Col2X);
    private readonly record struct ColData(string Label, string Value, Color Color);

    // Caches for N0-formatted vital strings
    private long _cachedHp = -1, _cachedMaxHp = -1;
    private long _cachedMp = -1, _cachedMaxMp = -1;
    private long _cachedSp = -1, _cachedMaxSp = -1;
    private long _cachedWithinLevel = -1, _cachedTnl = -1;
    private long _cachedTotalExp = -1;
    private string _hpText = "", _mpText = "", _spText = "", _expText = "", _totalExpText = "";

    private long _lastAnimToggleMs;
    private int _animFrame;

    // Cache for name/level header
    private int _cachedLevel = -1;
    private string _cachedClassName = "", _cachedLevelStr = "", _cachedPlayerName = "";

    // Cache for PK timer row
    private long _cachedPkExpiryUtc = -1;
    private long _cachedPkTimerSlab = -1;
    private string _pkTimerText = "";

    // Trails ClientStrings.Generation so a language switch invalidates the caches above that hold a
    // resolved string. See the block at the top of Draw.
    private int _labelsGeneration = -1;

    // Set once from the live font so minH exactly covers all content.
    private bool _minHSet;

    // True once a saved layout has been restored, so the first-Draw snap-to-min-height leaves a
    // player's explicitly resized height alone.
    private bool _boundsFromConfig;

    public void Update(InputState input, ClientState state)
    {
        if (!IsOpen) return;
        _panel.Update(input);
        if (_panel.WasClosed) IsOpen = false;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, InputState input, IReadOnlyList<Texture2D?> sprites,
        float dispHp, float dispMp, float dispSp, float dispExp, bool isActive = false)
    {
        if (!IsOpen) return;

        // Every vital/level/exp string below is cached against the NUMBER that produced it, so a
        // language switch on its own leaves the old label in place until that number next moves —
        // HP could sit on "HP 50/50" in a French session for as long as the player avoids damage.
        // Clearing the cache keys re-runs the normal rebuild paths with the new language.
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _cachedHp = _cachedMp = _cachedSp = -1;
            _cachedWithinLevel = _cachedTotalExp = -1;
            _cachedLevel = -1;
        }

        // Pin minimum height to the exact pixel count needed for all rows.
        if (!_minHSet)
        {
            int contentH = 2
                + font.LineSpacing + 1   // name
                + 32 + 4                 // sprite preview
                + font.LineSpacing + 4   // level/class
                + BarH + BarGap          // bar row 1: HP | MP
                + BarH + BarGap + 4      // bar row 2: SP | EXP
                + 5 * RowH               // stat rows: STR|Crit, INT|SpellCrit, DEF|Block/Dodge, SPD|Sprint, Points|—
                + 2 * RowH               // combat-output rows: DMG, P-MIT|M-MIT
                + 2 * RowH               // regen rows: HP|MP, SP|—
                + RowH;                  // PK timer row (always visible)
            int minH = contentH + DraggablePanel.TitleH;
            _panel.SetMinH(minH);
            // A fresh panel (no saved layout) opens at exactly its minimum height — snug, with no empty
            // space below the last row.  A restored layout keeps the player's saved height, but is still
            // clamped UP to the current min (SetBounds does the max) so a layout saved before the content
            // grew — e.g. before the Points row was added — can never clip the bottom rows.
            var b = _panel.Bounds;
            _panel.SetBounds(new Rectangle(b.X, b.Y, b.Width, _boundsFromConfig ? b.Height : minH));
            _minHSet = true;
        }

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.StatsPanel_Title), isActive);

        var me = state.Me;
        var c = _panel.ContentBounds;
        int x = c.X + 4;
        int w = c.Width - 8;
        int y = c.Y + 2;
        int halfW = (w - 4) / 2;
        int col2X = x + halfW + 4;

        // ── Name ─────────────────────────────────────────────────────────────
        int nameH = font.LineSpacing + 1;
        if (y + nameH <= c.Bottom)
        {
            if (me.Name != _cachedPlayerName) _cachedPlayerName = me.Name.Trim();
            if (_cachedPlayerName.Length > 0)
            {
                // Same name-color rule as the overhead name / HUD sidebar (shared PlayerNameColor).
                long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                bool showAsPk = me.IsPk(nowUtc) && me.PkGraceUntilUtc <= nowUtc;
                var nameColor = TextArea.GetColor(PlayerNameColor.For(showAsPk, me.Access));
                UiHelper.DrawLabelCentered(sb, font, _cachedPlayerName, c.X, y, c.Width, nameColor);
            }
            y += nameH;
        }

        // ── Sprite preview ────────────────────────────────────────────────────
        var spriteSheet = sprites.Sheet(me.SpriteSheet);
        if (spriteSheet is not null && me.Sprite >= 0 && y + 32 + 4 <= c.Bottom)
        {
            long nowMs = Environment.TickCount64;
            if (nowMs - _lastAnimToggleMs >= 250)
            {
                _animFrame ^= 1;
                _lastAnimToggleMs = nowMs;
            }
            var src = SpriteAtlas.GetSourceRect(me.Sprite, Direction.Down, _animFrame);
            sb.Draw(spriteSheet, new Rectangle(c.X + (c.Width - Constants.PicX) / 2, y, Constants.PicX, Constants.PicY), src, Color.White);
            y += 32 + 4;
        }

        // ── Level / Class ─────────────────────────────────────────────────────
        int levelH = font.LineSpacing + 4;
        if (y + levelH <= c.Bottom)
        {
            string className = (me.Class > 0 && me.Class < state.Classes.Length)
                ? state.Classes[me.Class]?.Name ?? "" : "";
            if (me.Level != _cachedLevel || className != _cachedClassName)
            {
                _cachedLevel = me.Level;
                _cachedClassName = className;
                _cachedLevelStr = className.Length > 0
                    ? ClientStrings.Format(ClientStrings.Common_LevelWithClassFormat, ("Level", me.Level), ("Class", className))
                    : ClientStrings.Format(ClientStrings.Common_LevelFormat, ("Level", me.Level));
            }
            UiHelper.DrawLabelCentered(sb, font, _cachedLevelStr, c.X, y, c.Width, Color.Gold);
            y += levelH;
        }

        // ── Vital bars — two per row ──────────────────────────────────────────
        long expFloor = ExpFormulas.ExpFloorForLevel(me.Level);
        long withinLevel = me.Exp - expFloor;
        long tnl = ExpFormulas.TnlForLevel(me.Level);

        if (me.Hp != _cachedHp || me.MaxHp != _cachedMaxHp)
        {
            _cachedHp = me.Hp;
            _cachedMaxHp = me.MaxHp;
            _hpText = UiHelper.VitalBarText(ClientStrings.Get(ClientStrings.Stats_Hp), me.Hp, me.MaxHp);
        }
        if (me.Mp != _cachedMp || me.MaxMp != _cachedMaxMp)
        {
            _cachedMp = me.Mp;
            _cachedMaxMp = me.MaxMp;
            _mpText = UiHelper.VitalBarText(ClientStrings.Get(ClientStrings.Stats_Mp), me.Mp, me.MaxMp);
        }
        if (me.Sp != _cachedSp || me.MaxSp != _cachedMaxSp)
        {
            _cachedSp = me.Sp;
            _cachedMaxSp = me.MaxSp;
            _spText = UiHelper.VitalBarText(ClientStrings.Get(ClientStrings.Stats_Sp), me.Sp, me.MaxSp);
        }
        if (withinLevel != _cachedWithinLevel || tnl != _cachedTnl)
        {
            _cachedWithinLevel = withinLevel;
            _cachedTnl = tnl;
            _expText = UiHelper.VitalBarText(ClientStrings.Get(ClientStrings.Stats_Exp), withinLevel, tnl);
        }
        if (me.Exp != _cachedTotalExp)
        {
            _cachedTotalExp = me.Exp;
            _totalExpText = ClientStrings.Format(ClientStrings.StatsPanel_TotalExpFormat, ("Total", me.Exp));
        }

        // Real (weather-INDEPENDENT) max vitals for the hover readout: me.MaxHp/Mp/Sp are the EFFECTIVE maxes
        // (Snow/Heat Wave cut them), so recompute the true pools from stats + class — the player can then hover a
        // bar to see their real cap even while a weather effect is shrinking it.
        var myClass = (me.Class > 0 && me.Class < state.Classes.Length) ? state.Classes[me.Class] : null;
        int realMaxHp = StatFormulas.GetPlayerMaxHp(me.Level, me.Def, myClass?.Def ?? 0);
        int realMaxMp = StatFormulas.GetPlayerMaxMp(me.Level, me.Int, myClass?.Int ?? 0);
        int realMaxSp = StatFormulas.GetPlayerMaxSp(me.Level, me.Spd, myClass?.Spd ?? 0);

        if (y + BarH <= c.Bottom)
        {
            var hpBar = new Rectangle(x, y, halfW, BarH);
            var mpBar = new Rectangle(col2X, y, halfW, BarH);
            string hpLabel = hpBar.Contains(input.MousePosition) ? MaxVitalText(ClientStrings.Get(ClientStrings.Stats_Hp), realMaxHp) : _hpText;
            string mpLabel = mpBar.Contains(input.MousePosition) ? MaxVitalText(ClientStrings.Get(ClientStrings.Stats_Mp), realMaxMp) : _mpText;
            DrawVitalBar(sb, font, hpBar, dispHp, hpLabel, UiHelper.VitalHpColor);
            DrawVitalBar(sb, font, mpBar, dispMp, mpLabel, UiHelper.VitalMpColor);
            y += BarH + BarGap;
        }
        if (y + BarH <= c.Bottom)
        {
            var spBar = new Rectangle(x, y, halfW, BarH);
            string spLabel = spBar.Contains(input.MousePosition) ? MaxVitalText(ClientStrings.Get(ClientStrings.Stats_Sp), realMaxSp) : _spText;
            DrawVitalBar(sb, font, spBar, dispSp, spLabel, UiHelper.VitalSpColor);
            var expBar = new Rectangle(col2X, y, halfW, BarH);
            string expLabel = expBar.Contains(input.MousePosition) ? _totalExpText : _expText;
            DrawVitalBar(sb, font, expBar, dispExp, expLabel, UiHelper.ExpBarColor);
            y += BarH + BarGap + 4;
        }

        // ── Stat rows — left: base stat, right: derived combat value ──────────
        // All derived values route through the shared formula classes so the panel
        // stays in lockstep with what the server actually computes — no duplicated
        // formula bodies to fall out of date.
        int str = me.Str, def = me.Def, @int = me.Int, spd = me.Spd, points = me.Points;
        string crit = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(str, me.Level));
        string block = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(def, me.Level));
        string spellCrit = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(@int, me.Level));
        string dodge = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerDodgeChancePerMille(def, me.Level));

        // Regen shows the EFFECTIVE per-tick amount — Snow / Heat Wave halve regen magnitude, so in that weather the
        // row shows the reduced value and hovering a cell reveals the REAL (weather-independent) amount, mirroring
        // the Max-vital hover on the bars above.  The hover is live ONLY on a cell whose effective differs from its
        // real value — out of reducing weather the multiplier is 1.0 (effective == real), so there's nothing to
        // toggle and the mouse check is skipped.
        double regenMult = WeatherEffects.RegenMultiplier(state.Weather);
        int hpRegen = StatFormulas.GetPlayerHpRegen(def);                // real (mult = 1.0)
        int mpRegen = StatFormulas.GetPlayerMpRegen(@int);
        int spRegen = StatFormulas.GetPlayerSpRegen(spd);
        int hpRegenEff = StatFormulas.GetPlayerHpRegen(def, regenMult);  // effective (reduced under Snow / Heat Wave)
        int mpRegenEff = StatFormulas.GetPlayerMpRegen(@int, regenMult);
        int spRegenEff = StatFormulas.GetPlayerSpRegen(spd, regenMult);

        bool hasShield = me.ShieldSlot > 0;
        string evasionLabel = hasShield ? ClientStrings.Get(ClientStrings.Stats_Block) : ClientStrings.Get(ClientStrings.Stats_Dodge);
        string evasionVal = hasShield ? block : dodge;
        // Sprint = how much faster running is than WALKING: +100% at 0 SPD (twice walk pace), up to +200%
        // at the SPD cap.  Shown under Block (both are SP-fueled outputs).
        string sprintVal = $"+{MovementFormulas.SprintBonusPercent(spd)}%";
        // Points = unspent training points.  Always shown (even at 0) on its own row directly below SPD.
        string ptsLabel = ClientStrings.Get(ClientStrings.Stats_Points);
        string ptsVal = points.ToString();

        var rowLayout = new RowLayout(x, w, halfW, col2X);
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_Str), str.ToString(), Color.OrangeRed), new ColData(ClientStrings.Get(ClientStrings.Stats_PCrit), crit, Color.White));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_Int), @int.ToString(), Color.DodgerBlue), new ColData(ClientStrings.Get(ClientStrings.Stats_MCrit), spellCrit, Color.White));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_Def), def.ToString(), Color.LimeGreen), new ColData(evasionLabel, evasionVal, Color.White));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_Spd), spd.ToString(), UiHelper.VitalSpColor), new ColData(ClientStrings.Get(ClientStrings.Stats_Sprint), sprintVal, Color.White));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ptsLabel, ptsVal, Color.White), new ColData("", "", Color.White));
            y += RowH;
        }

        // ── Combat-output rows — actual damage / mitigation given current stats + gear ──────
        // Format: "<base> + <gear contrib> = <total>" when gear contributes, just "<base>" otherwise.
        // Values share the neutral white combat-readout color used by Crit/Block/SpellCrit (SP amber is reserved for SP).
        // DMG values stacked in the left column, MIT on the right:
        // Row 1: P-DMG (melee — unarmed + weapon) | MIT (Def base + armor + helmet + shield — one universal axis)
        // Row 2: M-DMG (spell — SpellPower + prepared spell's VitalAmount) | (free — Sprint under Block)
        (int pdmgBase, int pdmgGear) = ComputePhysDamageBreakdown(me, state);
        (int mdmgBase, int mdmgGear) = ComputeMagicDamageBreakdown(me, state);
        (int mitBase, int mitGear) = ComputeMitBreakdown(me, state);
        string pdmgVal = FormatBreakdown(pdmgBase, pdmgGear);
        string mdmgVal = FormatBreakdown(mdmgBase, mdmgGear);
        string mitVal = FormatBreakdown(mitBase, mitGear);

        // Always magic damage: the prepared slot is the caster's weapon, and only a SubHp spell may occupy
        // it (PacketHandler.Spells enforces that). Every other type lives on the action bar and never
        // reaches this row.
        string mdmgLabel = ClientStrings.Get(ClientStrings.Stats_MDmg);
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_PDmg), pdmgVal, Color.White), new ColData(ClientStrings.Get(ClientStrings.Stats_Mit), mitVal, Color.White));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(mdmgLabel, mdmgVal, Color.White), new ColData("", "", Color.White));
            y += RowH;
        }

        // ── Regen rows ────────────────────────────────────────────────────────
        if (y + RowH <= c.Bottom)
        {
            var hpRegenCell = new Rectangle(x, y, halfW, RowH);
            var mpRegenCell = new Rectangle(col2X, y, halfW, RowH);
            string hpRegenStr = $"+{(hpRegen != hpRegenEff && hpRegenCell.Contains(input.MousePosition) ? hpRegen : hpRegenEff)}/tick";
            string mpRegenStr = $"+{(mpRegen != mpRegenEff && mpRegenCell.Contains(input.MousePosition) ? mpRegen : mpRegenEff)}/tick";
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_HpRegen), hpRegenStr, Color.IndianRed), new ColData(ClientStrings.Get(ClientStrings.Stats_MpRegen), mpRegenStr, Color.SkyBlue));
            y += RowH;
        }
        if (y + RowH <= c.Bottom)
        {
            var spRegenCell = new Rectangle(x, y, halfW, RowH);
            string spRegenStr = $"+{(spRegen != spRegenEff && spRegenCell.Contains(input.MousePosition) ? spRegen : spRegenEff)}/tick";
            DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_SpRegen), spRegenStr, UiHelper.VitalSpColor), new ColData("", "", Color.White));
            y += RowH;
        }

        // ── PK timer row ──────────────────────────────────────────────────────
        if (y + RowH <= c.Bottom)
        {
            long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (me.IsPk(nowUtc))
            {
                long slab = nowUtc / 60;
                if (me.PkExpiryUtc != _cachedPkExpiryUtc || slab != _cachedPkTimerSlab)
                {
                    _cachedPkExpiryUtc = me.PkExpiryUtc;
                    _cachedPkTimerSlab = slab;
                    long secsLeft = Math.Max(0, me.PkExpiryUtc - nowUtc);
                    long minsLeft = (secsLeft + 59) / 60;  // ceiling: 1–60s → 1m
                    int hours = (int)(minsLeft / 60);
                    int mins = (int)(minsLeft % 60);
                    _pkTimerText = $"{hours:D2}:{mins:D2}";
                }
                DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_PkTimer), _pkTimerText, UiHelper.PkNameColor), new ColData("", "", Color.White));
            }
            else
            {
                DrawTwoColRow(sb, font, rowLayout, y, new ColData(ClientStrings.Get(ClientStrings.Stats_PkTimer), "-", Color.Lime), new ColData("", "", Color.White));
            }
        }

        _panel.DrawOverlay(sb);
    }

    // Hover readout for a vital bar: the REAL (weather-independent) max, e.g. "Max HP: 500".
    private static string MaxVitalText(string vital, int max) =>
        ClientStrings.Format(ClientStrings.StatsPanel_MaxVitalFormat, ("Vital", vital), ("Max", max));

    private static void DrawVitalBar(SpriteBatch sb, SpriteFont font, Rectangle bounds, float fill, string label, Color barColor) =>
        UiHelper.DrawVitalBar(sb, font, bounds, fill, barColor, Color.DimGray, label, Color.White);

    private static void DrawTwoColRow(SpriteBatch sb, SpriteFont font, RowLayout layout, int y, ColData left, ColData right)
    {
        UiHelper.DrawFilledRect(sb, new Rectangle(layout.X - 2, y, layout.W + 4, RowH - 2), UiHelper.StatRowBg);

        float lv = font.MeasureString(left.Value).X;
        sb.DrawString(font, UiHelper.FitText(font, left.Label, Math.Max(10f, layout.HalfW - lv - 2)), new Vector2(layout.X, y + 2), Color.DimGray);
        sb.DrawString(font, left.Value, new Vector2(layout.X + layout.HalfW - lv, y + 2), left.Color);

        if (right.Label.Length > 0)
        {
            float rv = font.MeasureString(right.Value).X;
            sb.DrawString(font, UiHelper.FitText(font, right.Label, Math.Max(10f, layout.HalfW - rv - 2)), new Vector2(layout.Col2X, y + 2), Color.DimGray);
            sb.DrawString(font, right.Value, new Vector2(layout.Col2X + layout.HalfW - rv, y + 2), right.Color);
        }
    }

    /// <summary>Format a breakdown value: "base + gear = total" when gear > 0, just "base" otherwise.
    /// Zero-gear display matches "no bonuses from any gear" — value column shows only the stat-derived base.</summary>
    private static string FormatBreakdown(int baseVal, int gearVal) =>
        gearVal > 0 ? $"{baseVal} + {gearVal} = {baseVal + gearVal}" : baseVal.ToString();

    /// <summary>P-DMG = UnarmedDamage(Str) + WeaponContribution(weapon.Power, Str).
    /// Returns (base, gear) — gear is 0 if no weapon equipped or item not loaded.</summary>
    private static (int Base, int Gear) ComputePhysDamageBreakdown(Mirage.Shared.Records.PlayerRecord me, ClientState state)
    {
        int baseVal = CombatFormulas.UnarmedDamage(me.Str);
        int gearVal = 0;
        if (me.WeaponSlot > 0 && me.Inv is not null && state.Items is not null)
        {
            int itemNum = me.Inv[me.WeaponSlot].Num;
            if (itemNum > 0 && itemNum < state.Items.Length && state.Items[itemNum] is { } weapon)
                gearVal = CombatFormulas.WeaponContribution(weapon.Power, me.Str);
        }
        return (baseVal, gearVal);
    }

    /// <summary>M-DMG = SpellPower(Int) + SpellContribution(preparedSpell.VitalAmount, Int).
    /// Returns (base, contribution) — contribution is 0 if no prepared spell.  Mirrors the
    /// weapon pattern: SpellPower is the always-available stat-based component; preparing
    /// a spell is the "equipping" step that adds its VitalAmount contribution.
    /// <para>Note: <see cref="Mirage.Shared.Records.PlayerRecord.PreparedSpell"/> is a 1-based slot index
    /// into <see cref="Mirage.Shared.Records.PlayerRecord.Spell"/> (the learned-spell list), NOT the spell number.
    /// We dereference one extra level to get the actual SpellRecord.</para></summary>
    private static (int Base, int Gear) ComputeMagicDamageBreakdown(Mirage.Shared.Records.PlayerRecord me, ClientState state)
    {
        int baseVal = CombatFormulas.SpellPower(me.Int);
        int gearVal = 0;
        int preparedSlot = me.PreparedSpell;
        if (preparedSlot > 0 && me.Spell is not null && preparedSlot < me.Spell.Length)
        {
            int spellNum = me.Spell[preparedSlot];
            if (spellNum > 0 && state.SpellDefs is not null && spellNum < state.SpellDefs.Length
                && state.SpellDefs[spellNum] is { } spell
                && spell.Type != SpellType.GiveItem)
            {
                // GiveItem carries an item id rather than a magnitude, and has no VitalAmount at all —
                // hence the type guard above. The base SpellPower still shows so the player sees their
                // raw magic potential.
                gearVal = CombatFormulas.SpellContribution(spell.VitalAmount, me.Int);
            }
        }
        return (baseVal, gearVal);
    }

    /// <summary>MIT = PlayerProtection(Level, Def) + full GearMitigation for armor + helmet + a 1/4 chip from the
    /// shield.  The mirror's single universal MIT — this one number defends physical AND magic identically.  The
    /// base is now level-primary (a per-level baseline everyone gets + the DEF stat bonus), so it rises with level.</summary>
    private static (int Base, int Gear) ComputeMitBreakdown(Mirage.Shared.Records.PlayerRecord me, ClientState state)
    {
        int baseVal = CombatFormulas.PlayerProtection(me.Level, me.Def);
        int gearVal = CombatFormulas.GearMitigation(SlotPower(me, state, me.ArmorSlot), me.Def)
                    + CombatFormulas.GearMitigation(SlotPower(me, state, me.HelmetSlot), me.Def)
                    + CombatFormulas.ShieldMitigation(SlotPower(me, state, me.ShieldSlot), me.Def);
        return (baseVal, gearVal);
    }

    /// <summary>Equipped item's Power for a slot (0 if empty/invalid); the mit helpers map Power=0 → 0.</summary>
    private static int SlotPower(Mirage.Shared.Records.PlayerRecord me, ClientState state, int slot)
    {
        if (slot <= 0 || me.Inv is null || state.Items is null) return 0;
        int itemNum = me.Inv[slot].Num;
        if (itemNum <= 0 || itemNum >= state.Items.Length) return 0;
        return state.Items[itemNum] is { } it ? it.Power : 0;
    }
}
