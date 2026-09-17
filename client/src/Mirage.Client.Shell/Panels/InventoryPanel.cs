using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>Inventory panel. Buttons act on the
/// selected slot; right-click for the bulk Drop 1/X/All menu.</summary>
public sealed class InventoryPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 280, 340), minH: 142);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    private const string TooltipScope = "Inv";

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _stateDirty = true;
        }
        else
        {
            _prompt.Close();
            _contextMenu.Close();
            _dropConfirm.Close();
            Tooltip.CloseScope(TooltipScope);
            _view = InvView.List;
        }
    }

    // True while a number prompt or context menu is showing; used by GameplayScreen to suppress
    // Escape-closes-panel so Escape cancels the prompt/menu instead.
    public bool IsCapturingInput => _prompt.IsOpen || _contextMenu.IsOpen || _dropConfirm.IsOpen;

    private readonly ListBox _list = new() { ShowTruncationTooltip = false };   // rows show the richer item tooltip instead
    private readonly Button _useBtn = new();
    private readonly Button _dropBtn = new();
    private readonly ContextMenu _contextMenu = new();
    private readonly NumberPromptDialog _prompt = new();
    private readonly ConfirmDialog _dropConfirm = new();   // warns before dropping a DestroyOnDrop item
    private InputState _input = new();
    private SpriteFont? _cachedFont;

    // Equipment sub-view: [Sort]/[Equipment] links sit right-justified in an 18px strip above the
    // list; [Equipment] swaps the list for the equipment paper-doll (DrawEquipment / EquipmentHitTest,
    // folded in at the bottom of this class) with a bottom Back button.
    private enum InvView { List, Equipment }
    private InvView _view = InvView.List;
    private const int LinkStripH = 18;
    private readonly Link _sortLink = new();
    private readonly Link _equipLink = new();
    private readonly Button _backBtn = new();

    private int _stateHash;
    private bool _stateDirty = true;
    private int _labelsGeneration = -1;

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    /// <summary>Whether the global beat has come round. A POTION spends the same tick as a swing or a
    /// cast; nothing else does. The server enforces it, and asking here only keeps Use from looking
    /// broken while the beat is still running. Unset means always ready.</summary>
    public Func<bool>? CanUseConsumable { get; set; }

    private bool ConsumableReady => CanUseConsumable?.Invoke() ?? true;

    private bool UseReady(ClientState state)
    {
        if (_list.SelectedIndex < 0) return false;
        var inv = state.Me.Inv[_list.SelectedIndex + 1];
        var type = inv.Num > 0 && inv.Num < state.Items.Length ? state.Items[inv.Num]?.Type : null;
        return type != ItemType.Consumable || ConsumableReady;
    }

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            _prompt.Close();
            _contextMenu.Close();
            _dropConfirm.Close();
            Tooltip.CloseScope(TooltipScope);
            _view = InvView.List;
            return;
        }

        var c = _panel.ContentBounds;
        long nowMs = Environment.TickCount64;

        if (_dropConfirm.IsOpen)
        {
            _dropConfirm.Update(input);
            return;
        }

        if (_prompt.IsOpen)
        {
            _prompt.Update(input, c, nowMs);
            return;
        }

        if (_contextMenu.IsOpen && _cachedFont != null)
        {
            _contextMenu.Update(input, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
            return;
        }

        // Links / back button need the font measured; it's captured in Draw. On the very first frame
        // (before any Draw) skip — nothing is clickable until it's been drawn once anyway.
        if (_cachedFont != null) SyncChrome(c, _cachedFont);

        if (_view == InvView.Equipment)
        {
            if (_backBtn.IsClicked(input))
            {
                _view = InvView.List;
                _contextMenu.Close();
                Tooltip.CloseScope(TooltipScope);
                input.ConsumeMouseClick();
                return;
            }
            // Right-click a piece → Unequip menu; the hover tooltip is suppressed while it's open.
            if (input.IsRightMouseClicked() && _cachedFont != null &&
                EquipmentHitTest(state, c, _cachedFont, input.MousePosition) is { } hit)
            {
                input.ConsumeRightMouseClick();
                OpenUnequipContextMenu(hit.InvSlot, hit.Item, input.MousePosition, sender);
            }
            return;
        }

        // ── List view ── hit-test the links before the list so a click doesn't fall through to a row.
        if (_sortLink.IsClicked(input))
        {
            sender.SendSortInventory();
            input.ConsumeMouseClick();
            return;
        }
        if (_equipLink.IsClicked(input))
        {
            _view = InvView.Equipment;
            _contextMenu.Close();
            Tooltip.CloseScope(TooltipScope);
            input.ConsumeMouseClick();
            return;
        }

        _useBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _dropBtn.Bounds = UiHelper.PanelBottomButton(c, 1);

        _list.Update(input, ListBoundsOf(c), keyboardActive: isActive);

        // Drop button disables when the map is at the voluntary clutter cap. Use only PlayerDropped
        // items in the count — NPC loot piles don't block voluntary drops server-side either.
        _dropBtn.Enabled = _list.SelectedIndex >= 0 && PlayerDroppedCountOnCurrentMap(state) < Constants.MaxMapItems;

        // Use grays out while the beat is still running, so the button reads the way the action bar's
        // sweep does rather than silently sending something the server will drop.
        _useBtn.Enabled = UseReady(state);

        if (_useBtn.IsClicked(input) && UseReady(state))
            sender.SendUseItem(_list.SelectedIndex + 1);

        if (_dropBtn.IsClicked(input) && _list.SelectedIndex >= 0)
        {
            int slot = _list.SelectedIndex + 1;
            var inv = state.Me.Inv[slot];
            if (inv.Num > 0 && inv.Num <= state.Limits.Items)
                BeginDrop(slot, state, sender);
        }

        int rcSlot = _list.ConsumeRightClickedRow(input);
        if (rcSlot > 0) OpenDropContextMenu(rcSlot, input.MousePosition, state, sender);
    }

    private void BeginDrop(int invSlot, ClientState state, ClientPacketSender sender)
    {
        var inv = state.Me.Inv[invSlot];
        var item = state.Items[inv.Num];
        if (item?.Type == ItemType.Currency)
        {
            string itemName = item.Name?.TrimEnd() ?? $"Item {inv.Num}";
            int max = inv.Quantity;
            ConfirmDestroyThen(inv.Num, state, () => _prompt.Open(
                ClientStrings.Get(ClientStrings.InventoryPanel_DropItemLabel),
                itemName,
                max,
                amt => sender.SendMapDropItem(invSlot, amt)));
        }
        else
        {
            ConfirmDestroyThen(inv.Num, state, () => sender.SendMapDropItem(invSlot, 0));
        }
    }

    // Runs `drop` immediately, unless the item is DestroyOnDrop — then it WARNS first (dropping destroys it;
    // it never reaches the ground) and only runs `drop` on confirm.
    private void ConfirmDestroyThen(int itemNum, ClientState state, Action drop)
    {
        var item = state.Items[itemNum];
        if (item?.DestroyOnDrop == true)
        {
            string itemName = item.Name?.TrimEnd() ?? $"Item {itemNum}";
            _dropConfirm.Open(ClientStrings.Format(ClientStrings.InventoryPanel_DestroyDropWarn, ("Item", itemName)), drop);
        }
        else
        {
            drop();
        }
    }

    private void OpenDropContextMenu(int invSlot, Point mousePos, ClientState state, ClientPacketSender sender)
    {
        if (_cachedFont is null) return;
        var inv = state.Me?.Inv?[invSlot];
        if (inv is null || inv.Num <= 0 || inv.Num > state.Limits.Items) return;
        int itemNum = inv.Num;
        var item = state.Items[itemNum];
        if (item is null) return;

        bool hasRoom = PlayerDroppedCountOnCurrentMap(state) < Constants.MaxMapItems;
        bool isCurrency = item.Type == ItemType.Currency;
        string itemName = item.Name?.TrimEnd() ?? $"Item {itemNum}";

        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Drop1),
                () => ConfirmDestroyThen(itemNum, state, () => { if (isCurrency) sender.SendMapDropItem(invSlot, 1); else sender.SendMapDropBulk(itemNum, 1); }),
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_DropX),
                () => ConfirmDestroyThen(itemNum, state, () =>
                {
                    int max = isCurrency ? inv.Quantity : InventoryQuery.CountInvSlotsMatching(state, itemNum, skipEquipped: true);
                    if (max < 1) return;
                    _prompt.Open(
                        ClientStrings.Get(ClientStrings.InventoryPanel_DropItemLabel),
                        itemName,
                        max,
                        amt => { if (isCurrency) sender.SendMapDropItem(invSlot, amt); else sender.SendMapDropBulk(itemNum, amt); });
                }),
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_DropAll),
                () => ConfirmDestroyThen(itemNum, state, () => { if (isCurrency) sender.SendMapDropItem(invSlot, 0); else sender.SendMapDropBulk(itemNum, 0); }),
                hasRoom),
        };

        // Only where there is a bar to assign to. A world whose game declared none has nowhere to put
        // one, and the row would open a submenu with nothing in it.
        if (HotkeyAssignMenu.IsOffered(state))
        {
            items.Add(new ContextMenu.Item(ClientStrings.Get(ClientStrings.HotkeyBar_AssignSubmenu),
                HotkeyAssignMenu.ForItem(state, sender, itemNum)));
        }

        _contextMenu.Open(mousePos, itemName, items, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
    }

    private static int PlayerDroppedCountOnCurrentMap(ClientState state)
    {
        int count = 0;
        foreach (var mi in state.MapItems.Values)
            if (mi.Num > 0 && mi.Source == ItemSource.PlayerDropped) count++;
        return count;
    }

    public void Refresh(ClientState state) => InventoryListBuilder.BuildDisplayRows(state, _list);

    private static int ComputeStateHash(ClientState state)
    {
        var me = state.Me;
        if (me is null) return 0;
        var h = new HashCode();
        foreach (var (key, invSlot) in me.Equipped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            h.Add(key);
            h.Add(invSlot);
        }
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = me.Inv?[i];
            h.Add(slot?.Num ?? 0);
            h.Add(slot?.Quantity ?? 0);
            h.Add(slot?.Dur ?? 0);
        }
        return h.ToHashCode();
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, long nowMs, IReadOnlyList<Texture2D?> itemsTex, bool isActive = false, bool canHover = true)
    {
        if (!IsOpen) return;
        _cachedFont = font;
        int hash = ComputeStateHash(state);
        if (_stateDirty || hash != _stateHash)
        {
            _stateHash = hash;
            _stateDirty = false;
            Refresh(state);
        }

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _useBtn.Label = ClientStrings.Get(ClientStrings.InventoryPanel_UseItemButton);
            _dropBtn.Label = ClientStrings.Get(ClientStrings.InventoryPanel_DropItemButton);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.InventoryPanel_Title), isActive);

        var c = _panel.ContentBounds;
        SyncChrome(c, font);

        if (_view == InvView.Equipment)
        {
            DrawEquipment(sb, font, state, c, itemsTex);
            _backBtn.Draw(sb, font, _input);
            _panel.DrawOverlay(sb);
            _contextMenu.Draw(sb, font);   // the Unequip menu draws above the paper-doll
            if (canHover && !_contextMenu.IsOpen) NotifyHoverEquip(state, c, itemsTex);
            return;
        }

        // ── List view ──
        _sortLink.Draw(sb, font, _input);
        _equipLink.Draw(sb, font, _input);

        _useBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _dropBtn.Bounds = UiHelper.PanelBottomButton(c, 1);

        _list.Draw(sb, font, ListBoundsOf(c));
        float infoY = c.Bottom - 56;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", state.PlayerGold())), new Vector2(c.X + 8, infoY), Color.Gold, c.Width - 16);
        DrawSlotCount(sb, font, state, c, infoY);
        _useBtn.Draw(sb, font, _input);
        _dropBtn.Draw(sb, font, _input);
        _panel.DrawOverlay(sb);

        // Prompt draws on top of the panel; context menu draws on top of the prompt; the destroy-drop
        // confirm draws on top of everything.
        _prompt.Draw(sb, font, c, nowMs);
        _contextMenu.Draw(sb, font);
        _dropConfirm.Draw(sb, font, c);

        if (canHover && !_prompt.IsOpen && !_contextMenu.IsOpen && !_dropConfirm.IsOpen) NotifyHover(state, itemsTex);
    }

    private void NotifyHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _list.HoveredIndex;
        if (hovered < 0) return;
        int slotIdx = hovered + 1;
        var slot = state.Me?.Inv?[slotIdx];
        if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) return;
        var item = state.Items[slot.Num];
        if (item is null) return;
        var key = (TooltipScope, slotIdx, slot.Num);
        Tooltip.NotifyHoverItem(TooltipScope, key, item, slot, state.Me, itemsTex, _input.MousePosition,
            state.Items, state.Weather);
    }

    // Equipment-view counterpart to NotifyHover: shows the item tooltip for the equipped piece under
    // the mouse, keyed like the list so it re-pins when the hovered slot/item changes.
    private void NotifyHoverEquip(ClientState state, Rectangle c, IReadOnlyList<Texture2D?> itemsTex)
    {
        if (_cachedFont is null) return;
        if (EquipmentHitTest(state, c, _cachedFont, _input.MousePosition) is not { } hit) return;
        var slot = state.Me?.Inv?[hit.InvSlot];
        int num = slot?.Num ?? 0;
        var key = (TooltipScope, hit.InvSlot, num);
        Tooltip.NotifyHoverItem(TooltipScope, key, hit.Item, slot, state.Me, itemsTex, _input.MousePosition,
            state.Items, state.Weather);
    }

    // Keeps the [Sort]/[Equipment] links right-justified in the top strip and the equipment-view Back
    // button spanning the bottom row, in sync with panel bounds + locale. Called from Update (for
    // hit-testing) and Draw (for rendering); cheap and idempotent.
    private void SyncChrome(Rectangle c, SpriteFont font)
    {
        _sortLink.Label = ClientStrings.Get(ClientStrings.Common_SortHeader);
        _equipLink.Label = ClientStrings.Get(ClientStrings.Common_EquipmentHeader);
        const int rightPad = 6, gap = 10;
        int eqW = (int)Math.Ceiling(Link.MeasureSize(font, _equipLink.Label).X);
        int sortW = (int)Math.Ceiling(Link.MeasureSize(font, _sortLink.Label).X);
        _equipLink.Bounds = new Rectangle(c.Right - rightPad - eqW, c.Y, eqW, LinkStripH);
        _sortLink.Bounds = new Rectangle(c.Right - rightPad - eqW - gap - sortW, c.Y, sortW, LinkStripH);

        _backBtn.Label = ClientStrings.Get(ClientStrings.Common_Back);
        var b0 = UiHelper.PanelBottomButton(c, 0);
        var b1 = UiHelper.PanelBottomButton(c, 1);
        _backBtn.Bounds = new Rectangle(b0.X, b0.Y, b1.Right - b0.X, b0.Height);
    }

    private void OpenUnequipContextMenu(int invSlot, ItemRecord item, Point mousePos, ClientPacketSender sender)
    {
        if (_cachedFont is null) return;
        Tooltip.CloseScope(TooltipScope);   // hide the hover tooltip while the menu is up
        string itemName = item.Name?.TrimEnd() ?? $"Item {invSlot}";
        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Unequip), () => sender.SendUseItem(invSlot)),
        };
        _contextMenu.Open(mousePos, itemName, items, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
    }

    private static void DrawSlotCount(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c, float y)
    {
        int count = 0;
        var inv = state.Me?.Inv;
        if (inv is not null)
        {
            for (int i = 1; i <= Constants.MaxInv; i++)
            {
                var slot = inv[i];
                if (slot is not null && slot.Num > 0 && slot.Num <= state.Limits.Items) count++;
            }
        }
        float capacity = count / (float)Constants.MaxInv;
        Color color = capacity >= 0.9f ? Color.Red : capacity >= 0.5f ? Color.Yellow : Color.White;
        string text = $"{count} / {Constants.MaxInv}";
        float w = font.MeasureString(text).X;
        sb.DrawString(font, text, new Vector2(c.Right - 8 - w, y), color);
    }

    private static void DrawPotionLabel(SpriteBatch sb, SpriteFont font, string text, Color color, ref float x, float y)
    {
        sb.DrawString(font, text, new Vector2(x, y), color);
        x += font.MeasureString(text).X + 10;
    }

    private static Rectangle ListBoundsOf(Rectangle c) =>
        new(c.X + 4, c.Y + 2 + LinkStripH, c.Width - 8, Math.Max(0, c.Height - LinkStripH - 66));

    // ── Equipment paper-doll sub-view ────────────────────────────────────────────────────────────────────
    // One icon per slot the game declared, in its own order, wrapping to the panel width. Pure layout +
    // draw; this panel owns the hover tooltip + the right-click Unequip menu and calls EquipmentHitTest.
    private const int EqIconSize = 32;
    private const int EqTopPad = 8;
    private const int EqRowGap = 6;          // vertical gap between one row of icons and the next
    private const int EqColGap = 16;         // horizontal gap between icons in a row
    private const int EqSlotLabelLines = 2;  // per icon: the slot's own name, then the durability line

    private static readonly Color EqSlotBg = new(12, 26, 22, 235);
    private static readonly Color EqEmptyTextColor = new(104, 124, 116);

    // One declared slot and the icon rect it occupies. Computed identically for DrawEquipment and
    // EquipmentHitTest so the hover/click targets line up with what's drawn.
    private readonly record struct EquipCell(EquipSlot Slot, Rectangle Rect);

    // Rows are centered on the panel, so two slots read as a pair rather than hugging the left edge.
    private static List<EquipCell> EquipLayout(Rectangle c, SpriteFont font, IReadOnlyList<EquipSlot> slots)
    {
        int cellW = EqIconSize + EqColGap;
        int cellH = EqIconSize + EqSlotLabelLines * font.LineSpacing + EqRowGap;
        int perRow = Math.Max(1, (c.Width - 8 + EqColGap) / cellW);

        var cells = new List<EquipCell>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            int inThisRow = Math.Min(perRow, slots.Count - row * perRow);
            int rowWidth = inThisRow * cellW - EqColGap;
            cells.Add(new EquipCell(slots[i], new Rectangle(
                c.X + (c.Width - rowWidth) / 2 + col * cellW,
                c.Y + EqTopPad + row * cellH,
                EqIconSize, EqIconSize)));
        }
        return cells;
    }

    private static ItemRecord? EquippedItem(ClientState state, int invSlot)
    {
        var me = state.Me;
        if (invSlot <= 0 || me?.Inv is null) return null;
        int num = me.Inv[invSlot].Num;
        if (num <= 0 || num >= state.Items.Length) return null;
        return state.Items[num];
    }

    // Returns the worn piece (its inventory slot + item) whose icon contains `mouse`, or null. Empty
    // slots return null — no tooltip, no right-click.
    private (int InvSlot, ItemRecord Item)? EquipmentHitTest(ClientState state, Rectangle content, SpriteFont font, Point mouse)
    {
        var me = state.Me;
        if (me is null) return null;

        foreach (var cell in EquipLayout(content, font, state.EquipSlots.Slots))
        {
            if (!cell.Rect.Contains(mouse)) continue;
            int invSlot = me.EquippedIn(cell.Slot.Key);
            var item = EquippedItem(state, invSlot);
            if (item is not null) return (invSlot, item);
        }
        return null;
    }

    private void DrawEquipment(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle content, IReadOnlyList<Texture2D?> itemsTex)
    {
        var me = state.Me;
        if (me is null) return;

        // What is worn, and how worn it is. What a piece CONTRIBUTES is a formula a game owns, so there is
        // no bonus line — the caption is the slot's own name, which is the only thing Core can say about it.
        foreach (var cell in EquipLayout(content, font, state.EquipSlots.Slots))
        {
            int invSlot = me.EquippedIn(cell.Slot.Key);
            int dur = invSlot > 0 && me.Inv is not null ? me.Inv[invSlot].Dur : 0;
            EqDrawSlot(sb, font, itemsTex, cell.Rect, EquippedItem(state, invSlot),
                ClientStrings.GetOrFallback(cell.Slot.LabelKey, cell.Slot.Key), dur);
        }
    }

    private static void EqDrawSlot(SpriteBatch sb, SpriteFont font, IReadOnlyList<Texture2D?> itemsTex,
        Rectangle iconRect, ItemRecord? item, string caption, int dur)
    {
        UiHelper.DrawFilledRect(sb, iconRect, EqSlotBg);
        int centerX = iconRect.X + iconRect.Width / 2;
        int labelY = iconRect.Bottom + 2;
        if (item is null)
        {
            UiHelper.DrawBorder(sb, iconRect, UiHelper.DisabledColor);
            EqDrawCentered(sb, font, ClientStrings.Get(ClientStrings.Common_Empty), centerX, labelY, EqEmptyTextColor);
            return;
        }
        if (itemsTex is not null && item.Pic >= 0)
            sb.DrawItemIcon(itemsTex, item, iconRect, Color.White);
        UiHelper.DrawBorder(sb, iconRect, UiHelper.UiControlBorder);

        EqDrawCentered(sb, font, caption, centerX, labelY, Color.White);
        int nextY = labelY + font.LineSpacing;

        // Condition line beneath the bonus line — cur/max wear, color-coded (white/yellow/red) exactly like
        // the tooltip and repair panel. Items with no durability budget skip it.
        int maxDur = item.Durability;
        if (maxDur > 0)
            EqDrawCentered(sb, font, $"{dur}/{maxDur}", centerX, nextY, UiHelper.DurabilityColor(dur, maxDur));
    }

    private static void EqDrawCentered(SpriteBatch sb, SpriteFont font, string text, int centerX, int y, Color color)
    {
        float w = font.MeasureString(text).X;
        sb.DrawString(font, text, new Vector2(centerX - w / 2f, y), color);
    }
}
