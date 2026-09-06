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

/// <summary>Map delivery and region sync — the client asking for the map it just entered, a
/// neighbor it lacks, or a full re-sync after a seamless crossing.</summary>
public sealed partial class PacketHandler
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  Map loading handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleRequestNewMap(int index)
    {
        var sp = _pm[index];
        if (!sp.IsConnected || sp.Login == "") return;

        int mapNum = sp.Char.Map;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;

        _dispatcher.SendTo(index, PacketBuilder.SendMap(mapNum, _world.Maps[mapNum]));
        _joinLeave.SendJoinData(index);
    }

    private void HandleMapDataClient(int index)
    {
        var sp = _pm[index];
        if (!sp.IsConnected || sp.Login == "") return;
        _joinLeave.SendJoinData(index);
    }

    // Serves a single neighbor map the client lacks in its cache.  Honors the requested
    // mapNum (unlike the center flow), but only if it is genuinely one of the player's
    // currently observable neighbors at the claimed cell — prevents arbitrary map scraping
    // and silently drops stale requests from a player who has since moved.
    private void HandleNeedNeighborMap(int index, NeedNeighborMapPacket p)
    {
        var sp = _pm[index];
        if (!sp.IsConnected || sp.Login == "") return;
        if (p.MapNum <= 0 || p.MapNum > _world.Limits.Maps) return;
        if (p.Col is < 0 or > 2 || p.Row is < 0 or > 2) return;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, sp.Char.Map);
        if (grid[p.Col, p.Row] != p.MapNum) return;
        _dispatcher.SendTo(index, PacketBuilder.SendMap(p.MapNum, _world.Maps[p.MapNum], p.Col, p.Row));
    }

    // After a seamless crossing the client shifted its grid and asks to be re-synced for the new
    // center.  Non-blocking — re-sends the now-current region (players, entities, neighbor maps).
    private void HandleRequestRegionSync(int index)
    {
        if (!_pm[index].IsPlaying || _pm[index].GettingMap) return;
        _joinLeave.SendRegionSync(index);
    }
}
