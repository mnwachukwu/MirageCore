using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The action bar: four slots, each holding an item or a spell, bound by NUMBER (see
/// <see cref="PlayerHotkey"/>) and fired with 1–4 or the gamepad's face buttons under a trigger.
///
/// <para>Not an <c>IGamePanel</c> — it is chrome, not a window. It never opens or closes, never takes
/// focus, and never blocks movement; it draws in the sidebar strip just above the Mail/Options/Help
/// links and only ever claims a right-click that actually lands on one of its four boxes.</para>
///
/// <para>Everything it shows is derived from <see cref="ClientState"/> each frame rather than cached: a
/// bound slot goes gray the moment the bag runs out and colors again when it is restocked, with no
/// invalidation to get wrong.</para>
/// </summary>
public static class HotkeyBarPanel
{
    public const int IconSize = 32;
    private const int Gap = 6;
    // Sit the row directly above the link strip HudPanel lays out, with the same breathing gap the
    // links themselves use, so the two read as one stack in the sidebar rather than two stray widgets.
    private const int BarBottomGap = 6;
    private const int BarY = HudPanel.LinkStripY - IconSize - BarBottomGap;
    private const int BarW = Constants.MaxHotkeys * IconSize + (Constants.MaxHotkeys - 1) * Gap;
    private const int BarX = HudPanel.LinkStripCenterX - BarW / 2;

    // The key badge sits in the box's bottom-right corner, on its own dark plate so a digit stays legible
    // over a busy icon.
    private const int BadgeW = 11, BadgeH = 11;

    // How many the bag holds, in the opposite corner from the key badge so the two can never crowd each
    // other. Its plate is sized to the digits rather than fixed: a stack of gold runs to four of them.
    private const int CountPadX = 2;

    // Cooldown sweep: a fan of thin spokes from the box center, since SpriteBatch draws quads and a
    // genuine pie wedge would need geometry. 48 spokes over a 32px box leaves no visible gaps.
    private const int SweepSpokes = 48;

    private static readonly Color EmptyFill = new(18, 22, 24, 200);
    private static readonly Color BoundFill = new(28, 34, 36, 210);
    private static readonly Color UnavailableTint = new(70, 70, 70);
    private static readonly Color CooldownVeil = new(6, 8, 10, 165);
    private static readonly Color BadgePlate = new(0, 0, 0, 190);
    private static readonly Color BookCover = new(122, 74, 46);
    private static readonly Color BookPages = new(226, 220, 202);
    private static readonly Color BookSpine = new(72, 42, 26);

    /// <summary>Screen rect of one 1-based slot.</summary>
    public static Rectangle SlotBounds(int slot) =>
        new(BarX + (slot - 1) * (IconSize + Gap), BarY, IconSize, IconSize);

    /// <summary>The whole bar, for hit-testing "is the mouse over the bar at all".</summary>
    public static Rectangle Bounds => new(BarX, BarY, BarW, IconSize);

    /// <summary>The 1-based slot under a point, or 0.</summary>
    public static int SlotAt(Point p)
    {
        for (int i = 1; i <= Constants.MaxHotkeys; i++)
            if (SlotBounds(i).Contains(p)) return i;
        return 0;
    }

    // ── Resolution: a bound NUMBER to a live slot ────────────────────────────
    // Both of these are the reason hotkeys store numbers. They run per frame for drawing and again on
    // use; they are linear scans over 24-ish entries, which is nothing next to a draw call.

    /// <summary>First inventory slot holding this item number, or 0 when the bag has none.</summary>
    public static int FindInvSlot(ClientState state, int itemNum)
    {
        var me = state.Me;
        if (me?.Inv is null || itemNum <= 0) return 0;
        for (int i = 1; i <= Constants.MaxInv && i < me.Inv.Length; i++)
            if (me.Inv[i]?.Num == itemNum) return i;
        return 0;
    }

    /// <summary>Spellbook slot holding this spell number, or 0 when it is not known.</summary>
    public static int FindSpellSlot(ClientState state, int spellNum)
    {
        var me = state.Me;
        if (me?.Spell is null || spellNum <= 0) return 0;
        for (int i = 1; i <= Constants.MaxPlayerSpells && i < me.Spell.Length; i++)
            if (me.Spell[i] == spellNum) return i;
        return 0;
    }

    /// <summary>Whether the slot's binding can be acted on right now — the item is in the bag, or the
    /// spell is still known. Drives the gray/color state and is re-read every frame.</summary>
    public static bool IsAvailable(ClientState state, PlayerHotkey hk) => hk.Kind switch
    {
        HotkeyKind.Item => FindInvSlot(state, hk.Num) > 0,
        HotkeyKind.Spell => FindSpellSlot(state, hk.Num) > 0,
        _ => false,
    };

    // ── Draw ─────────────────────────────────────────────────────────────────

    /// <param name="cooldownOf">How much of a slot's cooldown is still to run, 1→0, asked per slot.
    /// There are two clocks — drinking and the action beat — so a bar holding a potion beside a spell
    /// shows two different sweeps, and one value could not describe the row.</param>
    public static void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex,
                            Func<PlayerHotkey, float> cooldownOf, bool gamepadActive, InputState input, bool canHover)
    {
        var me = state.Me;
        if (me?.Hotkeys is null) return;

        for (int slot = 1; slot <= Constants.MaxHotkeys; slot++)
        {
            var box = SlotBounds(slot);
            var hk = slot < me.Hotkeys.Length ? me.Hotkeys[slot] : PlayerHotkey.Empty;
            bool available = hk.IsBound && IsAvailable(state, hk);

            UiHelper.DrawFilledRect(sb, box, hk.IsBound ? BoundFill : EmptyFill);

            if (hk.IsBound)
            {
                // Unavailable draws the same art dimmed rather than blank: the player needs to see WHICH
                // potion they have run out of, not merely that the slot is unusable.
                var tint = available ? Color.White : UnavailableTint;
                if (hk.Kind == HotkeyKind.Item) DrawItemIcon(sb, box, state, itemsTex, hk.Num, tint);
                else DrawBookIcon(sb, box, tint);
            }

            UiHelper.DrawBorder(sb, box, UiHelper.UiControlBorder);
            if (hk.IsBound && hk.Kind == HotkeyKind.Item) DrawHeldCount(sb, font, box, state, hk.Num);
            DrawKeyBadge(sb, font, box, slot, gamepadActive);
        }

        // The sweeps go on last so they veil the icons rather than sitting under them. An empty slot is
        // never veiled: it has nothing to be waiting on.
        for (int slot = 1; slot <= Constants.MaxHotkeys; slot++)
        {
            var hk = slot < me.Hotkeys.Length ? me.Hotkeys[slot] : PlayerHotkey.Empty;
            if (!hk.IsBound) continue;
            float fraction = cooldownOf(hk);
            if (fraction > 0f) DrawCooldownSweep(sb, SlotBounds(slot), fraction);
        }

        // The trigger modifier is a property of the GROUP, not of any one slot, so it is labeled once to
        // the left of the row rather than repeated on all four badges.
        if (gamepadActive)
        {
            string mod = ClientStrings.Get(ClientStrings.HotkeyBar_GamepadModifier);
            var size = font.MeasureString(mod);
            sb.DrawString(font, mod, new Vector2(BarX - size.X - Gap, BarY + (IconSize - size.Y) / 2f), Color.LightGray);
        }

        if (canHover) NotifyHover(state, input, itemsTex);
    }

    private static void DrawItemIcon(SpriteBatch sb, Rectangle box, ClientState state, IReadOnlyList<Texture2D?> itemsTex, int itemNum, Color tint)
    {
        if (itemNum <= 0 || itemNum >= state.Items.Length) return;
        sb.DrawItemIcon(itemsTex, state.Items[itemNum], box, tint);
    }

    /// <summary>A spell has no art of its own, so every spell slot shows the same book and the hover
    /// tooltip says which one. Keeps the bar readable at 32px, where distinct glyphs would not be.</summary>
    private static void DrawBookIcon(SpriteBatch sb, Rectangle box, Color tint)
    {
        int pad = 5;
        var cover = new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2);
        UiHelper.DrawFilledRect(sb, cover, Multiply(BookCover, tint));
        // Page block inset from the fore-edge, and a darker spine down the left.
        var pages = new Rectangle(cover.X + 5, cover.Y + 3, cover.Width - 8, cover.Height - 6);
        UiHelper.DrawFilledRect(sb, pages, Multiply(BookPages, tint));
        var spine = new Rectangle(cover.X, cover.Y, 4, cover.Height);
        UiHelper.DrawFilledRect(sb, spine, Multiply(BookSpine, tint));
    }

    private static Color Multiply(Color c, Color tint) =>
        new(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, c.A);

    /// <summary>How many of a bound item the bag holds, in the box's top-left corner — the corner the key
    /// badge does not use. Drawn whenever the player holds any, including the last one: running down to a
    /// single potion is exactly when the number earns its place. At zero the slot is already grayed, and a
    /// count there would only repeat what the dimming says.</summary>
    private static void DrawHeldCount(SpriteBatch sb, SpriteFont font, Rectangle box, ClientState state, int itemNum)
    {
        int held = InventoryQuery.HeldCount(state, itemNum);
        if (held <= 0) return;

        string label = HeldCountLabel(held);
        var size = font.MeasureString(label);
        var plate = new Rectangle(box.X + 1, box.Y + 1, (int)size.X + CountPadX * 2, BadgeH);
        UiHelper.DrawFilledRect(sb, plate, BadgePlate);
        sb.DrawString(font, label,
            new Vector2(plate.X + CountPadX, plate.Y + (plate.Height - size.Y) / 2f),
            Color.White);
    }

    /// <summary>The count as the corner badge shows it, capped at four characters.
    ///
    /// <para>A stack runs to billions, and the badge sits inside a 32-pixel icon: past three digits the
    /// plate grows over the art it belongs to. Above the cap the exact figure is not what the badge is for
    /// anyway — it answers "have I got plenty", and the bag and the tooltip both carry the real number.</para></summary>
    internal static string HeldCountLabel(int held) => held > MaxShownCount ? $"{MaxShownCount}+" : held.ToString();

    /// <summary>The largest count shown exactly; more reads as "999+".</summary>
    internal const int MaxShownCount = 999;

    private static void DrawKeyBadge(SpriteBatch sb, SpriteFont font, Rectangle box, int slot, bool gamepadActive)
    {
        var plate = new Rectangle(box.Right - BadgeW - 1, box.Bottom - BadgeH - 1, BadgeW, BadgeH);
        UiHelper.DrawFilledRect(sb, plate, BadgePlate);

        if (gamepadActive && GamepadGlyphs.PreferPlayStation)
        {
            GamepadGlyphs.DrawPlayStationFace(sb, plate, PlayStationFace(slot));
            return;
        }

        string label = gamepadActive ? GamepadFace(slot) : slot.ToString();
        var size = font.MeasureString(label);
        sb.DrawString(font, label,
            new Vector2(plate.X + (plate.Width - size.X) / 2f, plate.Y + (plate.Height - size.Y) / 2f),
            Color.White);
    }

    /// <summary>Slot to face button: 1→X, 2→Y, 3→B, 4→A. Not arbitrary — X/Y/B are the legacy HP/MP/SP
    /// potion buttons, so existing muscle memory carries over, and the fourth slot takes A (still plain
    /// pickup without the trigger held).</summary>
    public static string GamepadFace(int slot) => slot switch { 1 => "X", 2 => "Y", 3 => "B", _ => "A" };

    /// <summary>The same four physical positions in Sony's vocabulary: X→Square, Y→Triangle, B→Circle,
    /// A→Cross. One mapping, two names — the button under the thumb never moves.</summary>
    public static GamepadGlyphs.PsFace PlayStationFace(int slot) => slot switch
    {
        1 => GamepadGlyphs.PsFace.Square,
        2 => GamepadGlyphs.PsFace.Triangle,
        3 => GamepadGlyphs.PsFace.Circle,
        _ => GamepadGlyphs.PsFace.Cross,
    };

    /// <summary>The dimming veil for the shared cooldown, drawn as a spoke fan sweeping clockwise from
    /// twelve o'clock. <paramref name="fraction"/> is how much is LEFT, so the veil retreats and the icon
    /// returns to color as the beat runs out.</summary>
    private static void DrawCooldownSweep(SpriteBatch sb, Rectangle box, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var center = new Vector2(box.Center.X, box.Center.Y);
        // Circumscribed: the spokes reach past the corners so the veil covers the icon completely instead of
        // leaving four lit triangles. That overhangs the slot on all four sides, and each spoke carries half
        // its own width further still, so the whole fan is drawn inside a scissor clip on the slot rect. The
        // geometry stays simple and the pixels stop at the border.
        float radius = MathF.Sqrt(box.Width * box.Width + box.Height * box.Height) / 2f + 1f;
        int spokes = Math.Max(1, (int)MathF.Ceiling(SweepSpokes * fraction));
        float sweep = MathF.Tau * fraction;
        // Thick enough that neighboring spokes overlap at the rim rather than fanning into stripes.
        float thickness = radius * sweep / spokes + 2f;

        UiHelper.BeginClip(sb, box);
        for (int i = 0; i <= spokes; i++)
        {
            float a = -MathF.PI / 2f + sweep * i / spokes;
            var tip = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            UiHelper.DrawLine(sb, center, tip, CooldownVeil, thickness);
        }
        UiHelper.EndClip(sb);
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    private static void NotifyHover(ClientState state, InputState input, IReadOnlyList<Texture2D?> itemsTex)
    {
        var me = state.Me;
        if (me?.Hotkeys is null) return;
        int slot = SlotAt(input.MousePosition);
        if (slot == 0) return;

        var hk = slot < me.Hotkeys.Length ? me.Hotkeys[slot] : PlayerHotkey.Empty;
        if (!hk.IsBound)
        {
            Tooltip.NotifyHoverText(TooltipScope, (TooltipScope, slot),
                ClientStrings.Get(ClientStrings.HotkeyBar_EmptyHint), input.MousePosition);
            return;
        }

        if (hk.Kind == HotkeyKind.Item && hk.Num > 0 && hk.Num < state.Items.Length)
        {
            // Reuse the real item tooltip so a hotkeyed weapon reads exactly as it does in the bag —
            // requirements, mitigation and all — rather than getting a second, thinner description.
            // The live inventory slot (when there is one) carries durability and stack size.
            if (state.Items[hk.Num] is { } item)
            {
                int inv = FindInvSlot(state, hk.Num);
                Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, slot), item,
                    inv > 0 ? me.Inv[inv] : null, me, state.Classes, itemsTex, input.MousePosition,
                    state.SpellDefs, state.Items, state.Weather);
            }
        }
        else if (hk.Kind == HotkeyKind.Spell && hk.Num > 0 && hk.Num < state.SpellDefs.Length)
        {
            // The whole point of the shared book glyph: the tooltip is what says which spell this is.
            if (state.SpellDefs[hk.Num] is { } spell)
                Tooltip.NotifyHoverSpell(TooltipScope, (TooltipScope, slot), spell,
                    me, state.Classes, state.Items, state.Weather, input.MousePosition);
        }
    }

    private const string TooltipScope = "hotkeybar";
}
