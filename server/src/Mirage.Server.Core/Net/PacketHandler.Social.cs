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

/// <summary>Friends and ignores. Adds are addressed by character name and validated in SocialSystem
/// (must be online, not self); removes are by account login, straight off the row the client shows.</summary>
public sealed partial class PacketHandler
{
    //  Social handlers
    // ===========================================================================

    private void HandleSocialAddFriend(int index, SocialAddFriendPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Name))
        {
            HackingAttempt(index, "Friend Name Modification");
            return;
        }
        _social.AddFriend(index, p.Name.Trim());
    }

    private void HandleSocialAddIgnore(int index, SocialAddIgnorePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Name))
        {
            HackingAttempt(index, "Ignore Name Modification");
            return;
        }
        _social.AddIgnore(index, p.Name.Trim());
    }

    private void HandleSocialRemoveFriend(int index, SocialRemoveFriendPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _social.RemoveFriend(index, p.Login.Trim());
    }

    private void HandleSocialRemoveIgnore(int index, SocialRemoveIgnorePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _social.RemoveIgnore(index, p.Login.Trim());
    }
}
