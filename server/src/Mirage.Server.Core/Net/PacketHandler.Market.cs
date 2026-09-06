using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>The marketplace: opening the board, listing, buying, cancelling and refreshing.</summary>
public sealed partial class PacketHandler
{
    //  Marketplace handlers
    // ===========================================================================

    // ── Marketplace ────────────────────────────────────────────────────────────

    private void HandleMarketOpen(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Open(index);
    }

    private void HandleMarketCreate(int index, MarketCreatePacket p)
    {
        if (!IsActing(index)) return;
        _market.List(index, p.InvSlot, p.Quantity, p.Price);
    }

    private void HandleMarketBuy(int index, MarketBuyPacket p)
    {
        if (!IsActing(index)) return;
        _market.Buy(index, p.Id, p.Quantity);
    }

    private void HandleMarketCancel(int index, MarketCancelPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Cancel(index, p.Id);
    }

    private void HandleMarketRefresh(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Refresh(index);
    }

    // No IsPlaying gate: a closing panel should always clear the viewer flag (harmless if already gone).
    private void HandleMarketClose(int index) => _market.Close(index);
}
