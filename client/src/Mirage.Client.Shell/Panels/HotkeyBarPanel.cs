using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The action bar: as many slots as this game declared, each holding a verb or a record, fired with the
/// digit keys or the gamepad’s face buttons under a trigger.
///
/// <para>🔴 <b>No bar at all when a game declared none.</b> A world of conversations and walking wants no
/// action bar, and a row of empty boxes in the sidebar is chrome nobody asked for. Every measurement here
/// reads the declared count, so the row is as wide as the game said and no wider.</para>
///
/// <para><b>What each slot shows arrives with the binding.</b> A slot may hold a record of a family this
/// client has never heard of, so the server says what to draw and what to call it. Core’s own items are
/// the exception this client answers for itself — it has the bag, so it counts them and grays a slot the
/// bag cannot fill, which no description sent once could keep current.</para>
///
/// <para>Not an <c>IGamePanel</c> — it is chrome, not a window. It never opens or closes, never takes
/// focus, and never blocks movement; it draws in the sidebar strip just above the Mail/Options/Help
/// links and only ever claims a right-click that actually lands on one of its boxes.</para>
/// </summary>
public static class HotkeyBarPanel
{
    public const int IconSize = 32;
    private const int Gap = 6;
    // Sit the row directly above the link strip HudPanel lays out, with the same breathing gap the
    // links themselves use, so the two read as one stack in the sidebar rather than two stray widgets.
    private const int BarBottomGap = 6;
    private const int BarY = HudPanel.LinkStripY - IconSize - BarBottomGap;

    /// <summary>How wide a bar of that many slots is, and where its left edge falls. Both read the
    /// declared count rather than a constant, so the row stays centered under the sidebar at any
    /// width.</summary>
    private static int WidthOf(int slots) => slots <= 0 ? 0 : slots * IconSize + (slots - 1) * Gap;

    private static int LeftOf(int slots) => HudPanel.LinkStripCenterX - WidthOf(slots) / 2;

    // The key badge sits in the box's bottom-right corner, on its own dark plate so a digit stays legible
    // over a busy icon.
    private const int BadgeW = 11, BadgeH = 11;

    // How many the bag holds, in the opposite corner from the key badge so the two can never crowd each
    // other. Its plate is sized to the digits rather than fixed: a stack of gold runs to four of them.
    private const int CountPadX = 2;

    // Cooldown sweep: a fan of thin spokes from the box center, since SpriteBatch draws quads and a
    // genuine pie wedge would need geometry. 48 spokes over a 32px box leaves no visible gaps.
    private const int SweepSpokes = 48;

    private static readonly Color EmptyFill = new(14, 26, 22, 200);
    private static readonly Color BoundFill = new(22, 40, 34, 210);
    private static readonly Color UnavailableTint = new(70, 70, 70);
    private static readonly Color CooldownVeil = new(6, 8, 10, 165);
    private static readonly Color BadgePlate = new(0, 0, 0, 190);
    private static readonly Color BookCover = new(122, 74, 46);
    private static readonly Color BookPages = new(226, 220, 202);
    private static readonly Color BookSpine = new(72, 42, 26);

    /// <summary>Screen rect of one 1-based slot on a bar of that many.</summary>
    public static Rectangle SlotBounds(int slots, int slot) =>
        new(LeftOf(slots) + (slot - 1) * (IconSize + Gap), BarY, IconSize, IconSize);

    /// <summary>The whole bar, for hit-testing "is the mouse over the bar at all". Empty for a game that
    /// declared none, so nothing hovers it and nothing is drawn beside it.</summary>
    public static Rectangle Bounds(int slots) =>
        slots <= 0 ? Rectangle.Empty : new Rectangle(LeftOf(slots), BarY, WidthOf(slots), IconSize);

    /// <summary>The 1-based slot under a point, or 0.</summary>
    public static int SlotAt(int slots, Point p)
    {
        for (int i = 1; i <= slots; i++)
            if (SlotBounds(slots, i).Contains(p)) return i;
        return 0;
    }

    // ── Resolution: a bound NUMBER to a live slot ────────────────────────────
    // Hotkeys store numbers for these two. They run per frame for drawing and again on
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

    /// <summary>Whether the slot holds one of Core’s own items, which is the one kind this client can
    /// say anything about on its own: it has the bag and the item sheet.</summary>
    public static bool IsHeldItem(PlayerHotkeysPacket.Slot hk) =>
        hk.Kind == (byte)HotkeyKind.Record
        && string.Equals(hk.Id, CoreRecordFamilies.Items, StringComparison.Ordinal);

    public static bool IsBound(PlayerHotkeysPacket.Slot hk) =>
        hk.Kind != (byte)HotkeyKind.None && !string.IsNullOrEmpty(hk.Id);

    /// <summary>Whether the slot’s binding can be acted on right now. Re-read every frame, so a slot
    /// grays the moment the bag runs out and colors again when it is restocked.
    ///
    /// <para>⚠ Only an item can be answered here. Whether a game’s own record is usable is a rule that
    /// lives on the server, and a client guessing at it would gray a slot that works or light one that
    /// does not — so a game’s slots are always drawn live and the server refuses what it must.</para></summary>
    public static bool IsAvailable(ClientState state, PlayerHotkeysPacket.Slot hk) =>
        !IsHeldItem(hk) || FindInvSlot(state, hk.Num) > 0;

    // ── Draw ─────────────────────────────────────────────────────────────────

    /// <param name="cooldownOf">How much of a slot's cooldown is still to run, 1→0, asked per slot.
    /// There are two clocks — drinking and the action beat — so a bar holding a potion beside a spell
    /// shows two different sweeps, and one value could not describe the row.</param>
    public static void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex,
                            Func<PlayerHotkeysPacket.Slot, float> cooldownOf, bool gamepadActive,
                            InputState input, bool canHover)
    {
        int slots = state.HotkeySlots;
        if (slots <= 0) return;   // a game that declared no bar gets no row of empty boxes

        var bar = state.Hotkeys;

        for (int slot = 1; slot <= slots; slot++)
        {
            var box = SlotBounds(slots, slot);
            var hk = At(bar, slot);
            bool bound = IsBound(hk);
            bool available = bound && IsAvailable(state, hk);

            UiHelper.DrawFilledRect(sb, box, bound ? BoundFill : EmptyFill);

            if (bound)
            {
                // Unavailable draws the same art dimmed rather than blank: the player needs to see WHICH
                // potion they have run out of, not merely that the slot is unusable.
                var tint = available ? Color.White : UnavailableTint;
                DrawSlotArt(sb, box, state, itemsTex, hk, tint);
            }

            UiHelper.DrawBorder(sb, box, UiHelper.UiControlBorder);
            if (bound && IsHeldItem(hk)) DrawHeldCount(sb, font, box, state, hk.Num);
            DrawKeyBadge(sb, font, box, slot, gamepadActive);
        }

        // The sweeps go on last so they veil the icons rather than sitting under them. An empty slot is
        // never veiled: it has nothing to be waiting on.
        for (int slot = 1; slot <= slots; slot++)
        {
            var hk = At(bar, slot);
            if (!IsBound(hk)) continue;
            float fraction = cooldownOf(hk);
            if (fraction > 0f) DrawCooldownSweep(sb, SlotBounds(slots, slot), fraction);
        }

        // The trigger modifier is a property of the GROUP, not of any one slot, so it is labeled once to
        // the left of the row rather than repeated on every badge.
        if (gamepadActive)
        {
            string mod = ClientStrings.Get(ClientStrings.HotkeyBar_GamepadModifier);
            var size = font.MeasureString(mod);
            sb.DrawString(font, mod,
                new Vector2(LeftOf(slots) - size.X - Gap, BarY + (IconSize - size.Y) / 2f), Color.LightGray);
        }

        if (canHover) NotifyHover(state, input, itemsTex);
    }

    /// <summary>The slot at that 1-based position, or an empty one. The bar is resized by the server, so
    /// a frame drawn between a resize and its packet would otherwise index past it.</summary>
    public static PlayerHotkeysPacket.Slot At(PlayerHotkeysPacket.Slot[]? bar, int slot) =>
        bar is not null && slot >= 0 && slot < bar.Length
            ? bar[slot]
            : new PlayerHotkeysPacket.Slot((byte)HotkeyKind.None, string.Empty, 0, string.Empty, string.Empty, 0);

    /// <summary>The picture in a slot: an item’s own art where the server sent one, and otherwise the
    /// glyph its declaration named. A game’s records have no art this client holds, so a spell and a
    /// recipe are told apart by their family’s glyph and the tooltip.</summary>
    private static void DrawSlotArt(SpriteBatch sb, Rectangle box, ClientState state,
                                    IReadOnlyList<Texture2D?> itemsTex,
                                    PlayerHotkeysPacket.Slot hk, Color tint)
    {
        if (IsHeldItem(hk))
        {
            DrawItemIcon(sb, box, state, itemsTex, hk.Num, tint);
            return;
        }

        int pad = 4;
        GameIcons.Draw(sb, GameIcon.Or(hk.Icon),
            new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2), tint);
    }

    private static void DrawItemIcon(SpriteBatch sb, Rectangle box, ClientState state, IReadOnlyList<Texture2D?> itemsTex, int itemNum, Color tint)
    {
        if (itemNum <= 0 || itemNum >= state.Items.Length) return;
        sb.DrawItemIcon(itemsTex, state.Items[itemNum], box, tint);
    }

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

        // Past the fourth slot a pad has no face button, so those badges keep their digit whichever way
        // the player is holding the game.
        bool onPad = gamepadActive && GamepadFace(slot).Length > 0;

        if (onPad && GamepadGlyphs.PreferPlayStation)
        {
            GamepadGlyphs.DrawPlayStationFace(sb, plate, PlayStationFace(slot));
            return;
        }

        string label = onPad ? GamepadFace(slot) : KeyLabel(slot);
        var size = font.MeasureString(label);
        sb.DrawString(font, label,
            new Vector2(plate.X + (plate.Width - size.X) / 2f, plate.Y + (plate.Height - size.Y) / 2f),
            Color.White);
    }

    /// <summary>Slot to face button: 1→X, 2→Y, 3→B, 4→A, and nothing past the fourth.
    ///
    /// <para>Not arbitrary — X/Y/B are the legacy HP/MP/SP potion buttons, so existing muscle memory
    /// carries over, and the fourth slot takes A (still plain pickup without the trigger held). A pad has
    /// four face buttons and a game may declare more slots than that; the rest are reached with the
    /// digits or the mouse, and their badges stay numbered.</para></summary>
    public static string GamepadFace(int slot) =>
        slot switch { 1 => "X", 2 => "Y", 3 => "B", 4 => "A", _ => string.Empty };

    /// <summary>The same four physical positions in Sony's vocabulary: X→Square, Y→Triangle, B→Circle,
    /// A→Cross. One mapping, two names — the button under the thumb never moves.</summary>
    public static GamepadGlyphs.PsFace PlayStationFace(int slot) => slot switch
    {
        1 => GamepadGlyphs.PsFace.Square,
        2 => GamepadGlyphs.PsFace.Triangle,
        3 => GamepadGlyphs.PsFace.Circle,
        _ => GamepadGlyphs.PsFace.Cross,
    };

    /// <summary>The digit that fires a slot: 1 through 9, then 0 for the tenth. Past that there is no
    /// eleventh digit, and the badge is blank — those slots are reached by clicking them.</summary>
    public static string KeyLabel(int slot) => slot switch
    {
        >= 1 and <= 9 => slot.ToString(),
        HotkeyBar.Keyed => "0",
        _ => string.Empty,
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
        int slot = SlotAt(state.HotkeySlots, input.MousePosition);
        if (slot == 0) return;

        var hk = At(state.Hotkeys, slot);
        if (!IsBound(hk))
        {
            Tooltip.NotifyHoverText(TooltipScope, (TooltipScope, slot),
                ClientStrings.Get(ClientStrings.HotkeyBar_EmptyHint), input.MousePosition);
            return;
        }

        if (IsHeldItem(hk) && hk.Num > 0 && hk.Num < state.Items.Length && state.Items[hk.Num] is { } item)
        {
            // Reuse the real item tooltip so a hotkeyed weapon reads exactly as it does in the bag —
            // requirements, mitigation and all — rather than getting a second, thinner description.
            // The live inventory slot (when there is one) carries durability and stack size.
            int inv = FindInvSlot(state, hk.Num);
            Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, slot), item,
                inv > 0 ? me.Inv[inv] : null, me, itemsTex, input.MousePosition,
                state.Items, state.Weather);
            return;
        }

        // Everything else wears the caption the server sent. It is all this client has, and all the
        // player needs: which verb, or which of the game’s records.
        Tooltip.NotifyHoverText(TooltipScope, (TooltipScope, slot),
            ClientStrings.GetOrFallback(hk.Caption, hk.Caption), input.MousePosition);
    }

    private const string TooltipScope = "hotkeybar";
}
