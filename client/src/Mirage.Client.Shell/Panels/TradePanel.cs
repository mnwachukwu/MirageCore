using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The direct-trade window: a live two-party session opened when a trade is accepted (server-driven via
/// <see cref="ClientState.TradeActive"/>). Movement-locks like the bank. Shows both sides' staged offers and
/// confirm state; you stage items from your inventory (escrowed server-side; a currency prompts for an amount),
/// pull them back, then Confirm. When both parties are confirmed the server executes the atomic swap and closes
/// the window. Closing or Cancel returns everything.
/// </summary>
public sealed class TradePanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 470, 360), minH: 300, minW: 440);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);
    public bool IsCapturingInput => IsOpen && _amountPrompt.IsOpen;

    public void Open()
    {
        IsOpen = true;
        _lastVersion = -1;
        _amountPrompt.Close();
    }
    public void Close()
    {
        IsOpen = false;
        _amountPrompt.Close();
        Tooltip.CloseScope(TooltipScope);
    }

    // ShowTruncationTooltip off on all three: each row installs the richer item tooltip below, and both
    // notifiers write the same Tooltip singleton. With truncation left on, a narrow offer row registers its
    // text tooltip and the item tooltip immediately replaces it — every frame — so the tooltip re-pins to the
    // cursor on each one instead of staying where it first appeared.
    private readonly ListBox _myOffer = new() { ShowTruncationTooltip = false };
    private readonly ListBox _theirOffer = new() { ShowTruncationTooltip = false };
    private readonly ListBox _invList = new() { ShowTruncationTooltip = false };
    private readonly List<int> _invSlots = new();
    private readonly Button _offerBtn = new();
    private readonly Button _removeBtn = new();
    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    private readonly NumberPromptDialog _amountPrompt = new();
    private int _lastVersion = -1;
    private int _labelsGeneration = -1;
    private InputState _input = new();
    private Rectangle _myRect, _theirRect, _invRect;

    private const int ButtonH = 26, Gap = 6, LabelH = 14, BtnW = 84;
    private const string TooltipScope = "Trade";
    private static readonly Color ConfirmedColor = new(70, 120, 70);

    // ── Update ───────────────────────────────────────────────────────────────────

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        if (!IsOpen) return;
        _input = input;
        long nowMs = Environment.TickCount64;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            sender.SendTradeCancel();
            return;
        }  // closing the window cancels the trade

        var c = _panel.ContentBounds;
        Layout(c);

        if (_amountPrompt.IsOpen)
        {
            _amountPrompt.Update(input, c, nowMs);
            return;
        }

        SyncOffers(state);
        _myOffer.Update(input, _myRect, keyboardActive: false);
        _theirOffer.Update(input, _theirRect, keyboardActive: false);
        RebuildInvList(state);
        _invList.Update(input, _invRect, keyboardActive: false);

        _offerBtn.Enabled = _invList.SelectedIndex >= 0 && _invList.SelectedIndex < _invSlots.Count;
        if (_offerBtn.Enabled && _offerBtn.IsClicked(input)) OfferSelected(state, sender);

        _removeBtn.Enabled = _myOffer.SelectedIndex >= 0 && _myOffer.SelectedIndex < state.TradeMine.Count;
        if (_removeBtn.Enabled && _removeBtn.IsClicked(input))
        {
            sender.SendTradeOfferRemove(_myOffer.SelectedIndex);
            _myOffer.SelectedIndex = -1;
        }

        if (_confirmBtn.IsClicked(input)) sender.SendTradeConfirm(!state.TradeMyConfirmed);
        if (_cancelBtn.IsClicked(input)) sender.SendTradeCancel();
    }

    private void OfferSelected(ClientState state, ClientPacketSender sender)
    {
        int slot = _invSlots[_invList.SelectedIndex];
        var invSlot = state.Me?.Inv?[slot];
        if (invSlot is null || invSlot.Num <= 0) return;
        var item = state.Items[invSlot.Num];
        if (item?.Type == ItemType.Currency)
        {
            _amountPrompt.Open(ClientStrings.Get(ClientStrings.TradePanel_OfferButton), item.Name?.TrimEnd() ?? "", invSlot.Quantity,
                amt => sender.SendTradeOfferAdd(slot, amt));
        }
        else
        {
            sender.SendTradeOfferAdd(slot, 0);
        }
    }

    // ── Draw ─────────────────────────────────────────────────────────────────────

    /// <param name="canHover">False when another panel sits over the mouse, so a hovered row here cannot
    /// push its tooltip through the window on top of it. Every other panel with row tooltips takes this.</param>
    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex,
                     bool isActive = false, bool canHover = true)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        long nowMs = Environment.TickCount64;

        _panel.Draw(sb, font, ClientStrings.Format(ClientStrings.TradePanel_TitleFormat, ("Name", state.TradePartner)), isActive);
        var c = _panel.ContentBounds;
        Layout(c);
        SyncOffers(state);
        RebuildInvList(state);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.TradePanel_YourOffer), new Vector2(_myRect.X, _myRect.Y - LabelH), Color.LightGray, _myRect.Width);
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.TradePanel_TheirOfferFormat, ("Name", state.TradePartner)), new Vector2(_theirRect.X, _theirRect.Y - LabelH), Color.LightGray, _theirRect.Width);
        _myOffer.Draw(sb, font, _myRect);
        _theirOffer.Draw(sb, font, _theirRect);

        DrawStatus(sb, font, _myRect, ClientStrings.Get(ClientStrings.TradePanel_YouLabel), state.TradeMyConfirmed);
        DrawStatus(sb, font, _theirRect, state.TradePartner, state.TradeTheirConfirmed);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.TradePanel_YourInventory), new Vector2(_invRect.X, _invRect.Y - LabelH), Color.LightGray, _invRect.Width);
        _invList.Draw(sb, font, _invRect);

        if (canHover && itemsTex is not null && !_amountPrompt.IsOpen) ShowTooltips(state, itemsTex);

        _confirmBtn.Label = ClientStrings.Get(state.TradeMyConfirmed ? ClientStrings.TradePanel_Unconfirm : ClientStrings.TradePanel_Confirm);
        _offerBtn.Draw(sb, font, _input);
        _removeBtn.Draw(sb, font, _input);
        _confirmBtn.Draw(sb, font, _input, normalColor: state.TradeMyConfirmed ? ConfirmedColor : (Color?)null);
        _cancelBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _amountPrompt.Draw(sb, font, c, nowMs);
        _panel.DrawOverlay(sb);
    }

    private static void DrawStatus(SpriteBatch sb, SpriteFont font, Rectangle offerRect, string who, bool confirmed)
    {
        string s = who + ": " + ClientStrings.Get(confirmed ? ClientStrings.TradePanel_StatusConfirmed : ClientStrings.TradePanel_StatusWaiting);
        UiHelper.DrawLabel(sb, font, s, new Vector2(offerRect.X, offerRect.Bottom + 2), confirmed ? Color.LightGreen : Color.Gray, offerRect.Width);
    }

    private void ShowTooltips(ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        if (_myOffer.HoveredIndex >= 0 && _myOffer.HoveredIndex < state.TradeMine.Count)
        {
            ShowItemTooltip(state, itemsTex, state.TradeMine[_myOffer.HoveredIndex], (TooltipScope, "mine", _myOffer.HoveredIndex));
        }
        else if (_theirOffer.HoveredIndex >= 0 && _theirOffer.HoveredIndex < state.TradeTheirs.Count)
        {
            ShowItemTooltip(state, itemsTex, state.TradeTheirs[_theirOffer.HoveredIndex], (TooltipScope, "theirs", _theirOffer.HoveredIndex));
        }
        else if (_invList.HoveredIndex >= 0 && _invList.HoveredIndex < _invSlots.Count)
        {
            var s = state.Me?.Inv?[_invSlots[_invList.HoveredIndex]];
            if (s is not null) ShowItemTooltip(state, itemsTex, s, (TooltipScope, "inv", _invSlots[_invList.HoveredIndex]));
        }
    }

    private void ShowItemTooltip(ClientState state, IReadOnlyList<Texture2D?> itemsTex, PlayerInvSlot it, object key)
    {
        if (it.Num <= 0 || it.Num >= state.Items.Length) return;
        var def = state.Items[it.Num];
        if (def is not null)
            Tooltip.NotifyHoverItem(TooltipScope, key, def, it, state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    // Rebuild both offer lists only when the server-pushed trade state changes.
    private void SyncOffers(ClientState state)
    {
        if (_lastVersion == state.TradeVersion) return;
        _lastVersion = state.TradeVersion;
        _myOffer.Items.Clear();
        foreach (var it in state.TradeMine) _myOffer.Items.Add(OfferLabel(state, it));
        _theirOffer.Items.Clear();
        foreach (var it in state.TradeTheirs) _theirOffer.Items.Add(OfferLabel(state, it));
    }

    private static string OfferLabel(ClientState state, PlayerInvSlot it)
    {
        var def = it.Num > 0 && it.Num < state.Items.Length ? state.Items[it.Num] : null;
        string name = def?.Name?.TrimEnd() ?? $"Item {it.Num}";
        return it.Quantity > 1 ? $"{name} ({it.Quantity:N0})" : name;
    }

    // Inventory offer candidates each frame: skip empty, equipped, and untradeable items (already-offered
    // items aren't here — they're escrowed off the bag). Selection survives the per-frame rebuild by slot.
    private void RebuildInvList(ClientState state) =>
        InventoryListBuilder.Rebuild(state, _invList, _invSlots, (_, item) => item?.NonTradeable == true);

    private void RefreshLabels()
    {
        _offerBtn.Label = ClientStrings.Get(ClientStrings.TradePanel_OfferButton);
        _removeBtn.Label = ClientStrings.Get(ClientStrings.TradePanel_RemoveButton);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }

    private void Layout(Rectangle c)
    {
        int halfW = (c.Width - 8 - Gap) / 2;
        int offerTop = c.Y + 4 + LabelH;
        const int offerH = 90, statusH = 16;
        _myRect = new Rectangle(c.X + 4, offerTop, halfW, offerH);
        _theirRect = new Rectangle(c.X + 4 + halfW + Gap, offerTop, halfW, offerH);

        int btnY = c.Bottom - ButtonH - 4;
        int invTop = _myRect.Bottom + statusH + Gap + LabelH;
        _invRect = new Rectangle(c.X + 4, invTop, c.Width - 8, Math.Max(0, btnY - Gap - invTop));

        _offerBtn.Bounds = new Rectangle(c.X + 4, btnY, BtnW, ButtonH);
        _removeBtn.Bounds = new Rectangle(_offerBtn.Bounds.Right + Gap, btnY, BtnW, ButtonH);
        _cancelBtn.Bounds = new Rectangle(c.Right - 4 - BtnW, btnY, BtnW, ButtonH);
        _confirmBtn.Bounds = new Rectangle(_cancelBtn.Bounds.Left - Gap - BtnW, btnY, BtnW, ButtonH);
    }
}
