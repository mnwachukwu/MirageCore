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

/// <summary>Party invites and membership.</summary>
public sealed partial class PacketHandler
{
    //  Party handlers
    // ===========================================================================

    private void HandlePartyRequest(int index, PartyRequestPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        int targetIndex = _pm.FindPlayerByName(p.Target);
        if (targetIndex == index) return;

        _party.SendPartyRequest(index, p.Target);
    }

    private void HandleJoinParty(int index, JoinPartyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (RefuseWhileDead(index)) return;
        _party.JoinParty(index);
    }

    private void HandleLeaveParty(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (RefuseWhileDead(index)) return;
        _party.LeaveParty(index);
    }
}
