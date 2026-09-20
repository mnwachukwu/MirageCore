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

/// <summary>Guild lifecycle and membership: founding and disbanding, offers and applications, ranks and transfers, and the guild's own settings and chat.</summary>
public sealed partial class PacketHandler
{
    //  Guild handlers
    // ===========================================================================

    private void HandleGuildCreate(int index, GuildCreatePacket p)
    {
        if (!IsActing(index)) return;
        string name = p.Name.Trim();
        // Name-format validation here (mirrors character creation); the guild business rules
        // (eligibility, name uniqueness, funds) are enforced in GuildSystem.CreateGuild. Max counts the whole
        // string (underscores included); min counts alphanumerics only (rejects "A__" / all-underscore).
        switch (NameRules.CheckLength(name, Constants.MinFieldLength, Constants.NameLength))
        {
            case NameLengthResult.TooLong:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.Guild_NameLength,
                    new ChatMetadata(GameColor.BrightRed, ChatChannel.System), ("Max", Constants.NameLength));
                return;
            case NameLengthResult.TooShort:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.Guild_NameNeedsAlnum,
                    new ChatMetadata(GameColor.BrightRed, ChatChannel.System), ("Min", Constants.MinFieldLength));
                return;
        }
        if (!IsValidName(name))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Auth_InvalidName,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }
        _guilds.CreateGuild(index, name);
    }

    private void HandleGuildDisband(int index, GuildDisbandPacket p)
    {
        if (!IsActing(index)) return;
        _guilds.DisbandGuild(index);
    }

    private void HandleGuildOfferInitiate(int index, GuildOfferInitiatePacket p)
    {
        if (!IsActing(index)) return;
        string target = p.TargetName.Trim();
        if (target.Length == 0) return;
        if (p.IsRequest) _guilds.RequestJoin(index, target);
        else _guilds.Invite(index, target);
    }

    private void HandleGuildOfferRespond(int index, GuildOfferRespondPacket p)
    {
        if (!IsActing(index)) return;
        if (p.Accept) _guilds.AcceptOffer(index);
        else _guilds.DeclineOffer(index);
    }

    private void HandleGuildSetOpen(int index, GuildSetOpenPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetOpenForMembership(index, p.Open);
    }

    private void HandleGuildSetShowRank(int index, GuildSetShowRankPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetShowRankOverhead(index, p.Show);
    }

    private void HandleGuildLeave(int index, GuildLeavePacket p)
    {
        if (!IsActing(index)) return;
        _guilds.LeaveGuild(index);
    }

    private void HandleGuildKick(int index, GuildKickPacket p)
    {
        if (!IsActing(index)) return;
        _guilds.KickMember(index, p.Login.Trim());
    }

    private void HandleGuildPromote(int index, GuildPromotePacket p)
    {
        if (!IsActing(index)) return;
        _guilds.PromoteMember(index, p.Login.Trim());
    }

    private void HandleGuildDemote(int index, GuildDemotePacket p)
    {
        if (!IsActing(index)) return;
        _guilds.DemoteMember(index, p.Login.Trim());
    }

    private void HandleGuildTransfer(int index, GuildTransferPacket p)
    {
        if (!IsActing(index)) return;
        _guilds.InitiateTransfer(index, p.Login.Trim());
    }

    private void HandleGuildSetMotd(int index, GuildSetMotdPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string motd = p.Motd.Trim();
        if (motd.Length > Constants.GuildMotdMaxLength) motd = motd[..Constants.GuildMotdMaxLength];
        if (motd.Length > 0 && !TextValidation.IsValidText(motd))
        {
            HackingAttempt(index, "Guild MOTD Modification");
            return;
        }
        _guilds.SetMotd(index, motd);
    }

    private void HandleGuildSetLabels(int index, GuildSetLabelsPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetLabels(index, p.Labels);
    }

    private void HandleGuildSetColor(int index, GuildSetColorPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetColor(index, p.Rgb);
    }

    private void HandleGuildChat(int index, GuildChatPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string msg = p.Msg.Trim();
        if (msg.Length == 0) return;
        if (!TextValidation.IsValidText(msg))
        {
            HackingAttempt(index, "Guild Chat Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;
        _guilds.GuildChat(index, msg, p.Officer);
    }

    private void HandleGuildBrowseRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.Browse(index);
    }

    private void HandleGuildApply(int index, GuildApplyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.Apply(index, p.Index);
    }

    private void HandleGuildReviewApplication(int index, GuildReviewApplicationPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.ReviewApplication(index, p.Login.Trim(), p.Accept);
    }

    private void HandleGuildInfoRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SendGuildInfo(index);
    }
}
