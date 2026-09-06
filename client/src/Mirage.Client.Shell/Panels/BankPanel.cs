using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>Bank panel — two-column layout: inventory on the left, bank on the right. Buttons
/// move a single slot at a time; right-click each list for the bulk 1/X/All menu.</summary>
public sealed class BankPanel : IGamePanel
{
    private const int PanelDefaultX = 20;
    private const int PanelDefaultY = 20;
    private const int PanelDefaultW = 460;
    private const int PanelDefaultH = 280;
    private const int PanelMinH = 180;
    private const int ColDividerW = 1;
    private const int HeaderPadLeft = 4;
    private const int HeaderPadTop = 2;
    private const int FooterLabelFromBottom = 56;
    private const int ListInset = 4;
    private const int ListHeaderH = 18;
    private const int ListInsetW = 8;
    private const int ListFootH = 78;

    private readonly DraggablePanel _panel = new(new Rectangle(PanelDefaultX, PanelDefaultY, PanelDefaultW, PanelDefaultH), minH: PanelMinH);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    private const string TooltipScopeInv = "BankInv";
    private const string TooltipScopeBank = "BankSlot";

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _invDirty = true;
            _bankDirty = true;
        }
        else
        {
            _prompt.Close();
            _contextMenu.Close();
            Tooltip.CloseScope(TooltipScopeInv);
            Tooltip.CloseScope(TooltipScopeBank);
        }
    }

    // True while a number prompt or context menu is showing; suppresses Escape-closes-panel and
    // keeps the panel "active" so keyboard input gets routed to it.
    public bool IsCapturingInput => _prompt.IsOpen || _contextMenu.IsOpen;

    private readonly ListBox _invList = new() { ShowTruncationTooltip = false };   // rows show the richer item tooltip
    private readonly ListBox _bankList = new() { ShowTruncationTooltip = false };
    private readonly Button _depositBtn = new();
    private readonly Button _withdrawBtn = new();
    // Right-justified [Sort] link in the bank column header — tidies only the bank (see BankSystem.SortBank).
    private readonly Link _sortLink = new();
    private readonly ContextMenu _contextMenu = new();
    private readonly NumberPromptDialog _prompt = new();
    private InputState _input = new();
    private SpriteFont? _cachedFont;
    private int _labelsGeneration = -1;

    private int _invHash;
    private bool _invDirty = true;
    private int _bankHash;
    private bool _bankDirty = true;

    private int _filledInvSlots;
    private int _filledBankSlots;
    private long _bankGold;

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            state.BankOpen = false;
            _prompt.Close();
            _contextMenu.Close();
            Tooltip.CloseScope(TooltipScopeInv);
            Tooltip.CloseScope(TooltipScopeBank);
            return;
        }

        var c = _panel.ContentBounds;
        long nowMs = Environment.TickCount64;

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

        var (left, right) = SplitContent(c);
        _depositBtn.Bounds = ColumnButton(left);
        _withdrawBtn.Bounds = ColumnButton(right);

        // Hit-test the [Sort] link before the lists so a header click can't fall through to a bank row.
        // Needs the font (captured in Draw); on the very first frame there's nothing to click yet.
        if (_cachedFont != null)
        {
            PositionSortLink(right, _cachedFont);
            if (_sortLink.IsClicked(input))
            {
                sender.SendBankSort();
                _bankDirty = true;
                input.ConsumeMouseClick();
                return;
            }
        }

        _invList.Update(input, ListBoundsOf(left));
        _bankList.Update(input, ListBoundsOf(right));

        // Capacity-aware button enable: source slot has an item AND destination has room.
        _depositBtn.Enabled = TryGetSelectedInvItemNum(state, out int selectedInvNum) && BankHasRoomFor(state, selectedInvNum);
        _withdrawBtn.Enabled = TryGetSelectedBankItemNum(state, out int selectedBankNum) && InvHasRoomFor(state, selectedBankNum);

        if (_depositBtn.IsClicked(input) && _invList.SelectedIndex >= 0)
        {
            int slot = _invList.SelectedIndex + 1;
            var inv = state.Me.Inv[slot];
            if (inv.Num > 0 && inv.Num <= state.Limits.Items)
                BeginDeposit(slot, state, sender);
        }

        if (_withdrawBtn.IsClicked(input) && _bankList.SelectedIndex >= 0)
        {
            int slot = _bankList.SelectedIndex + 1;
            var bankSlot = state.Bank[slot];
            if (bankSlot.Num > 0 && bankSlot.Num <= state.Limits.Items)
                BeginWithdraw(slot, state, sender);
        }

        // Right-click on either list opens the matching bulk-action menu. ConsumeRightClickedRow
        // returns the 1-based hovered row index and consumes the click so it can't bleed through.
        int invRcSlot = _invList.ConsumeRightClickedRow(input);
        if (invRcSlot > 0) OpenInvContextMenu(invRcSlot, input.MousePosition, state, sender);

        int bankRcSlot = _bankList.ConsumeRightClickedRow(input);
        if (bankRcSlot > 0) OpenBankContextMenu(bankRcSlot, input.MousePosition, state, sender);
    }

    // ── Single-slot button actions (currency prompts, non-currency sends immediately) ────────

    private void BeginDeposit(int invSlot, ClientState state, ClientPacketSender sender)
    {
        var inv = state.Me.Inv[invSlot];
        var item = state.Items[inv.Num];
        if (item?.Type == ItemType.Currency)
        {
            string itemName = item.Name?.TrimEnd() ?? $"Item {inv.Num}";
            int max = inv.Quantity;
            _prompt.Open(
                ClientStrings.Get(ClientStrings.BankPanel_DepositItemLabel),
                itemName,
                max,
                amt => { sender.SendBankDeposit(invSlot, amt); _invDirty = true; _bankDirty = true; });
        }
        else
        {
            sender.SendBankDeposit(invSlot, 0);
            _invDirty = true;
            _bankDirty = true;
        }
    }

    private void BeginWithdraw(int bankSlot, ClientState state, ClientPacketSender sender)
    {
        var bank = state.Bank[bankSlot];
        var item = state.Items[bank.Num];
        if (item?.Type == ItemType.Currency)
        {
            string itemName = item.Name?.TrimEnd() ?? $"Item {bank.Num}";
            int max = bank.Quantity;
            _prompt.Open(
                ClientStrings.Get(ClientStrings.BankPanel_WithdrawItemLabel),
                itemName,
                max,
                amt => { sender.SendBankWithdraw(bankSlot, amt); _invDirty = true; _bankDirty = true; });
        }
        else
        {
            sender.SendBankWithdraw(bankSlot, 0);
            _invDirty = true;
            _bankDirty = true;
        }
    }

    // ── Right-click context menus ────────────────────────────────────────────

    private void OpenInvContextMenu(int invSlot, Point mousePos, ClientState state, ClientPacketSender sender)
    {
        if (_cachedFont is null) return;
        var inv = state.Me?.Inv?[invSlot];
        if (inv is null || inv.Num <= 0 || inv.Num > state.Limits.Items) return;
        int itemNum = inv.Num;
        var item = state.Items[itemNum];
        if (item is null) return;

        bool hasRoom = BankHasRoomFor(state, itemNum);
        bool isCurrency = item.Type == ItemType.Currency;
        string itemName = item.Name?.TrimEnd() ?? $"Item {itemNum}";

        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Deposit1),
                () => { if (isCurrency) sender.SendBankDeposit(invSlot, 1); else sender.SendBankDepositBulk(itemNum, 1); _invDirty = _bankDirty = true; },
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_DepositX),
                () =>
                {
                    int max = isCurrency ? inv.Quantity : InventoryQuery.CountInvSlotsMatching(state, itemNum, skipEquipped: true);
                    if (max < 1) return;
                    _prompt.Open(
                        ClientStrings.Get(ClientStrings.BankPanel_DepositItemLabel),
                        itemName,
                        max,
                        amt => { if (isCurrency) sender.SendBankDeposit(invSlot, amt); else sender.SendBankDepositBulk(itemNum, amt); _invDirty = _bankDirty = true; });
                },
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_DepositAll),
                () => { if (isCurrency) sender.SendBankDeposit(invSlot, 0); else sender.SendBankDepositBulk(itemNum, 0); _invDirty = _bankDirty = true; },
                hasRoom),
        };
        _contextMenu.Open(mousePos, itemName, items, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
    }

    private void OpenBankContextMenu(int bankSlot, Point mousePos, ClientState state, ClientPacketSender sender)
    {
        if (_cachedFont is null) return;
        var bank = state.Bank[bankSlot];
        if (bank.Num <= 0 || bank.Num > state.Limits.Items) return;
        int itemNum = bank.Num;
        var item = state.Items[itemNum];
        if (item is null) return;

        bool hasRoom = InvHasRoomFor(state, itemNum);
        bool isCurrency = item.Type == ItemType.Currency;
        string itemName = item.Name?.TrimEnd() ?? $"Item {itemNum}";

        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Withdraw1),
                () => { if (isCurrency) sender.SendBankWithdraw(bankSlot, 1); else sender.SendBankWithdrawBulk(itemNum, 1); _invDirty = _bankDirty = true; },
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_WithdrawX),
                () =>
                {
                    int max = isCurrency ? bank.Quantity : CountBankSlotsMatching(state, itemNum);
                    if (max < 1) return;
                    _prompt.Open(
                        ClientStrings.Get(ClientStrings.BankPanel_WithdrawItemLabel),
                        itemName,
                        max,
                        amt => { if (isCurrency) sender.SendBankWithdraw(bankSlot, amt); else sender.SendBankWithdrawBulk(itemNum, amt); _invDirty = _bankDirty = true; });
                },
                hasRoom),
            new(ClientStrings.Get(ClientStrings.ContextMenu_WithdrawAll),
                () => { if (isCurrency) sender.SendBankWithdraw(bankSlot, 0); else sender.SendBankWithdrawBulk(itemNum, 0); _invDirty = _bankDirty = true; },
                hasRoom),
        };
        _contextMenu.Open(mousePos, itemName, items, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
    }

    // ── Capacity / selection helpers ─────────────────────────────────────────

    private static bool BankHasRoomFor(ClientState state, int itemNum)
    {
        if (itemNum <= 0 || itemNum > state.Limits.Items) return false;
        var item = state.Items[itemNum];
        bool isCurrency = item?.Type == ItemType.Currency;
        if (isCurrency)
        {
            for (int i = 1; i <= Constants.MaxBankSlots; i++)
                if (state.Bank[i].Num == itemNum) return true;
        }
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
            if (state.Bank[i].Num == 0) return true;
        return false;
    }

    private static bool InvHasRoomFor(ClientState state, int itemNum)
    {
        if (itemNum <= 0 || itemNum > state.Limits.Items) return false;
        var me = state.Me;
        if (me?.Inv is null) return false;
        var item = state.Items[itemNum];
        bool isCurrency = item?.Type == ItemType.Currency;
        if (isCurrency)
        {
            for (int i = 1; i <= Constants.MaxInv; i++)
                if (me.Inv[i]?.Num == itemNum) return true;
        }
        for (int i = 1; i <= Constants.MaxInv; i++)
            if ((me.Inv[i]?.Num ?? 0) == 0) return true;
        return false;
    }

    private static int CountBankSlotsMatching(ClientState state, int itemNum)
    {
        int count = 0;
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
            if (state.Bank[i].Num == itemNum) count++;
        return count;
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void RefreshInv(ClientState state) => _filledInvSlots = InventoryListBuilder.BuildDisplayRows(state, _invList);

    private void RefreshBank(ClientState state)
    {
        _bankList.Items.Clear();
        _filledBankSlots = 0;
        _bankGold = 0;
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
        {
            var slot = state.Bank[i];
            if (slot.Num <= 0 || slot.Num > state.Limits.Items)
            {
                _bankList.Items.Add($"{i}: {ClientStrings.Get(ClientStrings.Common_Empty)}");
                continue;
            }
            _filledBankSlots++;
            var item = state.Items[slot.Num];
            string name = item?.Name?.TrimEnd() ?? $"Item {slot.Num}";
            if (item?.Type == ItemType.Currency)
            {
                _bankGold += slot.Quantity;
                _bankList.Items.Add($"{i}: {name} ({slot.Quantity:N0})");
            }
            else
            {
                bool broken = item is { Durability: > 0 } && slot.Dur <= 0
                    && ItemRecord.IsEquipment(item.Type);
                _bankList.Items.Add(broken
                    ? $"{i}: {name} {ClientStrings.Get(ClientStrings.Common_Broken)}"
                    : $"{i}: {name}");
            }
        }
    }

    private static int ComputeInvHash(ClientState state)
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
            h.Add(slot?.Quantity ?? 0);
            h.Add(slot?.Dur ?? 0);
        }
        return h.ToHashCode();
    }

    private static int ComputeBankHash(ClientState state)
    {
        var h = new HashCode();
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
        {
            h.Add(state.Bank[i].Num);
            h.Add(state.Bank[i].Quantity);
            h.Add(state.Bank[i].Dur);
        }
        return h.ToHashCode();
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex, bool isActive = false, bool canHover = true)
    {
        if (!IsOpen) return;
        _cachedFont = font;

        int invHash = ComputeInvHash(state);
        if (_invDirty || invHash != _invHash)
        {
            _invHash = invHash;
            _invDirty = false;
            RefreshInv(state);
        }
        int bankHash = ComputeBankHash(state);
        if (_bankDirty || bankHash != _bankHash)
        {
            _bankHash = bankHash;
            _bankDirty = false;
            RefreshBank(state);
        }

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _depositBtn.Label = ClientStrings.Get(ClientStrings.BankPanel_DepositButton);
            _withdrawBtn.Label = ClientStrings.Get(ClientStrings.BankPanel_WithdrawButton);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.BankPanel_Title), isActive);

        var c = _panel.ContentBounds;
        var (left, right) = SplitContent(c);
        _depositBtn.Bounds = ColumnButton(left);
        _withdrawBtn.Bounds = ColumnButton(right);

        UiHelper.DrawFilledRect(sb, new Rectangle(c.X + c.Width / 2, c.Y, ColDividerW, c.Height), UiHelper.ConfirmOverlayBorder);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.BankPanel_InventoryHeader), new Vector2(left.X + HeaderPadLeft, left.Y + HeaderPadTop), Color.LightGray, left.Width - ListInsetW);
        _invList.Draw(sb, font, ListBoundsOf(left));
        string invSlotText = $"{_filledInvSlots}/{Constants.MaxInv}";
        float invFootY = left.Bottom - FooterLabelFromBottom;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", state.PlayerGold())), new Vector2(left.X + HeaderPadLeft, invFootY), Color.Gold, left.Width - ListInsetW);
        sb.DrawString(font, invSlotText, new Vector2(left.Right - HeaderPadLeft - font.MeasureString(invSlotText).X, invFootY), Color.LightGray);
        _depositBtn.Draw(sb, font, _input);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.BankPanel_BankHeader), new Vector2(right.X + HeaderPadLeft, right.Y + HeaderPadTop), Color.LightGray, right.Width - ListInsetW);
        PositionSortLink(right, font);
        _sortLink.Draw(sb, font, _input);
        _bankList.Draw(sb, font, ListBoundsOf(right));
        string bankSlotText = $"{_filledBankSlots}/{Constants.MaxBankSlots}";
        float bankFootY = right.Bottom - FooterLabelFromBottom;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.Common_GoldLabel, ("Gold", _bankGold)), new Vector2(right.X + HeaderPadLeft, bankFootY), Color.Gold, right.Width - ListInsetW);
        sb.DrawString(font, bankSlotText, new Vector2(right.Right - HeaderPadLeft - font.MeasureString(bankSlotText).X, bankFootY), Color.LightGray);
        _withdrawBtn.Draw(sb, font, _input);

        _panel.DrawOverlay(sb);

        // Prompt draws on top of the panel content but underneath the context menu.
        _prompt.Draw(sb, font, c, Environment.TickCount64);
        _contextMenu.Draw(sb, font);

        if (canHover && !_prompt.IsOpen && !_contextMenu.IsOpen)
        {
            NotifyInvHover(state, itemsTex);
            NotifyBankHover(state, itemsTex);
        }
    }

    private void NotifyInvHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _invList.HoveredIndex;
        if (hovered < 0) return;
        int slotIdx = hovered + 1;
        var slot = state.Me?.Inv?[slotIdx];
        if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) return;
        var item = state.Items[slot.Num];
        if (item is null) return;
        Tooltip.NotifyHoverItem(TooltipScopeInv, (TooltipScopeInv, slotIdx, slot.Num), item, slot, state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
    }

    private void NotifyBankHover(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        int hovered = _bankList.HoveredIndex;
        if (hovered < 0) return;
        int bankSlot = hovered + 1;
        var slot = state.Bank[bankSlot];
        if (slot.Num <= 0 || slot.Num > state.Limits.Items) return;
        var item = state.Items[slot.Num];
        if (item is null) return;
        Tooltip.NotifyHoverItem(TooltipScopeBank, (TooltipScopeBank, bankSlot, slot.Num), item, slot, state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
    }

    private bool TryGetSelectedInvItemNum(ClientState state, out int itemNum)
    {
        itemNum = 0;
        if (_invList.SelectedIndex < 0) return false;
        int slot = _invList.SelectedIndex + 1;
        var inv = state.Me?.Inv?[slot];
        if (inv is null || inv.Num <= 0 || inv.Num > state.Limits.Items) return false;
        itemNum = inv.Num;
        return true;
    }

    private bool TryGetSelectedBankItemNum(ClientState state, out int itemNum)
    {
        itemNum = 0;
        if (_bankList.SelectedIndex < 0) return false;
        int slot = _bankList.SelectedIndex + 1;
        var bank = state.Bank[slot];
        if (bank.Num <= 0 || bank.Num > state.Limits.Items) return false;
        itemNum = bank.Num;
        return true;
    }

    // Keeps the [Sort] link right-justified in the bank column's 18px header strip, mirroring the
    // inventory panel's sort link. Called from Update (hit-test) and Draw (render); cheap and idempotent.
    private void PositionSortLink(Rectangle right, SpriteFont font)
    {
        _sortLink.Label = ClientStrings.Get(ClientStrings.Common_SortHeader);
        int w = (int)Math.Ceiling(Link.MeasureSize(font, _sortLink.Label).X);
        _sortLink.Bounds = new Rectangle(right.Right - HeaderPadLeft - w, right.Y, w, ListHeaderH);
    }

    private static (Rectangle left, Rectangle right) SplitContent(Rectangle c)
    {
        int half = c.Width / 2;
        return (new Rectangle(c.X, c.Y, half, c.Height),
                new Rectangle(c.X + half, c.Y, c.Width - half, c.Height));
    }

    private static Rectangle ColumnButton(Rectangle col) =>
        new(col.X + UiHelper.PanelButtonEdgePad,
            col.Bottom - UiHelper.PanelButtonRowBottomPad,
            col.Width - 2 * UiHelper.PanelButtonEdgePad,
            UiHelper.PanelButtonHeight);

    private static Rectangle ListBoundsOf(Rectangle col) =>
        new(col.X + ListInset, col.Y + ListHeaderH, col.Width - ListInsetW, Math.Max(0, col.Height - ListFootH));
}
