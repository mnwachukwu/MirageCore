using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Globalization;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Marketplace (M) panel, opened from the Inn panel (server-gated to an inn) and movement-locking like the
/// bank. BROWSE lists everyone's items (Item / Seller / Price) with a Buy button (a currency listing prompts
/// for how many units to buy — a partial buy); MY LISTINGS shows your own with Cancel + a "List Item" flow
/// that shows the sale tax and net payout up front; SALES is your completed-sale history (item / buyer / net /
/// date). The listing set + your sales live on <see cref="ClientState"/>, replaced wholesale by every
/// <c>MarketListPacket</c>. A completed purchase arrives as delayed mail (see the Mail panel).
/// </summary>
public sealed class MarketPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 380, 320), minH: 240, minW: 360);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    // Owns the keyboard while typing a price or a buy amount, so GameplayScreen suppresses world hotkeys.
    public bool IsCapturingInput => IsOpen && (_listing || _buyAmountPrompt.IsOpen || _buyConfirm.IsOpen);

    public void Open()
    {
        IsOpen = true;
        _tab = Tab.Browse;
        _listing = false;
        _lastMarketVersion = -1;
        _buyAmountPrompt.Close();
        _listQtyPrompt.Close();
        _buyConfirm.Close();
    }
    public void Close()
    {
        IsOpen = false;
        _listing = false;
        _buyAmountPrompt.Close();
        _listQtyPrompt.Close();
        _buyConfirm.Close();
        Tooltip.CloseScope(TooltipScope);
    }
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    // ── Tabs ────────────────────────────────────────────────────────────────────────
    private enum Tab { Browse, Mine, Sales }
    private Tab _tab = Tab.Browse;
    private Tab _lastTab = Tab.Browse;
    private readonly Button _browseTab = new();
    private readonly Button _mineTab = new();
    private readonly Button _salesTab = new();

    // ── Tables ──────────────────────────────────────────────────────────────────────
    private readonly Table<MarketListPacket.Entry> _table = new();      // Browse + My Listings
    private readonly Table<MarketSale> _salesTable = new();    // Sales history
    private int _lastMarketVersion = -1;
    private long _serverNowUtc;         // server clock from the last market push (drives the Time Left column)
    private ItemRecord[]? _itemDefs;   // refreshed each frame so the Item column can resolve names

    // Column-layout persistence: the listings table is reorderable, the sales table is fixed; both persist
    // widths + sort. The host saves/restores via ColumnTables and watches ColumnsChanged.
    public IReadOnlyDictionary<string, IColumnLayoutTable> ColumnTables { get; }
    public bool ColumnsChanged { get; private set; }

    private readonly Button _buyBtn = new();
    private readonly Button _cancelListingBtn = new();
    private readonly Button _listItemBtn = new();
    private readonly Button _refreshBtn = new();                    // manual re-fetch (live sync also pushes changes)
    private readonly NumberPromptDialog _buyAmountPrompt = new();   // partial-buy amount for a currency listing
    // Buying spends gold on someone else's goods and cannot be undone: the listing is gone, and what you
    // bought arrives as delayed mail rather than in your hands. One misclick in a table whose rows move as
    // other people list and sell is enough, so the price is put in front of you before it leaves.
    private readonly ConfirmDialog _buyConfirm = new();

    // ── List sub-view ───────────────────────────────────────────────────────────────
    private bool _listing;
    private readonly ListBox _invList = new();
    private readonly List<int> _invSlots = new();
    private readonly TextInputField _priceField = new() { MaxLength = 10 };
    private readonly NumberPromptDialog _listQtyPrompt = new();     // units-to-list for a currency (partial listing)
    private readonly Button _confirmListBtn = new();
    private readonly Button _cancelBtn = new();
    private Rectangle _invRect, _priceRect;

    private int _labelsGeneration = -1;
    private InputState _input = new();

    private const int ButtonH = 26;
    private const int TabW = 84;
    private const int ActionBtnW = 90;
    private const int RefreshW = 74;
    private const string TooltipScope = "Market";
    private const int MarketColPrice = 2;
    private const int SalesColDate = 3;

    public MarketPanel()
    {
        _table.AllowReorder = true;   // opt in to drag-to-reorder columns (still sortable + resizable)
        _table.Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColItem), m => ItemName(m.ItemNum), width: 120, minWidth: 60)
              .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColSeller), m => m.Seller, width: 76, minWidth: 50)
              .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColPrice), m => m.Price, m => PriceText(m), width: 66, minWidth: 44)
              // Time until the listing expires (returns to the seller). Sorts by ListedUtc — oldest = least time left.
              .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColTimeLeft), m => m.ListedUtc, m => FormatTimeLeft(m), width: 74, minWidth: 44)
              .WithRowKey(m => m.Id);
        _table.SortBy(MarketColPrice, ascending: true);   // cheapest first by default

        // Sales history stays fixed-order (read-only log) — no AllowReorder opt-in — but still persists widths + sort.
        _salesTable.Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColItem), s => ItemName(s.ItemNum), width: 120, minWidth: 60)
                   .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColBuyer), s => s.Buyer, width: 84, minWidth: 50)
                   .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColNet), s => s.Price - s.Tax, s => (s.Price - s.Tax).ToString("N0"), width: 66, minWidth: 40)
                   .Column(() => ClientStrings.Get(ClientStrings.MarketPanel_ColDate), s => s.TimeUtc, s => FormatDate(s.TimeUtc), width: 86, minWidth: 60)
                   .WithRowKey(s => s.Id);
        _salesTable.SortBy(SalesColDate, ascending: false);   // newest first
        ColumnTables = new Dictionary<string, IColumnLayoutTable>
        {
            ["market.listings"] = _table,
            ["market.sales"] = _salesTable,
        };
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        ColumnsChanged = false;
        if (!IsOpen) return;
        _input = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            Close();
            return;
        }

        if (_listing)
        {
            UpdateListing(input, state, sender, isActive);
            return;
        }

        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        Layout(c, out var listRect);

        // The partial-buy amount prompt is modal while open.
        if (_buyAmountPrompt.IsOpen)
        {
            _buyAmountPrompt.Update(input, c, nowMs);
            return;
        }

        // So is the buy confirmation.
        if (_buyConfirm.IsOpen)
        {
            _buyConfirm.Update(input);
            return;
        }

        if (_refreshBtn.IsClicked(input)) sender.SendMarketRefresh();   // manual re-fetch (live sync also pushes)

        if (_tab != Tab.Browse && _browseTab.IsClicked(input)) SetTab(Tab.Browse);
        else if (_tab != Tab.Mine && _mineTab.IsClicked(input)) SetTab(Tab.Mine);
        else if (_tab != Tab.Sales && _salesTab.IsClicked(input)) SetTab(Tab.Sales);
        if (_tab == Tab.Mine && _listItemBtn.IsClicked(input))
        {
            StartListing();
            return;
        }

        SyncItems(state);

        if (_tab == Tab.Sales)
        {
            _salesTable.Update(input, listRect, keyboardActive: isActive);
            ColumnsChanged |= _salesTable.LayoutChanged;
            return;
        }

        _table.Update(input, listRect, keyboardActive: isActive);
        ColumnsChanged |= _table.LayoutChanged;
        var sel = _table.SelectedItem;

        if (_tab == Tab.Mine)
        {
            _cancelListingBtn.Enabled = sel is not null;
            if (sel is not null && _cancelListingBtn.IsClicked(input))
            {
                sender.SendMarketCancel(sel.Id);
                _table.ClearSelection();
            }
            return;
        }

        // Browse: buy. A currency listing (>1 unit) prompts for how many units to buy; anything else buys whole.
        long myGold = state.PlayerGold();
        bool own = sel is not null && string.Equals(sel.Seller, state.MyLogin, StringComparison.OrdinalIgnoreCase);
        bool partial = sel is not null && IsCurrency(state, sel.ItemNum) && sel.Quantity > 1;
        int maxUnits = sel is not null ? MaxBuyable(sel, myGold) : 0;
        bool canBuy = sel is not null && !own && (partial ? maxUnits >= 1 : sel.Price <= myGold);
        _buyBtn.Enabled = canBuy;
        if (canBuy && _buyBtn.IsClicked(input))
        {
            int listingId = sel!.Id;
            string itemName = ItemName(sel.ItemNum);
            if (partial)
            {
                // The amount prompt already makes the player state a number, so the confirmation comes
                // after it and quotes the total that number actually costs.
                _buyAmountPrompt.Open(ClientStrings.Get(ClientStrings.MarketPanel_Buy), itemName, maxUnits,
                    amt => ConfirmBuy(sender, listingId, amt, itemName, (long)amt * sel.Price));
            }
            else
            {
                ConfirmBuy(sender, listingId, 0, itemName, sel.Price);
            }
        }
    }

    /// <summary>Puts the price in front of the player, then sends the buy if they say yes.
    ///
    /// <para>Quotes the TOTAL rather than the per-unit price: for a partial currency buy those differ, and
    /// the number that matters is the one leaving the purse.</para>
    ///
    /// <para>The listing id is captured by the closure, so the purchase stays pointed at the row the
    /// player was looking at even if a live sync reorders the table while the dialog is open. Should that
    /// listing sell in the meantime, the server answers that it is gone.</para></summary>
    private void ConfirmBuy(ClientPacketSender sender, int listingId, int amount, string itemName, long totalCost) =>
        _buyConfirm.Open(
            ClientStrings.Format(ClientStrings.MarketPanel_ConfirmBuy,
                ("Item", itemName), ("Gold", totalCost.ToString("N0"))),
            () => sender.SendMarketBuy(listingId, amount));

    private void UpdateListing(InputState input, ClientState state, ClientPacketSender sender, bool isActive)
    {
        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        LayoutListing(c);

        // The units-to-list prompt (currency partial listing) is modal while open.
        if (_listQtyPrompt.IsOpen)
        {
            _listQtyPrompt.Update(input, c, nowMs);
            return;
        }

        RebuildInvList(state);
        _invList.Update(input, _invRect, keyboardActive: false);

        // The price field is the only text input here, so it always holds focus.
        if (input.IsClickIn(_priceRect)) _priceField.HandleMouseClick(input.MousePosition.X, false);
        _priceField.Feed(input, nowMs);

        int price = ParsePrice(_priceField.Text);
        int slot = _invList.SelectedIndex >= 0 && _invList.SelectedIndex < _invSlots.Count ? _invSlots[_invList.SelectedIndex] : 0;
        _confirmListBtn.Enabled = slot > 0 && price > 0;
        if (_confirmListBtn.Enabled && _confirmListBtn.IsClicked(input))
        {
            var inv = state.Me?.Inv?[slot];
            if (inv is not null && IsCurrency(state, inv.Num) && inv.Quantity > 1)
            {
                // Currency: choose how many units to list (a partial listing) at the per-unit price.
                int listSlot = slot, unitPrice = price;
                _listQtyPrompt.Open(ClientStrings.Get(ClientStrings.MarketPanel_QtyPrompt), ItemName(inv.Num), inv.Quantity,
                    units => { sender.SendMarketCreate(listSlot, units, unitPrice); _listing = false; });
            }
            else
            {
                sender.SendMarketCreate(slot, inv?.Quantity ?? 0, price);   // whole slot (amount ignored for gear)
                _listing = false;
            }
            return;
        }
        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            _listing = false;
        }
    }

    private void SetTab(Tab tab)
    {
        _tab = tab;
        _table.ClearSelection();
        _salesTable.ClearSelection();
        Tooltip.CloseScope(TooltipScope);
    }

    private void StartListing()
    {
        _listing = true;
        _invList.SelectedIndex = -1;
        _priceField.Clear();
    }

    // ── Draw ─────────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex, bool isActive = false)
    {
        if (!IsOpen) return;
        _itemDefs = state.Items;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.MarketPanel_Title), isActive);

        if (_listing)
        {
            DrawListing(sb, font, state, itemsTex);
            _panel.DrawOverlay(sb);
            return;
        }

        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        Layout(c, out var listRect);

        _browseTab.Draw(sb, font, _input, normalColor: _tab == Tab.Browse ? UiHelper.ActiveTabColor : (Color?)null);
        _mineTab.Draw(sb, font, _input, normalColor: _tab == Tab.Mine ? UiHelper.ActiveTabColor : (Color?)null);
        _salesTab.Draw(sb, font, _input, normalColor: _tab == Tab.Sales ? UiHelper.ActiveTabColor : (Color?)null);
        if (_tab == Tab.Mine) _listItemBtn.Draw(sb, font, _input);
        _refreshBtn.Draw(sb, font, _input);

        SyncItems(state);

        if (_tab == Tab.Sales)
        {
            if (state.MarketSales.Count == 0)
            {
                UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.MarketPanel_EmptySales),
                    new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
            }
            else
            {
                _salesTable.Draw(sb, font, listRect);
            }

            if (itemsTex is not null && _salesTable.HoveredItem is { } hs)
                ShowItemTooltip(state, itemsTex, hs.ItemNum, hs.Quantity, 0, (TooltipScope, "sale", hs.Id));
            _panel.DrawOverlay(sb);
            return;
        }

        var rows = _tab == Tab.Mine
            ? state.Market.Where(l => string.Equals(l.Seller, state.MyLogin, StringComparison.OrdinalIgnoreCase))
            : state.Market;
        if (!rows.Any())
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(_tab == Tab.Mine ? ClientStrings.MarketPanel_EmptyMine : ClientStrings.MarketPanel_Empty),
                new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _table.Draw(sb, font, listRect);
        }

        if (itemsTex is not null && _table.HoveredItem is { } hov)
            ShowItemTooltip(state, itemsTex, hov.ItemNum, hov.Quantity, hov.Dur, (TooltipScope, "row", hov.Id));

        if (_tab == Tab.Mine) _cancelListingBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        else _buyBtn.Draw(sb, font, _input);

        _buyAmountPrompt.Draw(sb, font, c, nowMs);
        // After the amount prompt: on a partial buy the confirmation follows it, so it draws on top.
        _buyConfirm.Draw(sb, font, c);
        _panel.DrawOverlay(sb);
    }

    private void DrawListing(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        LayoutListing(c);
        RebuildInvList(state);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.MarketPanel_ListTitle), new Vector2(c.X + 4, c.Y + 4), Color.Yellow, c.Width - 8);
        _invList.Draw(sb, font, _invRect);

        if (itemsTex is not null && !_listQtyPrompt.IsOpen && _invList.HoveredIndex >= 0 && _invList.HoveredIndex < _invSlots.Count)
        {
            int slot = _invSlots[_invList.HoveredIndex];
            var s = state.Me?.Inv?[slot];
            if (s is not null) ShowItemTooltip(state, itemsTex, s.Num, s.Quantity, s.Dur, (TooltipScope, "inv", slot));
        }

        // A currency stack is priced PER UNIT; anything else is a whole-stack price.
        bool sellingCurrency = _invList.SelectedIndex >= 0 && _invList.SelectedIndex < _invSlots.Count
            && IsCurrency(state, state.Me?.Inv?[_invSlots[_invList.SelectedIndex]]?.Num ?? 0);
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(sellingCurrency ? ClientStrings.MarketPanel_PriceLabelPerUnit : ClientStrings.MarketPanel_PriceLabel),
            new Vector2(_priceRect.X, _priceRect.Y - 14), Color.LightGray, _priceRect.Width);
        _priceField.Draw(sb, font, _priceRect, focused: true, nowMs);

        // Live sale-tax preview: what the seller nets after the gold-sink tax (per unit for a currency listing).
        int price = ParsePrice(_priceField.Text);
        if (price > 0)
        {
            int tax = SaleTax(price);
            string preview = ClientStrings.Format(sellingCurrency ? ClientStrings.MarketPanel_TaxPreviewPerUnit : ClientStrings.MarketPanel_TaxPreview,
                ("Percent", Constants.MarketSaleTaxPercent), ("Tax", tax.ToString("N0")), ("Net", (price - tax).ToString("N0")));
            UiHelper.DrawLabel(sb, font, preview, new Vector2(_priceRect.X, _priceRect.Bottom + 6), Color.Gold, c.Width - 8);
        }

        _confirmListBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);
        _listQtyPrompt.Draw(sb, font, c, nowMs);
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    // The sale tax withheld from a price (gold sink) — must match MarketSystem.SaleTax on the server.
    private static int SaleTax(int price) => (int)((long)price * Constants.MarketSaleTaxPercent / 100);

    private static int ParsePrice(string s) => int.TryParse(s, out int p) && p > 0 ? Math.Min(p, Constants.MarketMaxPrice) : 0;

    // Most units of a currency listing the buyer can afford at its per-unit price (capped at the stock).
    private static int MaxBuyable(MarketListPacket.Entry l, long gold) => l.Price <= 0 ? 0 : (int)Math.Min(l.Quantity, gold / l.Price);

    private bool IsCurrency(ClientState state, int itemNum)
        => itemNum > 0 && itemNum < state.Items.Length && state.Items[itemNum]?.Type == ItemType.Currency;

    private string ItemName(int itemNum)
    {
        if (_itemDefs is not null && itemNum > 0 && itemNum < _itemDefs.Length)
            return _itemDefs[itemNum]?.Name?.TrimEnd() ?? $"Item {itemNum}";
        return $"Item {itemNum}";
    }

    // A currency listing prices per unit, so its Price column shows the "/ea" form; anything else is a total.
    private string PriceText(MarketListPacket.Entry m)
    {
        bool currency = _itemDefs is not null && m.ItemNum > 0 && m.ItemNum < _itemDefs.Length && _itemDefs[m.ItemNum]?.Type == ItemType.Currency;
        return currency
            ? ClientStrings.Format(ClientStrings.MarketPanel_PricePerUnitFormat, ("Price", m.Price.ToString("N0")))
            : m.Price.ToString("N0");
    }

    private void ShowItemTooltip(ClientState state, IReadOnlyList<Texture2D?> itemsTex, int itemNum, int value, int dur, object key)
    {
        if (itemNum <= 0 || itemNum >= state.Items.Length) return;
        var def = state.Items[itemNum];
        if (def is not null)
        {
            Tooltip.NotifyHoverItem(TooltipScope, key, def, new PlayerInvSlot { Num = itemNum, Quantity = value, Dur = dur },
                state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
        }
    }

    private void SyncItems(ClientState state)
    {
        _serverNowUtc = state.MarketNowUtc;   // always current, so the Time Left column tracks the latest push
        if (_lastMarketVersion == state.MarketVersion && _lastTab == _tab) return;
        _lastMarketVersion = state.MarketVersion;
        _lastTab = _tab;
        if (_tab == Tab.Sales)
        {
            _salesTable.Items = state.MarketSales;
            return;
        }
        _table.Items = _tab == Tab.Mine
            ? state.Market.Where(l => string.Equals(l.Seller, state.MyLogin, StringComparison.OrdinalIgnoreCase)).ToList()
            : state.Market;
    }

    // Rebuild the inventory listing candidates each frame, skipping empty, equipped, and non-listable slots
    // (gold, reagent, valor, ... can't be sold).
    private void RebuildInvList(ClientState state) =>
        InventoryListBuilder.Rebuild(state, _invList, _invSlots, (_, item) => item?.NonListable == true);

    private void RefreshLabels()
    {
        _browseTab.Label = ClientStrings.Get(ClientStrings.MarketPanel_TabBrowse);
        _mineTab.Label = ClientStrings.Get(ClientStrings.MarketPanel_TabMine);
        _salesTab.Label = ClientStrings.Get(ClientStrings.MarketPanel_TabSales);
        _listItemBtn.Label = ClientStrings.Get(ClientStrings.MarketPanel_ListItem);
        _buyBtn.Label = ClientStrings.Get(ClientStrings.MarketPanel_Buy);
        _cancelListingBtn.Label = ClientStrings.Get(ClientStrings.MarketPanel_CancelListing);
        _confirmListBtn.Label = ClientStrings.Get(ClientStrings.MarketPanel_List);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        _refreshBtn.Label = ClientStrings.Get(ClientStrings.MarketPanel_Refresh);
    }

    private void Layout(Rectangle c, out Rectangle listRect)
    {
        _browseTab.Bounds = new Rectangle(c.X + 4, c.Y + 4, TabW, ButtonH);
        _mineTab.Bounds = new Rectangle(_browseTab.Bounds.Right + 2, c.Y + 4, TabW, ButtonH);
        _salesTab.Bounds = new Rectangle(_mineTab.Bounds.Right + 2, c.Y + 4, TabW, ButtonH);
        _listItemBtn.Bounds = new Rectangle(c.Right - 4 - ActionBtnW, c.Y + 4, ActionBtnW, ButtonH);
        int contentTop = _browseTab.Bounds.Bottom + 4;
        int btnY = c.Bottom - ButtonH - 4;
        _refreshBtn.Bounds = new Rectangle(c.X + 4, btnY, RefreshW, ButtonH);   // bottom-left on every tab
        _buyBtn.Bounds = new Rectangle(c.Right - 4 - ActionBtnW, btnY, ActionBtnW, ButtonH);
        _cancelListingBtn.Bounds = _buyBtn.Bounds;
        // Every tab has the bottom row now (Refresh), so the list stops above it on all of them.
        listRect = new Rectangle(c.X + 4, contentTop, c.Width - 8, Math.Max(0, btnY - 4 - contentTop));
    }

    private void LayoutListing(Rectangle c)
    {
        const int fieldH = 20, labelH = 14, gap = 6;
        int x = c.X + 4, w = c.Width - 8;
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
        _confirmListBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        int priceY = _confirmListBtn.Bounds.Y - gap - 18 - fieldH;   // room for the tax preview under the field
        _priceRect = new Rectangle(x, priceY, Math.Min(160, w), fieldH);
        int invTop = c.Y + 4 + labelH;
        _invRect = new Rectangle(x, invTop, w, Math.Max(0, priceY - labelH - gap - invTop));
    }

    private static string FormatDate(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Compact "time left" for a listing — the largest non-zero unit until it expires (returns to the seller),
    // from the frozen server clock. Rounds up to the minute and reuses the mail countdown's localized unit strings.
    private string FormatTimeLeft(MarketListPacket.Entry m)
    {
        long remaining = m.ListedUtc + Constants.MarketListingLifetimeSeconds - _serverNowUtc;
        long totalMinutes = (Math.Max(0, remaining) + 59) / 60;   // round up, like the mail countdown
        long days = totalMinutes / (24 * 60);
        long hours = totalMinutes % (24 * 60) / 60;
        long minutes = totalMinutes % 60;
        if (days > 0)
            return ClientStrings.Format(days == 1 ? ClientStrings.MailPanel_CountdownDay : ClientStrings.MailPanel_CountdownDays, ("N", days));
        if (hours > 0)
            return ClientStrings.Format(hours == 1 ? ClientStrings.MailPanel_CountdownHour : ClientStrings.MailPanel_CountdownHours, ("N", hours));
        return ClientStrings.Format(minutes == 1 ? ClientStrings.MailPanel_CountdownMinute : ClientStrings.MailPanel_CountdownMinutes, ("N", minutes));
    }
}
