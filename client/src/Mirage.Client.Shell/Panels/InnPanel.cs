using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Small floating panel shown when the player presses Rest on an Inn map.
/// Offers Set Spawn (with a gold-cost confirm overlay) and Access Bank.
/// </summary>
public sealed class InnPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 300, 120), minH: 120, minW: 290);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    private readonly Button _setSpawnBtn = new();
    private readonly Button _accessBankBtn = new();
    private readonly Button _marketBtn = new();
    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    private int _labelsGeneration = -1;

    private bool _confirmingSetSpawn;
    private int _spawnCost;
    private InputState _input = new();

    private const int ContentTopInset = 8;
    private const int OverlayLineSpacing = 18;

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (!IsOpen)
            _confirmingSetSpawn = false;
    }

    /// <summary>Explicitly raise the panel (an NPC interact opened a keeper inn) — mirrors ShopPanel.Open.</summary>
    public void Open()
    {
        IsOpen = true;
        _confirmingSetSpawn = false;
    }

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            _confirmingSetSpawn = false;
            return;
        }

        var c = _panel.ContentBounds;

        if (_confirmingSetSpawn)
        {
            _confirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
            _cancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);

            if (_confirmBtn.IsClicked(input))
            {
                sender.SendConfirmSetSpawn();
                _confirmingSetSpawn = false;
            }
            if (_cancelBtn.IsClicked(input))
                _confirmingSetSpawn = false;
        }
        else
        {
            // Three options on one row (Marketplace is available at any inn).
            _setSpawnBtn.Bounds = UiHelper.PanelBottomButton(c, 0, 3);
            _accessBankBtn.Bounds = UiHelper.PanelBottomButton(c, 1, 3);
            _marketBtn.Bounds = UiHelper.PanelBottomButton(c, 2, 3);

            int shopNum = state.ActiveInnShopNum;
            bool allowBanking = shopNum > 0 && shopNum < state.ShopDefs.Length
                && state.ShopDefs[shopNum].ShopType == ShopType.Inn
                && state.ShopDefs[shopNum].AllowBanking;
            _accessBankBtn.Enabled = allowBanking;

            if (_setSpawnBtn.IsClicked(input))
            {
                _spawnCost = (int)EconomyFormulas.InnSpawnCost(state.Me.Level);
                _confirmingSetSpawn = true;
            }

            if (_accessBankBtn.IsClicked(input))
                sender.SendBankOpen();

            if (_marketBtn.IsClicked(input))
                sender.SendMarketOpen();
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _setSpawnBtn.Label = ClientStrings.Get(ClientStrings.InnPanel_SetSpawnButton);
            _accessBankBtn.Label = ClientStrings.Get(ClientStrings.InnPanel_AccessBankButton);
            _marketBtn.Label = ClientStrings.Get(ClientStrings.InnPanel_MarketplaceButton);
            _confirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }
        int shopNum = state.ActiveInnShopNum;
        string innName = shopNum > 0 && shopNum < state.ShopDefs.Length
            ? (state.ShopDefs[shopNum]?.Name?.TrimEnd() ?? string.Empty)
            : string.Empty;
        _panel.Draw(sb, font, innName, isActive);
        var c = _panel.ContentBounds;

        if (_confirmingSetSpawn)
        {
            long playerGold = GetPlayerGold(state);

            UiHelper.DrawLabelCentered(sb, font, ClientStrings.Get(ClientStrings.InnPanel_SetSpawnPrompt), c.X, c.Y + ContentTopInset, c.Width, UiHelper.DlgLabelColor);
            UiHelper.DrawLabelCentered(sb, font, ClientStrings.Format(ClientStrings.InnPanel_YourGoldLabel, ("Gold", playerGold)), c.X, c.Y + ContentTopInset + OverlayLineSpacing, c.Width, Color.White);
            UiHelper.DrawLabelCentered(sb, font, ClientStrings.Format(ClientStrings.InnPanel_CostLabel, ("Cost", _spawnCost)), c.X, c.Y + ContentTopInset + OverlayLineSpacing * 2, c.Width, Color.White);

            _confirmBtn.Draw(sb, font, _input);
            _cancelBtn.Draw(sb, font, _input);
        }
        else
        {
            bool allowBanking = shopNum > 0 && shopNum < state.ShopDefs.Length
                && state.ShopDefs[shopNum].ShopType == ShopType.Inn
                && state.ShopDefs[shopNum].AllowBanking;
            _accessBankBtn.Enabled = allowBanking;

            UiHelper.DrawLabelCentered(sb, font, ClientStrings.Get(ClientStrings.InnPanel_MainPrompt), c.X, c.Y + ContentTopInset, c.Width, UiHelper.DlgLabelColor);

            _setSpawnBtn.Draw(sb, font, _input);
            _accessBankBtn.Draw(sb, font, _input);
            _marketBtn.Draw(sb, font, _input);
        }
        _panel.DrawOverlay(sb);
    }

    private static long GetPlayerGold(ClientState state)
    {
        if (state.Me?.Inv is null) return 0;
        long total = 0;
        for (int i = 1; i < state.Me.Inv.Length; i++)
        {
            var slot = state.Me.Inv[i];
            if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) continue;
            if (state.Items[slot.Num]?.Type == ItemType.Currency)
                total += slot.Quantity;
        }
        return total;
    }
}
