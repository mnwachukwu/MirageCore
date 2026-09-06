using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Screens;

/// <summary>Character creation: name, sex, and class, with a live preview of everything a level-1
/// character of the chosen class would have — stats, pools, regen, what it hits and soaks for, and the
/// gear and spells it starts with.
///
/// <para>The loadout entries carry the REAL in-game item and spell tooltips. That works because the one
/// thing those tooltips need and this screen lacks — a character — is answered by the class itself: a
/// stat requirement is quoted against the class BASE stat (the affinity head-start is fixed at
/// creation), and a brand-new character's stats ARE the class's. So a synthetic level-1 player of the
/// selected class is a truthful stand-in, and the tooltip runs unmodified.</para></summary>
public sealed class NewCharScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly TextInputField _nameField = new() { MaxLength = Constants.NameLength };
    private readonly ListBox _classList = new();
    private readonly List<int> _classNums = [];
    private readonly Button _maleBtn;
    private readonly Button _femaleBtn;
    private readonly Button _createBtn;
    private readonly Button _cancelBtn;
    private Sex _sex = Sex.Male;
    private InputState _input = new();
    // Button captions are captured in the constructor, so a language switch made while this screen
    // is showing would leave them stale — a menu transition rebuilds the screen, but sitting on it
    // does not. Everything else here is fetched inline at draw time and needs no refresh.
    private int _labelsGeneration = -1;

    private void RefreshLabels()
    {
        _maleBtn.Label = ClientStrings.Get(ClientStrings.NewCharScreen_MaleButton);
        _femaleBtn.Label = ClientStrings.Get(ClientStrings.NewCharScreen_FemaleButton);
        _createBtn.Label = ClientStrings.Get(ClientStrings.Common_Create);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }
    private string _errorMsg = "";
    private long _lastAnimToggleMs;
    private int _animFrame;

    // The synthetic level-1 character of the selected class, rebuilt only when the selection moves.
    // Cached because a PlayerRecord allocates a full inventory array, and this is per-frame draw code.
    private int _previewClassNum = -1;
    private Sex _previewSex = Sex.Male;
    private PlayerRecord? _preview;

    // One tooltip scope for the whole screen; the per-entry key distinguishes rows within it.
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("newchar");

    // ── Layout ────────────────────────────────────────────────────────────────
    // This screen uses the WIDE dialog: (50, 148, 700, 304). The template's 345px content column could
    // not hold a class's numbers AND its starting kit — a ten-entry loadout alone runs past 1200px of
    // names, and dropping the overflow would break the very thing the kit is here to show. The art panel
    // is a fixed 201px either way, so the extra 154px all goes to content and the art is untouched.
    //
    // Content splits into two columns and a full-width band: the controls you operate on the left, the
    // numbers you compare on the right, and the class's own words plus its starting kit underneath.
    private static readonly Rectangle Dlg = UiHelper.WideMenuDialogRect;

    private const int ColLX = 261;          // left column: name, sex, class list
    private const int ColLW = 210;
    private const int ColRX = 489;          // right column: sprite + the derived numbers
    private const int ColRW = 251;
    private const int BandX = ColLX;        // full-width band: description + loadout
    private const int BandW = ColRX + ColRW - ColLX;

    /// <summary>Text row pitch. Tuned to the menu font (Tahoma 10, ~16px line spacing) — the same
    /// 16px the previous layout stepped its stat rows by.</summary>
    private const int RowH = 16;

    private static readonly Rectangle NameRect = new(305, 152, 166, 22);
    private static readonly Rectangle ClassListRect = new(ColLX, 220, ColLW, 100);   // 5 rows
    private static readonly Rectangle SpriteRect =
        new(ColRX + (ColRW - Constants.PicX) / 2, 152, Constants.PicX, Constants.PicY);

    private const int StatsY = 190;         // first of seven stat rows in the right column → 302
    private const int DescY = 324;          // the class's pitch, full width
    private const int LoadoutY = 344;       // the three loadout groups, flowed across the lines below
    /// <summary>Lines the loadout may use. A group starts on a fresh line and spills onto the next when
    /// it runs long, so the budget is shared: four lines hold every class in the shipped roster, and what
    /// a longer one could not fit collapses to a "+N" count rather than pushing the buttons off the
    /// dialog.</summary>
    private const int LoadoutLines = 4;

    public NewCharScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _maleBtn = new Button { Bounds = new Rectangle(ColLX, 180, 100, 20), Label = ClientStrings.Get(ClientStrings.NewCharScreen_MaleButton) };
        _femaleBtn = new Button { Bounds = new Rectangle(ColLX + 110, 180, 100, 20), Label = ClientStrings.Get(ClientStrings.NewCharScreen_FemaleButton) };
        _createBtn = new Button { Bounds = new Rectangle(544, 414, 96, 28), Label = ClientStrings.Get(ClientStrings.Common_Create) };
        _cancelBtn = new Button { Bounds = new Rectangle(644, 414, 96, 28), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
    }

    /// <summary>Reset the entry fields and default the class selection, refreshing the preview.</summary>
    public void OnEnter()
    {
        _nameField.Clear();
        _errorMsg = "";
        _classList.Items.Clear();
        _classNums.Clear();
        _previewClassNum = -1;

        var classes = _ctx.State.Classes;
        if (classes is not null)
        {
            for (int i = 1; i < classes.Length; i++)
            {
                if (classes[i]?.Name.Length > 0)
                {
                    _classList.Items.Add(classes[i].Name.TrimEnd());
                    _classNums.Add(i);
                }
            }
        }
        _classList.SelectedIndex = _classList.Items.Count > 0 ? 0 : -1;
    }

    /// <summary>Drop any tooltip this screen put up, so it can't linger over the screen that replaces
    /// this one.</summary>
    public void OnExit() => Tooltip.CloseScope(_tooltipScope);

    /// <summary>Handle typing, list selection, and the submit key.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        _nameField.Feed(input, Environment.TickCount64);
        _classList.Update(input, ClassListRect);

        if (input.IsKeyPressed(Keys.Enter)) TryCreate();
        if (_maleBtn.IsClicked(input)) _sex = Sex.Male;
        if (_femaleBtn.IsClicked(input)) _sex = Sex.Female;
        if (_createBtn.IsClicked(input)) TryCreate();
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new CharSelectScreen(_ctx));
    }

    /// <summary>Validate the name and selection, then send the create request. The account is already
    /// connected and logged in at this point, so there is no connect step.</summary>
    private void TryCreate()
    {
        if (_nameField.Text.Length < Constants.MinFieldLength)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_NameTooShort);
            return;
        }
        if (_classList.SelectedIndex < 0)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_SelectClass);
            return;
        }
        _errorMsg = "";
        int classNum = _classNums[_classList.SelectedIndex];
        _ctx.Sender.SendAddChar(_nameField.Text, _sex, classNum);
        _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.NewCharScreen_CreatingCharacter));
        _ctx.Screens.Replace(new LoadingScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, the class preview, and any error text.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long now = Environment.TickCount64;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt, Dlg);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.NewCharScreen_Title), Dlg);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_NameLabel), new Vector2(ColLX, NameRect.Y + 4), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_ClassLabel), new Vector2(ColLX, ClassListRect.Y - RowH), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, focused: true, now);

        _maleBtn.Enabled = _sex == Sex.Female;
        _femaleBtn.Enabled = _sex == Sex.Male;
        _maleBtn.Draw(sb, font, _input);
        _femaleBtn.Draw(sb, font, _input);
        UiHelper.DrawBorder(sb, _sex == Sex.Male ? _maleBtn.Bounds : _femaleBtn.Bounds, Color.Gold, 2);

        _classList.Draw(sb, font, ClassListRect);

        DrawClassPreview(sb, font, now);

        if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red, Dlg);

        _createBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);

        // Last, so a loadout tooltip floats above every control on the screen.
        Tooltip.TickAndDraw(sb, font, now, _input.MousePosition);
    }

    /// <summary>Everything that depends on which class is selected: the sprite, the numbers, the pitch,
    /// and the starting loadout.</summary>
    private void DrawClassPreview(SpriteBatch sb, SpriteFont font, long nowMs)
    {
        var classes = _ctx.State.Classes;
        if (classes is null || _classList.SelectedIndex < 0 || _classList.SelectedIndex >= _classNums.Count) return;

        int classNum = _classNums[_classList.SelectedIndex];
        if (classNum >= classes.Length || classes[classNum] is not ClassRecord cls) return;

        // Keyed on the sex too: it decides the sprite, so switching Male/Female has to rebuild.
        if (_previewClassNum != classNum || _previewSex != _sex)
        {
            _previewClassNum = classNum;
            _previewSex = _sex;
            _preview = BuildPreview(classNum, cls, _sex);
        }
        var me = _preview!;
        var loadout = _ctx.State.LoadoutFor(classNum);

        if (nowMs - _lastAnimToggleMs >= 250)
        {
            _animFrame ^= 1;
            _lastAnimToggleMs = nowMs;
        }
        // Swings while the pointer is over Create — a small promise of what the button does.
        bool attacking = _createBtn.IsHovered(_input);
        int frame = attacking ? (nowMs % 1000L < 500L ? 2 : 0) : _animFrame;
        UiHelper.DrawMenuSpritePreview(sb, _ctx.Sprites, cls.SpriteFor(_sex), cls.SpriteSheetFor(_sex), frame, SpriteRect);

        DrawNumbers(sb, font, cls, me, loadout);
        DrawDescription(sb, font, cls);
        DrawLoadout(sb, font, me, loadout);
    }

    /// <summary>A level-1 character of this class, exactly as character creation would make one: stats
    /// copied straight off the class, level 1, pools computed from the shared formulas. The item and
    /// spell tooltips read it as though it were a real player.</summary>
    private static PlayerRecord BuildPreview(int classNum, ClassRecord cls, Sex sex)
    {
        var p = new PlayerRecord
        {
            Class = classNum,
            Sex = sex,
            Sprite = cls.SpriteFor(sex),
            Level = StartingLoadout.CreationLevel,
            Str = cls.Str,
            Def = cls.Def,
            Spd = cls.Spd,
            Int = cls.Int,
        };
        p.MaxHp = StatFormulas.GetPlayerMaxHp(p.Level, cls.Def, cls.Def);
        p.MaxMp = StatFormulas.GetPlayerMaxMp(p.Level, cls.Int, cls.Int);
        p.MaxSp = StatFormulas.GetPlayerMaxSp(p.Level, cls.Spd, cls.Spd);
        p.Hp = p.MaxHp;
        p.Mp = p.MaxMp;
        p.Sp = p.MaxSp;
        return p;
    }

    /// <summary>The right column: raw stats, pools, regen, and what the class actually hits and soaks
    /// for once its starting gear is on. Every value routes through the shared formula classes, so this
    /// preview and the in-game character sheet cannot drift.</summary>
    private void DrawNumbers(SpriteBatch sb, SpriteFont font, ClassRecord cls, PlayerRecord me,
        ClientState.ClassLoadout loadout)
    {
        int half = (ColRW - 4) / 2;
        int col2 = ColRX + half + 4;
        int third = (ColRW - 8) / 3;
        int quarter = (ColRW - 12) / 4;
        int ThirdX(int i) => ColRX + i * (third + 4);
        int QuarterX(int i) => ColRX + i * (quarter + 4);

        // The four base stats read as one spread, so they get one line — the whole class-choosing
        // question is which of the four this class spends its twenty points on.
        int y = StatsY;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Str), cls.Str.ToString(), Color.OrangeRed, QuarterX(0), quarter, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Int), cls.Int.ToString(), Color.DodgerBlue, QuarterX(1), quarter, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Def), cls.Def.ToString(), Color.LimeGreen, QuarterX(2), quarter, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Spd), cls.Spd.ToString(), UiHelper.VitalSpColor, QuarterX(3), quarter, y);
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Hp), me.MaxHp.ToString(), UiHelper.VitalHpColor, ThirdX(0), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Mp), me.MaxMp.ToString(), UiHelper.VitalMpColor, ThirdX(1), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Sp), me.MaxSp.ToString(), UiHelper.VitalSpColor, ThirdX(2), third, y);

        // Regen gets a full-width row each: the labels are long, and these are the numbers that decide
        // how much of a fight is spent waiting rather than fighting.
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_HpRegen), RegenText(StatFormulas.GetPlayerHpRegen(cls.Def)), Color.IndianRed, ColRX, ColRW, y);
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_MpRegen), RegenText(StatFormulas.GetPlayerMpRegen(cls.Int)), Color.SkyBlue, ColRX, ColRW, y);
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_SpRegen), RegenText(StatFormulas.GetPlayerSpRegen(cls.Spd)), UiHelper.VitalSpColor, ColRX, ColRW, y);

        // Sprint sits with the SP rows because it is the other thing SPD buys, and the only one a player
        // can feel. Measured against walking: +100% is twice walk pace, which every class gets before
        // spending a point, and SPD pushes it toward +200%. Without this row SPD reads as a stat that
        // only fills a bar.
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Sprint),
            $"+{MovementFormulas.SprintBonusPercent(cls.Spd)}%", UiHelper.VitalSpColor, ColRX, ColRW, y);

        // Combat output, GEAR INCLUDED — the whole reason the loadout is on this screen. A total rather
        // than the character sheet's "base + gear = total": there is no gear to swap here, so the only
        // honest question is what this class opens the game hitting and soaking for.
        y += RowH;
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_PDmg), PhysDamage(me, loadout).ToString(), Color.White, ThirdX(0), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_MDmg), MagicDamage(me, loadout).ToString(), Color.White, ThirdX(1), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_Mit), Mitigation(me, loadout).ToString(), Color.White, ThirdX(2), third, y);

        // Block or Dodge, never both — the same swap the character sheet makes, decided the same way it
        // is decided in combat: a shield in the slot blocks, an empty one dodges. What picks it here is
        // the class's own STARTING LOADOUT, resolved through the real gates, so the cell answers a
        // question about the character this screen is about to create rather than about the class's
        // eventual potential. No table maps class to avoidance style; edit a starting kit and this
        // follows, because it is reading the same thing the server will equip.
        y += RowH;
        bool hasShield = WornOfType(loadout, ItemType.Shield) is not null;
        DrawPair(sb, font,
            ClientStrings.Get(hasShield ? ClientStrings.Stats_Block : ClientStrings.Stats_Dodge),
            CombatFormulas.FormatPerMilleAsPercent(hasShield
                ? CombatFormulas.PlayerBlockChancePerMille(cls.Def, me.Level)
                : CombatFormulas.PlayerDodgeChancePerMille(cls.Def, me.Level)),
            Color.White, ThirdX(0), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_PCrit),
            CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(cls.Str, me.Level)), Color.White, ThirdX(1), third, y);
        DrawPair(sb, font, ClientStrings.Get(ClientStrings.Stats_MCrit),
            CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(cls.Int, me.Level)), Color.White, ThirdX(2), third, y);
    }

    private static string RegenText(int amount) =>
        ClientStrings.Format(ClientStrings.Stats_RegenFormat, ("Value", amount));

    /// <summary>One label/value cell: label at the left in the muted label color, value right-aligned to
    /// the cell's far edge so a column of numbers lines up. The label is squeezed rather than the value
    /// when the pair is too wide — a clipped number would be a lie, a clipped word is still readable.</summary>
    private static void DrawPair(SpriteBatch sb, SpriteFont font, string label, string value, Color valueColor,
        int x, int cellW, int y)
    {
        float valueW = font.MeasureString(value).X;
        sb.DrawString(font, UiHelper.FitText(font, label, Math.Max(10f, cellW - valueW - 4)), new Vector2(x, y), Color.DimGray);
        sb.DrawString(font, value, new Vector2(x + cellW - valueW, y), valueColor);
    }

    /// <summary>The class's own pitch, one line across the full content width. Quoted, so it reads as
    /// flavor rather than as another field — everything else on this screen is a fact about the class,
    /// and this is the class talking. Plain ASCII quotes: the menu font carries ASCII and Latin-1, and
    /// typographic quotes are in neither. Truncated text reveals itself on hover, like every other
    /// squeezed label in the client.</summary>
    private void DrawDescription(SpriteBatch sb, SpriteFont font, ClassRecord cls)
    {
        string desc = cls.Description.Trim();
        if (desc.Length == 0) return;
        string quoted = $"\"{desc}\"";
        sb.DrawString(font, UiHelper.FitText(font, quoted, BandW), new Vector2(BandX, DescY), UiHelper.DlgLabelColor);
        UiHelper.LabelTooltip(font, quoted, new Rectangle(BandX, DescY, BandW, RowH), _input.MousePosition, _tooltipScope, "desc");
    }

    /// <summary>One entry as the loadout line shows it: which record, the stack a currency arrives as,
    /// and how many copies were granted (two of the same elixir are one entry "x2", not two names).</summary>
    private readonly record struct LoadoutEntry(int Num, int Value, int Count);

    /// <summary>The three starting groups, names only. Everything else — power, requirements, MP cost —
    /// is one hover away in the real tooltip, which is why a whole kit fits in three lines.</summary>
    private void DrawLoadout(SpriteBatch sb, SpriteFont font, PlayerRecord me, ClientState.ClassLoadout loadout)
    {
        int line = 0;
        DrawGroup(sb, font, ClientStrings.Get(ClientStrings.NewCharScreen_WornLabel),
            Group(loadout.Worn.Select(n => (n, 0))), me, spells: false, ref line);
        DrawGroup(sb, font, ClientStrings.Get(ClientStrings.NewCharScreen_CarriedLabel),
            Group(loadout.Carried.Select(c => (c.Num, c.Value))), me, spells: false, ref line);
        DrawGroup(sb, font, ClientStrings.Get(ClientStrings.NewCharScreen_SpellsLabel),
            Group(loadout.Spells.Select(n => (n, 0))), me, spells: true, ref line);
    }

    /// <summary>Collapse repeats into counted entries, keeping the authored order. The bag really does
    /// hold two separate elixirs, but "Minor Healing Elixir, Minor Healing Elixir" spends a line
    /// saying one thing twice.</summary>
    private static List<LoadoutEntry> Group(IEnumerable<(int Num, int Value)> raw)
    {
        var result = new List<LoadoutEntry>();
        foreach (var (num, value) in raw)
        {
            int at = result.FindIndex(e => e.Num == num);
            if (at >= 0) result[at] = result[at] with { Count = result[at].Count + 1 };
            else result.Add(new LoadoutEntry(num, value, 1));
        }
        return result;
    }

    /// <summary>Draw one labeled group, starting on <paramref name="line"/> and wrapping onto the next
    /// when it runs long. Advances <paramref name="line"/> past whatever it used, so the next group
    /// starts fresh underneath.</summary>
    private void DrawGroup(SpriteBatch sb, SpriteFont font, string label, List<LoadoutEntry> entries,
        PlayerRecord me, bool spells, ref int line)
    {
        if (line >= LoadoutLines) return;
        int right = BandX + BandW;
        int y = LoadoutY + line * RowH;

        sb.DrawString(font, label, new Vector2(BandX, y), UiHelper.DlgLabelColor);
        int x = BandX + (int)font.MeasureString(label).X + 6;

        // An empty group is shown, not hidden: "Worn: nothing" is a real statement about a class that
        // opens unarmored, and a blank row would read as missing data instead.
        if (entries.Count == 0)
        {
            sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_LoadoutNone), new Vector2(x, y), Color.DimGray);
            line++;
            return;
        }

        const string Separator = ", ";
        float separatorW = font.MeasureString(Separator).X;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string? name = spells ? _ctx.State.LoadoutSpell(entry.Num)?.TrimmedName : _ctx.State.LoadoutItem(entry.Num)?.TrimmedName;
            if (name is null) continue;
            string text = entry.Count > 1 ? $"{name} x{entry.Count}" : name;

            float w = font.MeasureString(text).X;
            if (x + w > right)
            {
                // Out of lines: say how many were left off rather than truncating a name into something
                // the player would then hunt for on the tooltip and not find.
                if (line + 1 >= LoadoutLines)
                {
                    sb.DrawString(font, $"+{entries.Count - i}", new Vector2(x, y), Color.DimGray);
                    break;
                }
                line++;
                y += RowH;
                x = BandX;
            }

            var hit = new Rectangle(x, y, (int)w, RowH);
            sb.DrawString(font, text, new Vector2(x, y), Color.White);
            if (hit.Contains(_input.MousePosition)) ShowEntryTooltip(entry.Num, entry.Value, me, spells);

            x += (int)w;
            if (i < entries.Count - 1 && x + separatorW <= right)
            {
                sb.DrawString(font, Separator, new Vector2(x, y), Color.DimGray);
                x += (int)separatorW;
            }
        }
        line++;
    }

    /// <summary>Hand the hovered entry to the shared tooltip, which then renders exactly what it would
    /// in game. The <c>classes</c> table is the live one, so a class-restricted piece names its classes;
    /// the synthetic player supplies the level and stats every requirement line is colored against.</summary>
    private void ShowEntryTooltip(int num, int value, PlayerRecord me, bool spells)
    {
        if (spells)
        {
            if (_ctx.State.LoadoutSpell(num) is { } spell)
            {
                // No weather before joining, so the SubHp reagent line quotes its base (un-rained) cost.
                Tooltip.NotifyHoverSpell(_tooltipScope, ("spell", num), spell, me, _ctx.State.Classes,
                    _ctx.State.LoadoutItemDefs, WeatherType.Clear, _input.MousePosition);
            }
            return;
        }
        if (_ctx.State.LoadoutItem(num) is not { } item) return;
        // A stand-in bag slot: currency shows the stack it arrives as, equipment shows full durability,
        // because nothing has been used yet.
        var slot = new PlayerInvSlot { Num = num, Quantity = value, Dur = item.Durability };
        Tooltip.NotifyHoverItem(_tooltipScope, ("item", num), item, slot, me, _ctx.State.Classes,
            _ctx.Items, _input.MousePosition);
    }

    // ── Derived combat values, gear included ──────────────────────────────────

    private ItemRecord? WornOfType(ClientState.ClassLoadout loadout, ItemType type)
    {
        foreach (int num in loadout.Worn)
            if (_ctx.State.LoadoutItem(num) is { } item && item.Type == type) return item;
        return null;
    }

    /// <summary>Unarmed damage plus the starting weapon's contribution — the mirror of the character
    /// sheet's P-DMG row with the weapon equipped, which is exactly the state this class begins in.</summary>
    private int PhysDamage(PlayerRecord me, ClientState.ClassLoadout loadout)
    {
        int total = CombatFormulas.UnarmedDamage(me.Str);
        if (WornOfType(loadout, ItemType.Weapon) is { } weapon)
            total += CombatFormulas.WeaponContribution(weapon.Power, me.Str);
        return total;
    }

    /// <summary>Base spell power plus the STRONGEST offensive starting spell, so a caster is measured
    /// with its opening spell in hand for the same reason a fighter is measured with its sword. A class
    /// whose whole book is healing shows the base, which is what it would hit for casting nothing.</summary>
    private int MagicDamage(PlayerRecord me, ClientState.ClassLoadout loadout)
    {
        int best = 0;
        foreach (int num in loadout.Spells)
        {
            if (_ctx.State.LoadoutSpell(num) is not { } spell) continue;
            if (spell.Type is not (SpellType.SubHp or SpellType.SubMp or SpellType.SubSp)) continue;
            int contribution = CombatFormulas.SpellContribution(spell.VitalAmount, me.Int);
            if (contribution > best) best = contribution;
        }
        return CombatFormulas.SpellPower(me.Int) + best;
    }

    /// <summary>The level/DEF baseline plus every worn piece, on the mirror's one universal mitigation
    /// axis — this number defends physical and magic alike.</summary>
    private int Mitigation(PlayerRecord me, ClientState.ClassLoadout loadout)
    {
        int total = CombatFormulas.PlayerProtection(me.Level, me.Def);
        foreach (int num in loadout.Worn)
        {
            if (_ctx.State.LoadoutItem(num) is not { } item) continue;
            total += item.Type switch
            {
                ItemType.Armor or ItemType.Helmet => CombatFormulas.GearMitigation(item.Power, me.Def),
                ItemType.Shield => CombatFormulas.ShieldMitigation(item.Power, me.Def),
                _ => 0,
            };
        }
        return total;
    }
}
