using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Rendering;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Hover tooltip for items and spells. Exactly one tooltip is rendered at a time; panels feed
/// it via <see cref="NotifyHoverItem"/> / <see cref="NotifyHoverSpell"/> while their row is
/// hovered, and <c>GameplayScreen</c> calls <see cref="TickAndDraw"/> after every panel
/// has been drawn so the tooltip floats above the rest of the UI.
///
/// Timing rules:
///   • Appears immediately on the first frame a row is hovered (no delay).
///   • Position is pinned to the mouse coords at first-show + a small offset; it does not move
///     until the hovered identity changes.
///   • Moving onto a different row instantly swaps content and re-pins to the new mouse position
///     — no fade, no double-render.
///   • Mouse over the source row OR over the tooltip's own rect keeps it open. The instant both
///     leave (and no new row is hovered), the tooltip clears on the same frame — no linger.
///
/// Item tooltips show the item icon from items.bmp next to the header; spell tooltips are
/// text-only because spells have no per-spell graphic.
/// </summary>
public static class Tooltip
{
    private const int MouseOffsetX = 14;
    private const int MouseOffsetY = 18;

    private const int PadX = 8;
    private const int PadY = 6;
    private const int LineGap = 2;
    private const int HeaderGap = 4;
    private const int IconSize = 32;
    private const int IconRightGap = 8;

    private static readonly Color BgColor = new(20, 20, 40, 240);
    private static readonly Color BorderColor = new(100, 120, 200);
    private static readonly Color HeaderColor = Color.White;
    private static readonly Color LabelColor = new(170, 190, 230);
    private static readonly Color ValueColor = Color.White;
    private static readonly Color WarnColor = Color.OrangeRed;
    private static readonly Color GoodColor = Color.LightGreen;

    private readonly record struct Line(string Label, string Value, Color Color);

    private enum Kind { None, Item, Spell, Text }

    private static Kind _kind;
    private static string _scope = "";   // panel id that spawned the active tooltip
    private static object? _key;
    private static int _x, _y;
    private static Rectangle _bounds;
    private static bool _hoverPersists;

    // Cached content needed to redraw each frame. Captured by reference so live data (durability,
    // stat changes) reflects in the tooltip without callers having to refresh on every change.
    private static ItemRecord? _item;
    private static PlayerInvSlot? _slot;
    private static SpellRecord? _spell;
    private static string? _text;   // Kind.Text: the full string a truncated label shows on hover
    private static PlayerRecord? _me;
    private static ClassRecord?[] _classes = Array.Empty<ClassRecord?>();
    private static IReadOnlyList<Texture2D?> _itemsTex = [];
    private static SpellRecord?[] _spellDefs = Array.Empty<SpellRecord?>();   // spell definitions (for a scroll's spell half)
    private static ItemRecord?[] _itemDefs = Array.Empty<ItemRecord?>();   // item definitions (for the SubHp reagent name)
    private static WeatherType _weather;                                    // current weather (for the rain "(x2)" reagent hint)

    private static readonly List<Line> _lines = new();

    /// <summary>
    /// Called by a panel during its Update each frame the mouse is over a row that should show an item
    /// tooltip. <paramref name="key"/> identifies the row+item so the tooltip re-pins position when the
    /// user moves to a different slot or the slot's item changes. <paramref name="scope"/> tags this
    /// tooltip with the spawning panel id so <see cref="CloseScope"/> can dismiss it when that panel
    /// closes.
    ///
    /// <para>The last three are only needed for a SPELL SCROLL, whose tooltip continues into the spell
    /// it teaches — everything that decides whether a scroll is worth buying lives on the spell, not on
    /// the scroll. A caller with no spell table simply omits them and the scroll shows its item half
    /// alone, which is what it did everywhere before.</para>
    /// </summary>
    public static void NotifyHoverItem(string scope, object key, ItemRecord item, PlayerInvSlot? slot,
        PlayerRecord? me, ClassRecord?[] classes, IReadOnlyList<Texture2D?> itemsTex, Point mousePos,
        SpellRecord?[]? spellDefs = null, ItemRecord?[]? itemDefs = null, WeatherType weather = default)
    {
        if (_kind != Kind.Item || !Equals(_key, key))
        {
            _kind = Kind.Item;
            _scope = scope;
            _key = key;
            PinTo(mousePos);
        }
        _item = item;
        _slot = slot;
        _me = me;
        _classes = classes;
        _itemsTex = itemsTex;
        // Assigned even when null: these are shared with the spell path, and leaving them behind would
        // price a scroll's reagent line off whatever spell was hovered last.
        _spellDefs = spellDefs ?? [];
        _itemDefs = itemDefs ?? [];
        _weather = weather;
        _spell = null;
        _hoverPersists = true;
    }

    /// <summary>Spell counterpart to <see cref="NotifyHoverItem"/>.</summary>
    public static void NotifyHoverSpell(string scope, object key, SpellRecord spell,
        PlayerRecord? me, ClassRecord?[] classes, ItemRecord?[] itemDefs, WeatherType weather, Point mousePos)
    {
        if (_kind != Kind.Spell || !Equals(_key, key))
        {
            _kind = Kind.Spell;
            _scope = scope;
            _key = key;
            PinTo(mousePos);
        }
        _spell = spell;
        _me = me;
        _classes = classes;
        _itemDefs = itemDefs;
        _weather = weather;
        _item = null;
        _slot = null;
        _itemsTex = [];
        _hoverPersists = true;
    }

    /// <summary>Show a plain-text tooltip — used by a truncated label to reveal its full text on hover.
    /// <paramref name="key"/> identifies the hovered label so the tooltip re-pins when the pointer moves to a
    /// different one.</summary>
    public static void NotifyHoverText(string scope, object key, string text, Point mousePos)
    {
        if (_kind != Kind.Text || !Equals(_key, key))
        {
            _kind = Kind.Text;
            _scope = scope;
            _key = key;
            PinTo(mousePos);
        }
        _text = text;
        _item = null;
        _slot = null;
        _spell = null;
        _itemsTex = [];
        _hoverPersists = true;
    }

    /// <summary>Dismiss the tooltip if it was spawned by <paramref name="scope"/>. Called by a
    /// panel when it closes so any tooltip it had open doesn't linger over the empty space the
    /// panel just vacated. No-op when a different panel owns the active tooltip.</summary>
    public static void CloseScope(string scope)
    {
        if (_kind != Kind.None && _scope == scope) Reset();
    }

    /// <summary>Per-frame tick + draw — called once after every panel finishes drawing so the
    /// tooltip floats above all of them. Mouse on the source row (via NotifyHover*) or on the
    /// tooltip's own rect keeps it open; the instant both are no longer hovered the tooltip
    /// clears on this same frame.</summary>
    public static void TickAndDraw(SpriteBatch sb, SpriteFont font, long nowMs, Point mousePos)
    {
        if (_kind == Kind.None) return;

        // Mouse over the tooltip's own rect counts as continuing to hover.
        if (_bounds.Contains(mousePos)) _hoverPersists = true;

        if (!_hoverPersists)
        {
            Reset();
            return;
        }

        Draw(sb, font);
        _hoverPersists = false;
    }

    public static void Reset()
    {
        _kind = Kind.None;
        _scope = "";
        _key = null;
        _hoverPersists = false;
        _bounds = Rectangle.Empty;
        _item = null;
        _slot = null;
        _spell = null;
        _me = null;
        _itemsTex = [];
        _text = null;
    }

    // The RAW cursor position. The offsets are applied at draw, where the tooltip's own size is known
    // and decides whether it opens below the cursor or above it.
    private static void PinTo(Point mousePos)
    {
        _x = mousePos.X;
        _y = mousePos.Y;
    }

    /// <summary>Where a card of <paramref name="w"/> x <paramref name="h"/> sits for a cursor at
    /// (<paramref name="mouseX"/>, <paramref name="mouseY"/>).
    ///
    /// <para>Below the cursor by default, ABOVE it when the whole card will not fit below. Clamping
    /// alone put a tooltip near the bottom edge on top of the row that spawned it — the action bar
    /// sits 24px off the floor, so every one of its four slots did that. The clamp still runs last,
    /// for a card taller than the viewport or a cursor at the right edge.</para></summary>
    internal static Point Place(int mouseX, int mouseY, int w, int h)
    {
        int below = mouseY + MouseOffsetY;
        int y = below + h <= UiHelper.RefH - 2 ? below : mouseY - MouseOffsetY - h;
        return new Point(
            Math.Clamp(mouseX + MouseOffsetX, 2, UiHelper.RefW - 2 - w),
            Math.Clamp(y, 2, UiHelper.RefH - 2 - h));
    }

    private static void Draw(SpriteBatch sb, SpriteFont font)
    {
        _lines.Clear();
        string header;
        bool hasIcon;
        short pic;
        short itemSheet;

        switch (_kind)
        {
            case Kind.Item when _item is not null:
                header = _item.Name?.TrimEnd() ?? "Unknown";
                BuildItemLines(_item, _slot, _me, _classes, _spellDefs, _itemDefs, _weather);
                hasIcon = _item.Pic >= 0 && _itemsTex.Sheet(_item.ItemSheet) is not null;
                pic = _item.Pic;
                itemSheet = _item.ItemSheet;
                break;
            case Kind.Spell when _spell is not null:
                header = _spell.Name?.TrimEnd() ?? "Unknown";
                BuildSpellLines(_spell, _me, _classes, _itemDefs, _weather);
                hasIcon = false;
                pic = 0;
                itemSheet = 0;
                break;
            case Kind.Text when _text is not null:
                header = _text;   // a single-line tooltip: just the full (un-truncated) label text
                hasIcon = false;
                pic = 0;
                itemSheet = 0;
                break;
            default:
                return;
        }

        float lineH = font.LineSpacing;
        float headerW = font.MeasureString(header).X;
        float bodyW = 0f;
        for (int i = 0; i < _lines.Count; i++)
        {
            var ln = _lines[i];
            float lineW = font.MeasureString(ln.Value.Length == 0 ? ln.Label : ln.Label + ": " + ln.Value).X;
            if (lineW > bodyW) bodyW = lineW;
        }

        int headerRowH = hasIcon ? Math.Max((int)lineH, IconSize) : (int)lineH;
        int bodyRowsH = _lines.Count == 0 ? 0
            : HeaderGap + _lines.Count * (int)lineH + (_lines.Count - 1) * LineGap;

        float contentW = Math.Max(
            hasIcon ? IconSize + IconRightGap + headerW : headerW,
            bodyW);

        int w = (int)Math.Ceiling(contentW) + PadX * 2;
        int h = headerRowH + bodyRowsH + PadY * 2;

        var at = Place(_x, _y, w, h);
        _bounds = new Rectangle(at.X, at.Y, w, h);
        UiHelper.DrawFilledRect(sb, _bounds, BgColor);
        UiHelper.DrawBorder(sb, _bounds, BorderColor);

        int cx = _bounds.X + PadX;
        int cy = _bounds.Y + PadY;

        if (hasIcon)
        {
            sb.DrawItemIcon(_itemsTex, pic, itemSheet, new Rectangle(cx, cy, IconSize, IconSize), Color.White);
            float headerY = cy + (IconSize - lineH) / 2f;
            sb.DrawString(font, header, new Vector2(cx + IconSize + IconRightGap, headerY), HeaderColor);
            cy += IconSize;
        }
        else
        {
            sb.DrawString(font, header, new Vector2(cx, cy), HeaderColor);
            cy += (int)lineH;
        }

        if (_lines.Count > 0) cy += HeaderGap;

        for (int i = 0; i < _lines.Count; i++)
        {
            var ln = _lines[i];
            // No value means the line is a heading: label alone, no colon. Matches the width pass above.
            if (ln.Value.Length == 0)
            {
                sb.DrawString(font, ln.Label, new Vector2(cx, cy), LabelColor);
            }
            else
            {
                string labelText = ln.Label + ": ";
                sb.DrawString(font, labelText, new Vector2(cx, cy), LabelColor);
                float labelW = font.MeasureString(labelText).X;
                sb.DrawString(font, ln.Value, new Vector2(cx + labelW, cy), ln.Color);
            }
            cy += (int)lineH + LineGap;
        }
    }

    private static void BuildItemLines(ItemRecord item, PlayerInvSlot? slot, PlayerRecord? me,
        ClassRecord?[] classes, SpellRecord?[] spellDefs, ItemRecord?[] itemDefs, WeatherType weather)
    {
        bool isEquip = ItemRecord.IsEquipment(item.Type);

        if (isEquip && item.Durability > 0)
        {
            // A real inventory slot carries the item's actual wear; the cur/max readout is color-coded
            // by condition (white/yellow/red) exactly like the equipment panel and repair panel, so a
            // worn or broken piece reads the same everywhere. With no backing slot (e.g. a shop listing)
            // there's no wear to show, so display full (which colors white).
            int dur = slot?.Dur ?? item.Durability;
            _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Durability), $"{dur}/{item.Durability}", UiHelper.DurabilityColor(dur, item.Durability)));
        }

        int meStr = me?.Str ?? 0;
        int meDef = me?.Def ?? 0;
        var myClass = me != null && me.Class > 0 && me.Class < classes.Length ? classes[me.Class] : null;
        int classStr = myClass?.Str ?? 0;
        int classDef = myClass?.Def ?? 0;
        string hp = ClientStrings.Get(ClientStrings.Stats_Hp);
        string mp = ClientStrings.Get(ClientStrings.Stats_Mp);
        string sp = ClientStrings.Get(ClientStrings.Stats_Sp);

        // What the item DOES prints here; what it COSTS you to qualify prints after. The stat gate is
        // captured rather than added, so it can join the level and class gates in the block below.
        string? statReqLabel = null;
        string statReqValue = "";
        bool meetsStatReq = false;

        switch (item.Type)
        {
            case ItemType.Weapon when item.Power > 0:
                int weaponStrReq = CombatFormulas.GearStatRequirement(item.Power, classStr);
                statReqLabel = ClientStrings.Get(ClientStrings.Tooltip_StrReq);
                statReqValue = UiHelper.FormatRequirement(item.Power, weaponStrReq);
                meetsStatReq = meStr >= weaponStrReq;
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Stats_PDmg), $"+{CombatFormulas.WeaponContribution(item.Power, meStr)}", ValueColor));
                break;
            case ItemType.Armor when item.Power > 0:
                int armorDefReq = CombatFormulas.GearStatRequirement(item.Power, classDef);
                statReqLabel = ClientStrings.Get(ClientStrings.Tooltip_DefReq);
                statReqValue = UiHelper.FormatRequirement(item.Power, armorDefReq);
                meetsStatReq = meDef >= armorDefReq;
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Stats_Mit), $"+{CombatFormulas.GearMitigation(item.Power, meDef)}", ValueColor));
                break;
            case ItemType.Helmet when item.Power > 0:
                int helmetDefReq = CombatFormulas.GearStatRequirement(item.Power, classDef);
                statReqLabel = ClientStrings.Get(ClientStrings.Tooltip_DefReq);
                statReqValue = UiHelper.FormatRequirement(item.Power, helmetDefReq);
                meetsStatReq = meDef >= helmetDefReq;
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Stats_Mit), $"+{CombatFormulas.GearMitigation(item.Power, meDef)}", ValueColor));
                break;
            case ItemType.Shield when item.Power > 0:
                int shieldDefReq = CombatFormulas.GearStatRequirement(item.Power, classDef);
                statReqLabel = ClientStrings.Get(ClientStrings.Tooltip_DefReq);
                statReqValue = UiHelper.FormatRequirement(item.Power, shieldDefReq);
                meetsStatReq = meDef >= shieldDefReq;
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Stats_Mit), $"+{CombatFormulas.ShieldMitigation(item.Power, meDef)}", ValueColor));
                break;
            case ItemType.PotionAddHp when item.VitalAmount > 0:
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Restores), $"+{item.VitalAmount} {hp}", GoodColor));
                break;
            case ItemType.PotionAddMp when item.VitalAmount > 0:
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Restores), $"+{item.VitalAmount} {mp}", GoodColor));
                break;
            case ItemType.PotionAddSp when item.VitalAmount > 0:
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Restores), $"+{item.VitalAmount} {sp}", GoodColor));
                break;
            case ItemType.PotionSubHp when item.VitalAmount > 0:
                AddSubPotionLines(item, me?.MaxHp ?? 0, hp, isHp: true, (me?.MaxMp ?? 0, mp), (me?.MaxSp ?? 0, sp));
                break;
            case ItemType.PotionSubMp when item.VitalAmount > 0:
                AddSubPotionLines(item, me?.MaxMp ?? 0, mp, isHp: false, (me?.MaxHp ?? 0, hp), (me?.MaxSp ?? 0, sp));
                break;
            case ItemType.PotionSubSp when item.VitalAmount > 0:
                AddSubPotionLines(item, me?.MaxSp ?? 0, sp, isHp: false, (me?.MaxHp ?? 0, hp), (me?.MaxMp ?? 0, mp));
                break;
            case ItemType.Currency when slot is not null:
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Quantity), slot.Quantity.ToString("N0"), ValueColor));
                break;
        }

        // The gates, in one order everywhere they are shown: level, then stat, then class. A class gate may
        // name several classes and renders as one comma-joined line.
        if (item.LevelReq > 0)
        {
            bool meetsLevel = me is not null && me.Level >= item.LevelReq;
            _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_LevelReq),
                item.LevelReq.ToString(), meetsLevel ? GoodColor : WarnColor));
        }

        if (statReqLabel is not null)
            _lines.Add(new Line(statReqLabel, statReqValue, meetsStatReq ? GoodColor : WarnColor));

        if (isEquip && ClassGate.IsRestricted(item.AllowedClasses))
        {
            string names = ClassGate.Describe(item.AllowedClasses, classes);
            if (names.Length > 0)
            {
                bool meetsClass = me != null && ClassGate.Allows(item.AllowedClasses, me.Class);
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_ClassReq), names, meetsClass ? GoodColor : WarnColor));
            }
        }

        // ── The spell half of a scroll ────────────────────────────────────────
        // A scroll is a delivery mechanism and almost nothing about it is a property of the ITEM: what
        // it teaches, what a cast costs, who may learn it and at what level all live on the spell. Its
        // own LevelReq is deliberately zero (ItemRecord.UsesLevelReq excludes Spell), so reading only
        // the item leaves the reader with a price and nothing to weigh it against.
        //
        // Appended below rather than replacing the item lines: a scroll is still a thing with a price
        // that occupies a bag slot, and the buy confirm shows both halves the same way.
        if (item.Type == ItemType.Spell && item.SpellNum > 0 && item.SpellNum < spellDefs.Length
            && spellDefs[item.SpellNum] is { } taught)
        {
            _lines.Add(new Line(
                ClientStrings.Format(ClientStrings.Tooltip_Teaches, ("SpellName", taught.TrimmedName)),
                "", HeaderColor));
            BuildSpellLines(taught, me, classes, itemDefs, weather);
        }
    }

    /// <summary>The two lines a Sub* potion shows. What it PAYS depends on the reader's own pools, not on
    /// the item — <see cref="StatFormulas.SubPotionGain"/> converts through pool fractions — so this is
    /// computed against the viewing player rather than printed off <c>VitalAmount</c>.
    ///
    /// <para>With no player context (the character-create preview has no live vitals) only the drain is
    /// shown: that half IS a property of the item, while the payout genuinely is not knowable yet.</para></summary>
    private static void AddSubPotionLines(ItemRecord item, int drainMax, string drainName, bool isHp,
        (int Max, string Name) first, (int Max, string Name) second)
    {
        // Quoted from a FULL bar, which is the most the potion can ever take: a short pour is allowed but
        // pays less, and HP reserves its last point so a potion is never lethal. Showing the item's raw
        // VitalAmount would promise 3,169 HP of exchange to a player whose whole bar is 900.
        int drained = drainMax > 0 ? StatFormulas.SubPotionDrain(item.VitalAmount, drainMax, isHp) : item.VitalAmount;
        _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Drains), $"-{drained} {drainName}", WarnColor));
        if (drainMax <= 0 || first.Max <= 0 || second.Max <= 0) return;

        int gainFirst = StatFormulas.SubPotionGain(drained, drainMax, first.Max);
        int gainSecond = StatFormulas.SubPotionGain(drained, drainMax, second.Max);
        _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Restores),
            $"+{gainFirst} {first.Name} / +{gainSecond} {second.Name}", GoodColor));
    }

    private static void BuildSpellLines(SpellRecord spell, PlayerRecord? me, ClassRecord?[] classes, ItemRecord?[] itemDefs, WeatherType weather)
    {
        ClassRecord? myClass = null;
        if (me is not null && me.Class > 0 && me.Class < classes.Length)
            myClass = classes[me.Class];

        int classInt = myClass?.Int ?? 0;
        // SubHp pays the trivial pool-fraction (per the caster resource model); everything else the utility cost.
        // AddMp prices off what it will restore for THIS caster, so it reads me.Int — the player's own Int, as
        // the server does — not the class base used for the INT requirement below.
        int mpCost = spell.Type == SpellType.SubHp
            ? CombatFormulas.GetSubHpSpellMpCost(me?.MaxMp ?? 0)
            : CombatFormulas.GetSpellMpCost(spell, me?.Int ?? 0);
        int intReq = CombatFormulas.GetSpellIntRequirement(spell, classInt);

        _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_MpCost), mpCost.ToString(), ValueColor));

        // SubHp also burns casting reagents. TWO lines, both whole numbers: what a cast takes, then how often
        // it takes the larger of the two. A cast usually costs nothing at low tiers, so one averaged figure
        // ("0.1") reads as a strange fraction of an item the player never sees leave the bag.
        // Rain is folded into both numbers rather than tagged onto one, and still carries its "(x2)" hint.
        if (spell.Type == SpellType.SubHp)
        {
            string reagentName = (Constants.CastingReagentItemIndex < itemDefs.Length
                ? itemDefs[Constants.CastingReagentItemIndex]?.Name?.Trim() : null) ?? "?";
            bool raining = weather == WeatherType.Rain;
            double exact = CombatFormulas.SubHpReagentCostExact(spell.LevelReq)
                         * (raining ? Constants.WeatherRainReagentMultiplier : 1);
            int perCast = CombatFormulas.ReagentCostPerCast(exact);
            double chance = CombatFormulas.ReagentDepleteChancePercent(exact);

            string costText = perCast.ToString();
            if (raining) costText = ClientStrings.Format(ClientStrings.Tooltip_ReagentCostRained, ("Count", costText));
            _lines.Add(new Line(ClientStrings.Format(ClientStrings.Tooltip_ReagentCost, ("Reagent", reagentName)),
                costText, ValueColor));

            // Omitted at 100%, where every cast pays and there are no odds worth stating.
            if (chance is > 0 and < 100)
            {
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_ReagentDepletes),
                    ClientStrings.Format(ClientStrings.Tooltip_ReagentChancePercent, ("Percent", chance.ToString("0.#"))),
                    ValueColor));
            }
        }

        // Effectiveness: M-DMG for damaging spells (any Sub* drains a vital), and a per-vital restore
        // label for Add* spells. Shows ONLY the spell's own contribution paired with playerInt,
        // not base + contribution — matches how the weapon tooltip shows just WeaponContribution
        // rather than UnarmedDamage + WeaponContribution. GiveItem is suppressed because it carries an
        // item id rather than a magnitude.
        string? effectLabel = spell.Type switch
        {
            SpellType.SubHp => ClientStrings.Get(ClientStrings.Stats_MDmg),
            SpellType.SubMp => ClientStrings.Get(ClientStrings.Stats_MpDmg),
            SpellType.SubSp => ClientStrings.Get(ClientStrings.Stats_SpDmg),
            SpellType.AddHp => ClientStrings.Get(ClientStrings.Stats_HpRestore),
            SpellType.AddMp => ClientStrings.Get(ClientStrings.Stats_MpRestore),
            SpellType.AddSp => ClientStrings.Get(ClientStrings.Stats_SpRestore),
            _ => null,
        };
        if (effectLabel is not null)
        {
            int amount = CombatFormulas.SpellContribution(spell.VitalAmount, me?.Int ?? 0);
            _lines.Add(new Line(effectLabel, $"+{amount}", ValueColor));
        }

        // Level, stat, class — the same order the item tooltip reads in, after the effect above. Level is
        // checked on learn AND on every cast, so it belongs here rather than only in the shop: a delevel can
        // put a spell you already know out of reach.
        if (spell.LevelReq > 0)
        {
            bool meetsLevel = me is not null && me.Level >= spell.LevelReq;
            _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_LevelReq),
                spell.LevelReq.ToString(), meetsLevel ? GoodColor : WarnColor));
        }

        bool meetsInt = me?.Int >= intReq;
        _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_IntReq), UiHelper.FormatRequirement(CombatFormulas.RawSpellRequirement(spell), intReq), meetsInt ? GoodColor : WarnColor));

        if (ClassGate.IsRestricted(spell.AllowedClasses))
        {
            string names = ClassGate.Describe(spell.AllowedClasses, classes);
            if (names.Length > 0)
            {
                bool meetsClass = me != null && ClassGate.Allows(spell.AllowedClasses, me.Class);
                _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_ClassReq), names, meetsClass ? GoodColor : WarnColor));
            }
        }
    }
}
