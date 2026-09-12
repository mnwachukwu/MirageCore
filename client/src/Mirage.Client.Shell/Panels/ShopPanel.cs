using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>The shop panel: buy for gold, barter item-for-item, sell, and the repair (fix-item) flow.</summary>
public sealed class ShopPanel : IGamePanel
{
    // The list view fits in very little, but the acquisition confirm is the tallest thing this panel
    // draws and it grew: a spell scroll stacks level, what it teaches, MP cost, reagent cost, how often
    // the reagent depletes, an effectiveness preview, then the quantity and total rows. That is ten
    // 18px lines under the name-and-icon header and above the buttons, and a minimum that predates them
    // lets the panel be dragged down to where the confirm is cut off mid-sentence.
    private const int ConfirmLines = 10;
    private const int ConfirmChrome = 118;   // title bar, the icon/name header, the button row, padding
    private const int PanelMinH = ConfirmLines * 18 + ConfirmChrome;   // 298

    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 360, 320), minH: PanelMinH);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    private const string TooltipScope = "ShopFix";

    /// <summary>Open on a shop. Every list starts at the top with nothing selected, and the tab strip starts on
    /// Buy: this is a DIFFERENT keeper's stock, so a position carried over from the last one lands the player
    /// partway down a list they have not seen the start of, and an armed selection index points at whatever
    /// item now happens to occupy that row.</summary>
    public void Open()
    {
        IsOpen = true;
        _barterDirty = true;
        _fixSlotDirty = true;
        _salesDirty = true;
        _sellDirty = true;
        _tab = Tab.Buy;
        _viewState = ViewState.None;
        _prompt.Close();
        _salesList.Reset();
        _sellList.Reset();
        _barterList.Reset();
        _fixSlotList.Reset();
    }
    public void Close()
    {
        IsOpen = false;
        _viewState = ViewState.None;
        _prompt.Close();
        Tooltip.CloseScope(TooltipScope);
    }
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (!IsOpen) Tooltip.CloseScope(TooltipScope);
    }

    // True while a confirm sub-view is showing — lets GameplayScreen suppress Escape-closes-panel.
    public bool IsCapturingInput =>
        _prompt.IsOpen
        || _viewState is ViewState.ConfirmingRepair or ViewState.ConfirmingBarter
                      or ViewState.ConfirmingBuy or ViewState.ConfirmingSell;

    /// <summary>How many to buy, sell or trade for. Anything can be dealt in by the handful — a caster stocks
    /// reagents in dozens, an outfitter clears five identical helmets, a forager cashes in a pocket of teeth —
    /// so this opens whenever more than one is actually available. When only one is, there is nothing to
    /// choose between and the plain confirm card takes over instead.
    /// <para>Its cap is the client's copy of the server's own limits, so it never offers an amount that would
    /// come back refused: what the purse covers AND what the bag can hold for a purchase, the identical-copy
    /// group for a sale, and what can be both paid and received for a barter row.</para></summary>
    private readonly NumberPromptDialog _prompt = new();

    // ── The three storefronts ─────────────────────────────────────────────────
    // BUY is the gold shopfront (ShopRecord.SalesItem, priced from ItemRecord.Price), TRADE is the
    // barter table (give → get rows), SELL is the player's own bag. They are separate tabs rather
    // than one list because they are separate transactions — see ShopContentsPacket's note on why the
    // sales list is item numbers while a barter row names both sides.
    // Declaration order IS the on-screen order — DrawTabs indexes its label array by (Tab)i. The two gold
    // transactions sit together, with item-for-item barter last.
    private enum Tab { Buy, Sell, Barter }
    private Tab _tab = Tab.Buy;
    private const int TabStripH = 24;

    // Buy tab
    private readonly ListBox _salesList = new() { ShowTruncationTooltip = false };
    private readonly Button _buyBtn = new();
    private readonly Button _buyConfirmBtn = new();
    private readonly Button _buyCancelBtn = new();
    private int _pendingBuySlot;
    private int _salesHash;
    private bool _salesDirty = true;

    // Sell tab — maps list index → inventory slot, exactly as the repair picker does.
    private readonly ListBox _sellList = new() { ShowTruncationTooltip = false };
    private readonly Button _sellBtn = new();
    private readonly Button _sellConfirmBtn = new();
    private readonly Button _sellCancelBtn = new();
    private readonly List<int> _sellSlotNums = new();
    private int _pendingSellSlot;
    private int _sellHash;
    private bool _sellDirty = true;

    // Normal barter mode
    private readonly ListBox _barterList = new();
    private readonly Button _barterBtn = new();
    private readonly Button _fixBtn = new();

    // Fix-item slot selection mode: the player picks which inventory slot to repair.
    private readonly ListBox _fixSlotList = new() { ShowTruncationTooltip = false };   // rows show the richer item tooltip
    private readonly Button _fixConfirmBtn = new();
    private readonly Button _fixCancelBtn = new();

    // Repair confirm mode
    private readonly Button _repairConfirmBtn = new();
    private readonly Button _repairCancelBtn = new();

    // Barter confirm mode
    private readonly Button _barterConfirmBtn = new();
    private readonly Button _barterCancelBtn = new();
    private int _labelsGeneration = -1;

    private enum ViewState { None, SelectingSlot, ConfirmingRepair, ConfirmingBarter, ConfirmingBuy, ConfirmingSell }
    private ViewState _viewState;
    private int _pendingFixSlot;
    private int _pendingBarterSlot;

    private int _barterHash;
    private bool _barterDirty = true;
    private int _fixSlotHash;
    private bool _fixSlotDirty = true;
    private readonly List<int> _fixSlotNums = new(); // maps list index → inventory slot number

    private InputState _input = new();

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            state.ActiveShopNum = 0;
            _viewState = ViewState.None;
            Tooltip.CloseScope(TooltipScope);
            return;
        }

        var c = _panel.ContentBounds;

        if (_prompt.IsOpen)
        {
            _prompt.Update(input, c, System.Environment.TickCount64);
            return;
        }

        if (_viewState == ViewState.SelectingSlot)
        {
            SetFixButtonBounds(c);
            _fixSlotList.Update(input, ListBoundsOf(c));

            if (_fixConfirmBtn.IsClicked(input) && _fixSlotList.SelectedIndex >= 0
                && _fixSlotList.SelectedIndex < _fixSlotNums.Count)
            {
                int slot = _fixSlotNums[_fixSlotList.SelectedIndex];
                var inv = state.Me?.Inv?[slot];
                if (inv is not null && inv.Num > 0 && inv.Num <= state.Limits.Items)
                {
                    _pendingFixSlot = slot;
                    _viewState = ViewState.ConfirmingRepair;
                }
            }
            if (_fixCancelBtn.IsClicked(input))
                _viewState = ViewState.None;
            return;
        }

        if (_viewState == ViewState.ConfirmingRepair)
        {
            SetRepairConfirmButtonBounds(c);

            if (_repairConfirmBtn.IsClicked(input))
            {
                sender.SendFixItem(_pendingFixSlot);
                _viewState = ViewState.SelectingSlot;
            }
            if (_repairCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
            {
                input.ConsumeKey(Keys.Escape);
                _viewState = ViewState.SelectingSlot;
            }
            return;
        }

        if (_viewState == ViewState.ConfirmingBarter)
        {
            SetBarterConfirmButtonBounds(c);

            if (_barterConfirmBtn.IsClicked(input))
            {
                sender.SendShopBarter(state.ActiveShopNum, _pendingBarterSlot);
                _viewState = ViewState.None;
            }
            if (_barterCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
            {
                input.ConsumeKey(Keys.Escape);
                _viewState = ViewState.None;
            }
            return;
        }

        if (_viewState == ViewState.ConfirmingBuy)
        {
            SetBuyConfirmButtonBounds(c);
            if (_buyConfirmBtn.IsClicked(input))
            {
                sender.SendShopBuy(state.ActiveShopNum, _pendingBuySlot);
                _viewState = ViewState.None;
            }
            if (_buyCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
            {
                input.ConsumeKey(Keys.Escape);
                _viewState = ViewState.None;
            }
            return;
        }

        if (_viewState == ViewState.ConfirmingSell)
        {
            SetSellConfirmButtonBounds(c);
            if (_sellConfirmBtn.IsClicked(input))
            {
                // Quantity 0 = the whole stack. The server prices it; nothing here proposes a value.
                sender.SendShopSell(_pendingSellSlot, 0);
                _viewState = ViewState.None;
                _sellDirty = true;
            }
            if (_sellCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
            {
                input.ConsumeKey(Keys.Escape);
                _viewState = ViewState.None;
            }
            return;
        }

        UpdateTabs(input, c);
        SetButtonBounds(c);

        var shop = state.ActiveShopNum > 0 ? state.ShopDefs[state.ActiveShopNum] : null;

        switch (_tab)
        {
            case Tab.Buy:
                _salesList.Update(input, TabbedListBoundsOf(c));
                _buyBtn.Enabled = _salesList.SelectedIndex >= 0 && _salesList.SelectedIndex < state.ActiveSales.Length;
                if (_buyBtn.IsClicked(input) && _buyBtn.Enabled)
                {
                    _pendingBuySlot = _salesList.SelectedIndex + 1;   // 1-based on the wire
                    if (!BeginBulkBuy(state, sender)) _viewState = ViewState.ConfirmingBuy;
                }
                break;

            case Tab.Barter:
                _barterList.Update(input, TabbedListBoundsOf(c));
                if (_barterBtn.IsClicked(input) && _barterList.SelectedIndex >= 0)
                {
                    _pendingBarterSlot = _barterList.SelectedIndex + 1;
                    if (!BeginBulkBarter(state, sender)) _viewState = ViewState.ConfirmingBarter;
                }
                break;

            case Tab.Sell:
                _sellList.Update(input, TabbedListBoundsOf(c));
                _sellBtn.Enabled = _sellList.SelectedIndex >= 0 && _sellList.SelectedIndex < _sellSlotNums.Count;
                if (_sellBtn.IsClicked(input) && _sellBtn.Enabled)
                {
                    _pendingSellSlot = _sellSlotNums[_sellList.SelectedIndex];
                    if (!BeginBulkSell(state, sender)) _viewState = ViewState.ConfirmingSell;
                }
                break;
        }

        _fixBtn.Enabled = shop?.FixesItems ?? false;
        if (_fixBtn.IsClicked(input))
        {
            _viewState = ViewState.SelectingSlot;
            _fixSlotList.SelectedIndex = 0;
        }
    }

    // Tab strip across the top of the content area. Switching tabs clears the selection so a stale
    // index from one list can never be read against another — the lists are different lengths and
    // mean different things.
    private void UpdateTabs(InputState input, Rectangle c)
    {
        if (!input.IsMouseClicked()) return;
        var rects = TabRects(c);
        for (int i = 0; i < rects.Length; i++)
        {
            if (!input.IsClickIn(rects[i])) continue;
            var picked = (Tab)i;
            if (picked != _tab)
            {
                _tab = picked;
                _salesList.SelectedIndex = -1;
                _barterList.SelectedIndex = -1;
                _sellList.SelectedIndex = -1;
            }
            input.ConsumeMouseClick();
            break;
        }
    }

    private static Rectangle[] TabRects(Rectangle c)
    {
        const int count = 3, gap = 2;
        int w = (c.Width - gap * (count - 1)) / count;
        var rects = new Rectangle[count];
        for (int i = 0; i < count; i++)
            rects[i] = new Rectangle(c.X + i * (w + gap), c.Y + 2, w, TabStripH - 4);
        return rects;
    }

    private static int ComputeBarterHash(ClientState state)
    {
        var h = new HashCode();
        h.Add(state.ActiveShopNum);
        foreach (var row in state.ActiveBarters)
        {
            h.Add(row.GiveItem);
            h.Add(row.GiveQuantity);
            h.Add(row.GetItem);
            h.Add(row.GetQuantity);
        }
        return h.ToHashCode();
    }

    private static int ComputeFixSlotHash(ClientState state)
    {
        var me = state.Me;
        if (me is null) return 0;
        var h = new HashCode();
        h.Add(me.WeaponSlot);
        h.Add(me.ArmorSlot);
        h.Add(me.HelmetSlot);
        h.Add(me.ShieldSlot);
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = me.Inv?[i];
            h.Add(slot?.Num ?? 0);
            h.Add(slot?.Dur ?? 0);
        }
        return h.ToHashCode();
    }

    public void Refresh(ClientState state)
    {
        _barterList.Items.Clear();

        foreach (var row in state.ActiveBarters)
        {
            string give = row.GiveItem > 0 && row.GiveItem <= state.Items.Length - 1
                ? $"{state.Items[row.GiveItem]?.Name ?? "?"} x{row.GiveQuantity}"
                : "?";
            string get = row.GetItem > 0 && row.GetItem <= state.Items.Length - 1
                ? $"{state.Items[row.GetItem]?.Name ?? "?"} x{row.GetQuantity}"
                : "?";
            _barterList.Items.Add($"{give} -> {get}");
        }
    }

    /// <summary>The gold shopfront. Price comes from the item definition the client already holds —
    /// the wire only carried numbers — so a shop of any size costs one int per entry.</summary>
    private void RefreshSales(ClientState state)
    {
        _salesList.Items.Clear();
        foreach (int num in state.ActiveSales)
        {
            var item = num > 0 && num <= state.Limits.Items ? state.Items[num] : null;
            string name = item?.Name?.Trim() ?? "?";
            _salesList.Items.Add(item is null
                ? name
                : ClientStrings.Format(ClientStrings.ShopPanel_SalesRow, ("Item", name), ("Gold", item.Price)));
        }
    }

    /// <summary>What the player can sell, and for how much. Mirrors the server's Sell rules exactly —
    /// equipped gear and NonJunkable items are excluded rather than listed and then refused, because a
    /// row you can click and be told no about is worse than a row that is not there.</summary>
    private void RefreshSellSlots(ClientState state)
    {
        _sellList.Items.Clear();
        _sellSlotNums.Clear();
        var me = state.Me;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = me?.Inv?[i];
            if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) continue;
            var item = state.Items[slot.Num];
            if (item is null || item.NonJunkable) continue;
            if (me is not null && (me.WeaponSlot == i || me.ArmorSlot == i || me.HelmetSlot == i || me.ShieldSlot == i))
                continue;

            int offer = EconomyFormulas.ItemSellValue(item, slot.Dur);
            if (item.Type == ItemType.Currency) offer *= Math.Max(slot.Quantity, 1);

            string name = item.Name?.Trim() ?? "?";
            if (item.Type == ItemType.Currency) name = $"{name} x{Math.Max(slot.Quantity, 1)}";
            _sellList.Items.Add(ClientStrings.Format(ClientStrings.ShopPanel_SellRow, ("Item", name), ("Gold", offer)));
            _sellSlotNums.Add(i);
        }
    }

    private static int ComputeSalesHash(ClientState state)
    {
        var h = new HashCode();
        h.Add(state.ActiveShopNum);
        foreach (int num in state.ActiveSales) h.Add(num);
        return h.ToHashCode();
    }

    private void RefreshFixSlots(ClientState state)
    {
        _fixSlotList.Items.Clear();
        _fixSlotNums.Clear();
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = state.Me?.Inv?[i];
            if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) continue;
            var item = state.Items[slot.Num];
            if (item is null) continue;
            if (item.Type is not (ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield))
                continue;
            bool equipped = state.Me != null &&
                (state.Me.WeaponSlot == i || state.Me.ArmorSlot == i ||
                 state.Me.HelmetSlot == i || state.Me.ShieldSlot == i);
            bool broken = !equipped && item.Durability > 0 && slot.Dur <= 0;
            string name = item.Name?.Trim() ?? "?";
            // No slot index prefix here — the inventory position is irrelevant when picking an item to repair
            // (selection maps through _fixSlotNums). Surface Equipped / Broken so a broken piece is obvious.
            _fixSlotList.Items.Add(equipped
                ? $"{name} {ClientStrings.Get(ClientStrings.Common_Equipped)}"
                : broken
                    ? $"{name} {ClientStrings.Get(ClientStrings.Common_Broken)}"
                    : name);
            _fixSlotNums.Add(i);
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex, bool isActive = false, bool canHover = true)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _barterBtn.Label = ClientStrings.Get(ClientStrings.ShopPanel_TradeButton);
            _buyBtn.Label = ClientStrings.Get(ClientStrings.ShopPanel_BuyButton);
            _sellBtn.Label = ClientStrings.Get(ClientStrings.ShopPanel_SellButton);
            _buyConfirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _buyCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _sellConfirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _sellCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _fixBtn.Label = ClientStrings.Get(ClientStrings.ShopPanel_FixItemButton);
            _fixConfirmBtn.Label = ClientStrings.Get(ClientStrings.ShopPanel_FixButton);
            _fixCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _repairConfirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _repairCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _barterConfirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _barterCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }

        var shop = state.ActiveShopNum > 0 ? state.ShopDefs[state.ActiveShopNum] : null;
        string title = shop?.Name ?? "Shop";

        if (_viewState == ViewState.ConfirmingRepair)
        {
            _panel.Draw(sb, font, title, isActive);
            var c2 = _panel.ContentBounds;
            SetRepairConfirmButtonBounds(c2);
            DrawRepairConfirm(sb, font, state, c2, itemsTex);
            _panel.DrawOverlay(sb);
            return;
        }

        if (_viewState == ViewState.ConfirmingBarter)
        {
            _panel.Draw(sb, font, title, isActive);
            var c2 = _panel.ContentBounds;
            SetBarterConfirmButtonBounds(c2);
            DrawBarterConfirm(sb, font, state, c2, itemsTex, state.ActiveBarters[_pendingBarterSlot - 1],
                _barterConfirmBtn, _barterCancelBtn);
            _panel.DrawOverlay(sb);
            return;
        }

        if (_viewState == ViewState.SelectingSlot)
        {
            int fixHash = ComputeFixSlotHash(state);
            if (_fixSlotDirty || fixHash != _fixSlotHash)
            {
                _fixSlotHash = fixHash;
                _fixSlotDirty = false;
                RefreshFixSlots(state);
            }
            _panel.Draw(sb, font, title, isActive);
            var c2 = _panel.ContentBounds;
            SetFixButtonBounds(c2);
            _fixSlotList.Draw(sb, font, ListBoundsOf(c2));
            _fixConfirmBtn.Draw(sb, font, _input);
            _fixCancelBtn.Draw(sb, font, _input);
            _panel.DrawOverlay(sb);
            if (canHover) NotifyFixSlotHover(state, itemsTex);
            return;
        }

        if (_viewState == ViewState.ConfirmingBuy)
        {
            _panel.Draw(sb, font, title, isActive);
            var c2 = _panel.ContentBounds;
            SetBuyConfirmButtonBounds(c2);
            DrawBuyConfirm(sb, font, state, c2, itemsTex);
            _panel.DrawOverlay(sb);
            return;
        }

        if (_viewState == ViewState.ConfirmingSell)
        {
            _panel.Draw(sb, font, title, isActive);
            var c2 = _panel.ContentBounds;
            SetSellConfirmButtonBounds(c2);
            DrawSellConfirm(sb, font, state, c2, itemsTex);
            _panel.DrawOverlay(sb);
            return;
        }

        int barterHash = ComputeBarterHash(state);
        if (_barterDirty || barterHash != _barterHash)
        {
            _barterHash = barterHash;
            _barterDirty = false;
            Refresh(state);
        }
        int salesHash = ComputeSalesHash(state);
        if (_salesDirty || salesHash != _salesHash)
        {
            _salesHash = salesHash;
            _salesDirty = false;
            RefreshSales(state);
        }
        // The sell list reuses the repair picker's hash: both are "what is in the bag, and how worn",
        // which is exactly what changes a sell offer.
        int sellHash = ComputeFixSlotHash(state);
        if (_sellDirty || sellHash != _sellHash)
        {
            _sellHash = sellHash;
            _sellDirty = false;
            RefreshSellSlots(state);
        }
        _panel.Draw(sb, font, title, isActive);

        var c = _panel.ContentBounds;
        SetButtonBounds(c);
        DrawTabs(sb, font, c);

        switch (_tab)
        {
            case Tab.Buy:
                _salesList.Draw(sb, font, TabbedListBoundsOf(c));
                _buyBtn.Draw(sb, font, _input);
                break;
            case Tab.Barter:
                _barterList.Draw(sb, font, TabbedListBoundsOf(c));
                _barterBtn.Draw(sb, font, _input);
                break;
            case Tab.Sell:
                _sellList.Draw(sb, font, TabbedListBoundsOf(c));
                _sellBtn.Draw(sb, font, _input);
                break;
        }
        _fixBtn.Draw(sb, font, _input);

        long gold = state.PlayerGold();
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", gold)), new Vector2(c.X + 8, c.Bottom - 56), Color.Gold, c.Width - 16);
        _panel.DrawOverlay(sb);
        _prompt.Draw(sb, font, c, System.Environment.TickCount64);
        if (canHover && !_prompt.IsOpen) NotifyTabHover(state, itemsTex);
    }

    // ── Buying and selling by the handful ─────────────────────────────────────
    // Only a currency stack has an amount worth asking about. Both return false for everything else, which
    // sends the caller to the confirm card — the one that shows the level, stat and class gates a piece of
    // equipment has to clear, none of which a stack of reagents does.

    /// <summary>Asks how many of a stackable sales entry to buy, capped at what the purse covers. False when
    /// the entry is not a stack, or when a single one is already unaffordable.</summary>
    private bool BeginBulkBuy(ClientState state, ClientPacketSender sender)
    {
        int itemNum = _pendingBuySlot >= 1 && _pendingBuySlot <= state.ActiveSales.Length
            ? state.ActiveSales[_pendingBuySlot - 1] : 0;
        var item = itemNum > 0 && itemNum <= state.Limits.Items ? state.Items[itemNum] : null;
        if (item is null || item.Price <= 0) return false;

        // Capped by BOTH limits, because the server treats them differently: it would clamp an
        // unaffordable ask down but REFUSE one the bag cannot hold, so offering more than fits would
        // hand the player a button that only ever fails.
        int affordable = (int)Math.Min(int.MaxValue, state.PlayerGold() / item.Price);
        int max = Math.Min(affordable, InventoryQuery.RoomFor(state, itemNum));
        if (affordable <= 0) return false;   // the confirm card says why, in its own words
        if (max <= 1) return false;          // one at a time is the plain confirm, not a prompt

        int slot = _pendingBuySlot;
        // Refused rather than reduced: asking for fifty with room for thirty-three is a question about
        // fifty, and quietly buying thirty-three answers a question nobody asked.
        _prompt.Open(
            ClientStrings.Get(ClientStrings.ShopPanel_BuyHowMany),
            EachLine(item.Name, item.Price),
            max,
            amount => sender.SendShopBuy(state.ActiveShopNum, slot, amount),
            rejectOverMax: true);
        return true;
    }

    /// <summary>Asks how many helpings of a barter row to take. A row is a RATE: five teeth against a
    /// two-teeth row is two helpings with one left over. Capped by what the bag can both PAY and RECEIVE,
    /// since the server refuses a payout that would not fit rather than trimming it.</summary>
    private bool BeginBulkBarter(ClientState state, ClientPacketSender sender)
    {
        if (_pendingBarterSlot < 1 || _pendingBarterSlot > state.ActiveBarters.Length) return false;
        var row = state.ActiveBarters[_pendingBarterSlot - 1];
        if (row.GiveItem <= 0 || row.GetItem <= 0 || row.GiveQuantity <= 0 || row.GetQuantity <= 0) return false;

        int canPay = InventoryQuery.HeldCount(state, row.GiveItem) / row.GiveQuantity;

        // The payment is part of the room: a helping frees GiveQuantity slots and fills GetQuantity, so only
        // the DIFFERENCE has to be found up front. A row that gives back no more than it takes always fits,
        // however full the bag is — which is the same arithmetic the server does before refusing.
        int netPerHelping = row.GetQuantity - row.GiveQuantity;
        int canHold = netPerHelping <= 0
            ? canPay
            : InventoryQuery.RoomFor(state, row.GetItem) / netPerHelping;
        int max = Math.Min(canPay, canHold);
        if (max <= 1) return false;   // a single helping is the plain confirm card

        int slot = _pendingBarterSlot;
        var give = row.GiveItem <= state.Limits.Items ? state.Items[row.GiveItem] : null;
        _prompt.Open(
            ClientStrings.Get(ClientStrings.ShopPanel_BarterHowMany),
            ClientStrings.Format(ClientStrings.ShopPanel_PerHelping,
                ("Give", $"{give?.Name?.TrimEnd() ?? "?"} x{row.GiveQuantity}"),
                ("Get", $"{(row.GetItem <= state.Limits.Items ? state.Items[row.GetItem]?.Name?.TrimEnd() : null) ?? "?"} x{row.GetQuantity}")),
            max,
            amount => sender.SendShopBarter(state.ActiveShopNum, slot, amount),
            rejectOverMax: true);
        return true;
    }

    /// <summary>Asks how much of a stackable bag slot to sell, capped at the stack. False for anything
    /// else.</summary>
    private bool BeginBulkSell(ClientState state, ClientPacketSender sender)
    {
        var inv = state.Me?.Inv?[_pendingSellSlot];
        var item = inv is not null && inv.Num > 0 && inv.Num <= state.Limits.Items ? state.Items[inv.Num] : null;
        if (item is null) return false;

        // A stack sells by amount; everything else sells by the slot, and only alongside copies that are
        // INDISTINGUISHABLE from this one — same item, same durability. That is the rule the shop applies,
        // and it is what lets worn gear be sold in bulk at all without a pristine piece going with the
        // battered ones. Every copy in the group prices the same, so one "each" line describes them all.
        int held = item.Type == ItemType.Currency
            ? Math.Max(inv!.Quantity, 1)
            : InventoryQuery.IdenticalSaleableCount(state, _pendingSellSlot);
        if (held <= 1) return false;   // nothing to choose between; the plain confirm card says it

        int slot = _pendingSellSlot;
        // No spell to price against: a scroll is its own item type, never one of these groups.
        _prompt.Open(
            ClientStrings.Get(ClientStrings.ShopPanel_SellHowMany),
            EachLine(item.Name, EconomyFormulas.ItemSellValue(item, inv!.Dur)),
            held,
            amount => { sender.SendShopSell(slot, amount); _sellDirty = true; });
        return true;
    }

    private static string EachLine(string? name, int gold) =>
        ClientStrings.Format(ClientStrings.ShopPanel_EachPrice,
            ("Item", name?.TrimEnd() ?? "?"), ("Gold", gold));

    private void NotifyFixSlotHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _fixSlotList.HoveredIndex;
        if (hovered < 0 || hovered >= _fixSlotNums.Count) return;
        NotifyInvSlotHover(state, itemsTex, _fixSlotNums[hovered], "fix");
    }

    // Tooltips for the tab that is showing. Barter rows name two items at once, so a single-item tooltip
    // would be ambiguous there; that list keeps its truncation tooltip instead.
    private void NotifyTabHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        switch (_tab)
        {
            case Tab.Buy: NotifySalesHover(state, itemsTex); break;
            case Tab.Sell: NotifySellSlotHover(state, itemsTex); break;
        }
    }

    /// <summary>A shopfront row is an item the player does not own yet, so it is described by a stand-in bag
    /// slot at full durability — the same shape the character-create kit uses. Quantity is 1 because that is
    /// what a purchase grants (see DrawBuyConfirm's barter row).</summary>
    private void NotifySalesHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _salesList.HoveredIndex;
        if (hovered < 0 || hovered >= state.ActiveSales.Length) return;
        int itemNum = state.ActiveSales[hovered];
        if (itemNum <= 0 || itemNum > state.Limits.Items) return;
        var item = state.Items[itemNum];
        if (item is null) return;
        var slot = new PlayerInvSlot { Num = itemNum, Quantity = 1, Dur = item.Durability };
        Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, "buy", itemNum), item, slot,
            state.Me, itemsTex, _input.MousePosition,
            state.Items, state.Weather);
    }

    private void NotifySellSlotHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _sellList.HoveredIndex;
        if (hovered < 0 || hovered >= _sellSlotNums.Count) return;
        NotifyInvSlotHover(state, itemsTex, _sellSlotNums[hovered], "sell");
    }

    // A row backed by a real bag slot, so the tooltip shows its true wear and stack. Key on
    // (panel, list, slotIdx, itemNum) so it re-pins when the user moves to a different slot, when the
    // slot's item changes underneath them, or when the same slot is reached from a different list.
    private void NotifyInvSlotHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex, int slotIdx, string list)
    {
        var slot = state.Me?.Inv?[slotIdx];
        if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) return;
        var item = state.Items[slot.Num];
        if (item is null) return;
        Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, list, slotIdx, slot.Num), item, slot,
            state.Me, itemsTex, _input.MousePosition,
            state.Items, state.Weather);
    }

    private void DrawRepairConfirm(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c, IReadOnlyList<Texture2D?> itemsTex)
    {
        var inv = state.Me.Inv[_pendingFixSlot];
        var item = inv.Num > 0 && inv.Num <= state.Limits.Items ? state.Items[inv.Num] : null;
        string name = item?.Name?.Trim() ?? $"Item {inv.Num}";

        var bgRect = new Rectangle(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        float textY = c.Y + 12;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.ShopPanel_RepairItemLabel), new Vector2(c.X + 8, textY), Color.LightGray, c.Width - 16);
        textY += 18;
        UiHelper.DrawLabel(sb, font, name, new Vector2(c.X + 8, textY), Color.White, c.Width - 16);
        textY += 18;
        textY = DrawItemPreview(sb, c, itemsTex, item?.Pic ?? -1, item?.ItemSheet ?? 0, textY);

        int maxDur = item?.Durability ?? 0;
        int durNeeded = maxDur - inv.Dur;
        // Quote through the SHIPPED formula, never a local approximation: a copy goes stale the next
        // time repair is retuned, and the symptom is a panel quoting one price while the counter charges
        // another. EconomyFormulas is in Mirage.Shared precisely so both ends agree without a round-trip.
        int goldNeeded = item is not null ? EconomyFormulas.RepairCost(durNeeded, item) : 0;
        long playerGold = state.PlayerGold();

        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_DurabilityLabel, ("Current", inv.Dur), ("Max", maxDur)), new Vector2(c.X + 8, textY), UiHelper.DurabilityColor(inv.Dur, maxDur), c.Width - 16);
        textY += 18;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", playerGold)), new Vector2(c.X + 8, textY), Color.Gold, c.Width - 16);
        textY += 20;

        if (durNeeded <= 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.ShopPanel_PerfectCondition), new Vector2(c.X + 8, textY), Color.LightGreen, c.Width - 16);
        }
        else if (playerGold >= goldNeeded)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_FullRepairCost, ("Gold", goldNeeded)), new Vector2(c.X + 8, textY), Color.Cyan, c.Width - 16);
        }
        else if (item is not null && EconomyFormulas.RepairPointsAffordable(playerGold, item) > 0)
        {
            // Ask the formula how many points the purse covers rather than dividing by a display
            // rate — the server does exactly this, and dividing can name a point count that costs
            // a gold more than the player actually has.
            int durPartial = Math.Min(EconomyFormulas.RepairPointsAffordable(playerGold, item), durNeeded);
            int goldActual = EconomyFormulas.RepairCost(durPartial, item);
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_PartialRepairCost, ("Gold", goldActual)), new Vector2(c.X + 8, textY), Color.Yellow, c.Width - 16);
            textY += 18;
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_DurabilityGain, ("Amount", durPartial)), new Vector2(c.X + 8, textY), Color.LightGray, c.Width - 16);
        }
        else
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.ShopPanel_InsufficientGold), new Vector2(c.X + 8, textY), Color.OrangeRed, c.Width - 16);
        }

        _repairConfirmBtn.Draw(sb, font, _input);
        _repairCancelBtn.Draw(sb, font, _input);
    }

    /// <summary>The acquisition confirm, shared by barter and by buying.
    ///
    /// <para>A PURCHASE IS A TRADE ROW: give N gold, get one item. Expressing it that way rather than
    /// writing a second confirm screen is what keeps the two in step — the spell-taught line, the potion
    /// effect, the durability readout, the stat and INT requirements and the "you already know this
    /// spell" warning are all things a buyer needs exactly as much as a barterer, and there is now one
    /// copy of them.</para></summary>
    private void DrawBarterConfirm(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c,
        IReadOnlyList<Texture2D?> itemsTex, ShopContentsPacket.BarterRow row, Button confirmBtn, Button cancelBtn)
    {
        var get = row.GetItem > 0 && row.GetItem <= state.Limits.Items ? state.Items[row.GetItem] : null;
        var give = row.GiveItem > 0 && row.GiveItem <= state.Limits.Items ? state.Items[row.GiveItem] : null;
        var me = state.Me;

        var bgRect = new Rectangle(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        string name = get?.Name?.Trim() ?? "?";
        string giveName = give?.Name?.Trim() ?? "?";

        bool isEquip = get is not null && ItemRecord.IsEquipment(get.Type);

        string nameLine = get?.Type == ItemType.Currency ? $"{row.GetQuantity} {name}" : name;

        float textY = c.Y + 12;
        UiHelper.DrawLabel(sb, font, nameLine, new Vector2(c.X + 8, textY), Color.White, c.Width - 16);
        textY += 18;
        textY = DrawItemPreview(sb, c, itemsTex, get?.Pic ?? -1, get?.ItemSheet ?? 0, textY);
        {
            textY += 18;
        }
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_TradeCost, ("Amount", row.GiveQuantity), ("Item", giveName)), new Vector2(c.X + 8, textY), Color.Yellow, c.Width - 16);
        textY += 18;

        if (isEquip)
        {
            // A shop item is pristine (Current == Max), so this reads white under the condition coding —
            // the same "healthy" signal the equipment panel, tooltip, and repair panel use.
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_DurabilityLabel, ("Current", get!.Durability), ("Max", get.Durability)), new Vector2(c.X + 8, textY), UiHelper.DurabilityColor(get.Durability, get.Durability), c.Width - 16);
            textY += 18;
        }


        textY += 4;

        confirmBtn.Draw(sb, font, _input);
        cancelBtn.Draw(sb, font, _input);
    }

    /// <summary>Buying renders through the shared acquisition confirm by describing the purchase as the
    /// barter row it actually is — gold in, item out.</summary>
    private void DrawBuyConfirm(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c, IReadOnlyList<Texture2D?> itemsTex)
    {
        int itemNum = _pendingBuySlot >= 1 && _pendingBuySlot <= state.ActiveSales.Length
            ? state.ActiveSales[_pendingBuySlot - 1] : 0;
        var item = itemNum > 0 && itemNum <= state.Limits.Items ? state.Items[itemNum] : null;
        var row = new ShopContentsPacket.BarterRow(Constants.GoldItemIndex, item?.Price ?? 0, itemNum, 1);
        DrawBarterConfirm(sb, font, state, c, itemsTex, row, _buyConfirmBtn, _buyCancelBtn);
    }

    /// <summary>Selling gets its own compact confirm rather than the acquisition one: the player already
    /// owns the thing, so requirements and effect previews are noise. What matters is what is being given
    /// up, its condition — which is what sets the offer — and what the shop pays.</summary>
    private void DrawSellConfirm(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c, IReadOnlyList<Texture2D?> itemsTex)
    {
        var inv = state.Me?.Inv?[_pendingSellSlot];
        var item = inv is not null && inv.Num > 0 && inv.Num <= state.Limits.Items ? state.Items[inv.Num] : null;

        var bgRect = new Rectangle(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        string name = item?.Name?.Trim() ?? "?";
        int quantity = item?.Type == ItemType.Currency ? Math.Max(inv?.Quantity ?? 1, 1) : 1;

        float textY = c.Y + 12;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.ShopPanel_SellItemLabel), new Vector2(c.X + 8, textY), Color.LightGray, c.Width - 16);
        textY += 18;
        UiHelper.DrawLabel(sb, font, quantity > 1 ? $"{name} x{quantity}" : name, new Vector2(c.X + 8, textY), Color.White, c.Width - 16);
        textY += 18;
        textY = DrawItemPreview(sb, c, itemsTex, item?.Pic ?? -1, item?.ItemSheet ?? 0, textY);

        if (item is not null && item.Durability > 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_DurabilityLabel, ("Current", inv!.Dur), ("Max", item.Durability)), new Vector2(c.X + 8, textY), UiHelper.DurabilityColor(inv.Dur, item.Durability), c.Width - 16);
            textY += 18;
        }

        int offer = item is not null ? EconomyFormulas.ItemSellValue(item, inv!.Dur) * quantity : 0;

        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.ShopPanel_SellOffer, ("Gold", offer)),
            new Vector2(c.X + 8, textY), offer > 0 ? Color.Gold : Color.OrangeRed, c.Width - 16);
        textY += 18;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", state.PlayerGold())), new Vector2(c.X + 8, textY), Color.Gold, c.Width - 16);
        textY += 20;

        // A worthless item still sells — the vendor doubles as the way to empty a bag. Say so plainly
        // rather than leaving the player wondering whether the button is broken.
        if (offer <= 0)
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.ShopPanel_SellForNothing), new Vector2(c.X + 8, textY), Color.OrangeRed, c.Width - 16);

        _sellConfirmBtn.Draw(sb, font, _input);
        _sellCancelBtn.Draw(sb, font, _input);
    }

    private void DrawTabs(SpriteBatch sb, SpriteFont font, Rectangle c)
    {
        var rects = TabRects(c);
        string[] labels =
        [
            ClientStrings.Get(ClientStrings.ShopPanel_BuyTab),
            ClientStrings.Get(ClientStrings.ShopPanel_SellTab),
            ClientStrings.Get(ClientStrings.ShopPanel_TradeTab),
        ];
        for (int i = 0; i < rects.Length; i++)
        {
            bool active = (Tab)i == _tab;
            bool hovered = !active && rects[i].Contains(_input.MousePosition);
            TabStrip.DrawCenteredTab(sb, font, rects[i], labels[i], active, hovered);
        }
    }

    // 32×32 item icon left-justified to the same x as the surrounding text on both confirm
    // overlays. No-ops cleanly when the texture is missing or the item has no pic, so the
    // caller can blindly add the gap and continue laying out text below.
    private static float DrawItemPreview(SpriteBatch sb, Rectangle c, IReadOnlyList<Texture2D?> itemsTex, int pic, int itemSheet, float textY)
    {
        if (pic < 0) return textY;
        const int iconSize = 32;
        var iconRect = new Rectangle(c.X + 8, (int)textY, iconSize, iconSize);
        sb.DrawItemIcon(itemsTex, pic, itemSheet, iconRect, Color.White);
        UiHelper.DrawBorder(sb, iconRect, UiHelper.ConfirmOverlayBorder);
        return textY + iconSize + 6;
    }

    private void SetButtonBounds(Rectangle c)
    {
        // Slot 0 is whichever action the active tab owns; slot 1 is always Repair, so its position
        // does not move as the player switches tabs.
        var action = UiHelper.PanelBottomButton(c, 0);
        _buyBtn.Bounds = action;
        _barterBtn.Bounds = action;
        _sellBtn.Bounds = action;
        _fixBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private void SetBuyConfirmButtonBounds(Rectangle c)
    {
        _buyConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _buyCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private void SetSellConfirmButtonBounds(Rectangle c)
    {
        _sellConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _sellCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private void SetFixButtonBounds(Rectangle c)
    {
        _fixConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _fixCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private void SetRepairConfirmButtonBounds(Rectangle c)
    {
        _repairConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _repairCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private void SetBarterConfirmButtonBounds(Rectangle c)
    {
        _barterConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _barterCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private static Rectangle ListBoundsOf(Rectangle c) =>
        new(c.X + 4, c.Y + 2, c.Width - 8, Math.Max(0, c.Height - 44));

    // Leaves room for the tab strip above and the gold label + buttons below.
    private static Rectangle TabbedListBoundsOf(Rectangle c) =>
        new(c.X + 4, c.Y + TabStripH + 2, c.Width - 8, Math.Max(0, c.Height - TabStripH - 66));
}
