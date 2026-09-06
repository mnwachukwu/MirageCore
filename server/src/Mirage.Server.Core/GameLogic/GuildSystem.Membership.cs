using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>How players get in and out: the open-guild browser and application queue, invites,
/// join requests, leadership transfer, and the offer bookkeeping that backs them.</summary>
public sealed partial class GuildSystem : GameSystem
{
    // ── Discovery (open-guild browser + applications) ──────────────────────────────

    /// <summary>Send a guildless player the list of open-for-membership guilds, sorted by name.</summary>
    public void Browse(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var list = new List<GuildBrowseEntry>();
        foreach (var g in _world.Guilds.Values)
        {
            if (g.OpenForMembership)
            {
                list.Add(new GuildBrowseEntry
                {
                    Index = g.Index,
                    Name = g.Name,
                    Level = g.Level,
                    Members = g.Members.Count,
                    Labels = new List<GuildLabel>(g.Labels),
                });
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _dispatcher.SendTo(index, new GuildBrowsePacket { Guilds = list });
    }

    /// <summary>A guildless player applies to an open guild — held as a pending application (offline-safe).
    /// Online officers are nudged; the applicant learns the outcome by mail when it's reviewed.</summary>
    public void Apply(int index, int guildIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (sp.Char.Access > AdminLevel.Player)
        {
            Notify(index, ServerStrings.Guild_AdminCannotJoin);
            return;
        }
        if (sp.Guild != 0)
        {
            Notify(index, ServerStrings.Guild_AlreadyInOne);
            return;
        }
        var guild = GuildById(guildIndex);
        if (guild is null || !guild.OpenForMembership)
        {
            Notify(index, ServerStrings.Guild_NotOpen);
            return;
        }
        if (guild.Applications.Any(a => string.Equals(a, sp.Login, StringComparison.OrdinalIgnoreCase)))
        {
            Notify(index, ServerStrings.Guild_AlreadyApplied, ("GuildName", guild.Name));
            return;
        }
        if (guild.Applications.Count >= Constants.MaxGuildApplications)
        {
            Notify(index, ServerStrings.Guild_ApplicationsFull, ("GuildName", guild.Name));
            return;
        }

        guild.Applications.Add(sp.Login);
        SaveGuild(guild);   // persists + re-pushes GuildInfo so online officers see the new application
        NotifyOk(index, ServerStrings.Guild_ApplicationSent, ("GuildName", guild.Name));
        _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, ServerStrings.Guild_ApplicationReceived,
            new ChatMetadata(GameColor.GuildOfficer, ChatChannel.GuildOfficer), ("Name", sp.Char.TrimmedName));
    }

    /// <summary>Leader/Officer approves or rejects a pending application. Approve adds the applicant
    /// (online or offline); either way the outcome is mailed to them and they leave every guild's queue.</summary>
    public void ReviewApplication(int reviewerIndex, string applicantLogin, bool accept)
    {
        var reviewer = _pm[reviewerIndex];
        if (!reviewer.IsPlaying) return;
        var guild = GuildOf(reviewer);
        if (guild is null)
        {
            Notify(reviewerIndex, ServerStrings.Guild_NotInOne);
            return;
        }
        if (reviewer.GuildRank < GuildRank.Officer)
        {
            Notify(reviewerIndex, ServerStrings.Guild_NeedOfficer);
            return;
        }

        int i = guild.Applications.FindIndex(a => string.Equals(a, applicantLogin, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;   // stale / already handled by another officer
        string login = guild.Applications[i];
        string sender = ServerStrings.Get(ServerStrings.Mail_SystemSender);

        if (accept)
        {
            RemoveApplicationEverywhere(login);   // clears this guild too + any other guild they applied to
            AddApplicantToGuild(guild, login);
            _mail.Deliver(login, sender,
                ServerStrings.Get(ServerStrings.Guild_MailApprovedSubject),
                ServerStrings.Format(ServerStrings.Guild_MailApprovedBody, ("GuildName", guild.Name)));
        }
        else
        {
            guild.Applications.RemoveAt(i);
            SaveGuild(guild);
            _mail.Deliver(login, sender,
                ServerStrings.Get(ServerStrings.Guild_MailRejectedSubject),
                ServerStrings.Format(ServerStrings.Guild_MailRejectedBody, ("GuildName", guild.Name)));
        }
    }

    // Add an approved applicant: online + still guildless → the normal join; else set account membership
    // + a roster row offline (their character snapshot fills in on next login via RefreshMemberSnapshot).
    private void AddApplicantToGuild(GuildRecord guild, string login)
    {
        int idx = _pm.FindOnlineByLogin(login);
        if (idx != 0)
        {
            if (_pm[idx].Guild == 0) JoinGuild(idx, guild);   // JoinGuild persists + broadcasts
            else SaveGuild(guild);
            return;
        }
        _saver.MutateAccountInBackground(login, a => { if (a.Guild == 0) { a.Guild = guild.Index; a.GuildRank = GuildRank.Member; } });
        if (FindMember(guild, login) is null)
            guild.Members.Add(new GuildMember { Login = login, Rank = GuildRank.Member });
        SaveGuild(guild);
    }

    // Remove a login from EVERY guild's application queue (so an approve can't be double-honored by another
    // guild the same player applied to), saving each guild that actually changed.
    private void RemoveApplicationEverywhere(string login)
    {
        foreach (var g in _world.Guilds.Values)
        {
            if (g.Applications.RemoveAll(a => string.Equals(a, login, StringComparison.OrdinalIgnoreCase)) > 0)
                SaveGuild(g);
        }
    }

    // ── Invite / request / join ─────────────────────────────────────────────────

    /// <summary>An Officer+ invites a guildless online player to their guild — sets a pending offer
    /// on the target for them to accept.</summary>
    public void Invite(int inviterIndex, string targetName)
    {
        var inviter = _pm[inviterIndex];
        if (!inviter.IsPlaying) return;
        var guild = GuildOf(inviter);
        if (guild is null)
        {
            Notify(inviterIndex, ServerStrings.Guild_NotInOne);
            return;
        }
        if (inviter.GuildRank < GuildRank.Officer)
        {
            Notify(inviterIndex, ServerStrings.Guild_NeedOfficer);
            return;
        }

        int targetIndex = _pm.FindPlayerByName(targetName);
        if (targetIndex == 0 || targetIndex == inviterIndex)
        {
            Notify(inviterIndex, ServerStrings.Guild_PlayerNotOnline);
            return;
        }
        var target = _pm[targetIndex];
        if (target.Char.Access > AdminLevel.Player)
        {
            Notify(inviterIndex, ServerStrings.Guild_AdminCannotJoin);
            return;
        }
        if (target.Guild != 0)
        {
            Notify(inviterIndex, ServerStrings.Guild_TargetInGuild);
            return;
        }

        SetOffer(target, guild.Index, inviter.Login, GuildOfferKind.Invite);
        _dispatcher.SendTo(targetIndex, new GuildOfferNotifyPacket
        { GuildName = guild.Name, OtherName = inviter.Char.TrimmedName, Kind = GuildOfferKind.Invite });
        NotifyOk(inviterIndex, ServerStrings.Guild_InviteSent, ("Name", target.Char.TrimmedName));
    }

    /// <summary>A guildless player asks an Officer+ (<paramref name="targetName"/>) of an OPEN guild
    /// to let them join — sets a pending offer on that officer for them to approve.</summary>
    public void RequestJoin(int requesterIndex, string targetName)
    {
        var requester = _pm[requesterIndex];
        if (!requester.IsPlaying) return;
        if (requester.Char.Access > AdminLevel.Player)
        {
            Notify(requesterIndex, ServerStrings.Guild_AdminCannotJoin);
            return;
        }
        if (requester.Guild != 0)
        {
            Notify(requesterIndex, ServerStrings.Guild_AlreadyInOne);
            return;
        }

        int targetIndex = _pm.FindPlayerByName(targetName);
        if (targetIndex == 0 || targetIndex == requesterIndex)
        {
            Notify(requesterIndex, ServerStrings.Guild_PlayerNotOnline);
            return;
        }
        var target = _pm[targetIndex];
        var guild = GuildOf(target);
        if (guild is null || target.GuildRank < GuildRank.Officer)
        {
            Notify(requesterIndex, ServerStrings.Guild_TargetNotOfficer);
            return;
        }
        if (!guild.OpenForMembership)
        {
            Notify(requesterIndex, ServerStrings.Guild_NotOpen);
            return;
        }

        SetOffer(target, guild.Index, requester.Login, GuildOfferKind.Request);
        _dispatcher.SendTo(targetIndex, new GuildOfferNotifyPacket
        { GuildName = guild.Name, OtherName = requester.Char.TrimmedName, Kind = GuildOfferKind.Request });
        NotifyOk(requesterIndex, ServerStrings.Guild_RequestSent, ("GuildName", guild.Name));
    }

    /// <summary>Accept the pending guild offer: a received invite → I join; a request I was asked to
    /// approve → the requester joins.</summary>
    public void AcceptOffer(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (!HasLiveOffer(sp))
        {
            Notify(index, ServerStrings.Guild_NoOffer);
            ClearOffer(sp);
            return;
        }

        int guildId = sp.GuildOfferGuild;
        string other = sp.GuildOfferOther;
        GuildOfferKind kind = sp.GuildOfferKind;
        ClearOffer(sp);

        var guild = GuildById(guildId);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_OfferGone);
            return;
        }

        switch (kind)
        {
            case GuildOfferKind.Request:
            {
                // sp is the officer approving; the requester (other) joins — re-validate both sides.
                if (sp.Guild != guildId || sp.GuildRank < GuildRank.Officer)
                {
                    Notify(index, ServerStrings.Guild_OfferGone);
                    return;
                }
                int reqIndex = _pm.FindOnlineByLogin(other);
                if (reqIndex == 0 || _pm[reqIndex].Guild != 0)
                {
                    Notify(index, ServerStrings.Guild_RequesterGone);
                    return;
                }
                JoinGuild(reqIndex, guild);
                break;
            }
            case GuildOfferKind.Transfer:
            {
                // sp is the offered officer accepting leadership; the current leader (other) steps down.
                // Both must still be online and in the expected roles.
                if (sp.Guild != guildId || sp.GuildRank != GuildRank.Officer)
                {
                    Notify(index, ServerStrings.Guild_OfferGone);
                    return;
                }
                var leaderMember = FindMember(guild, other);
                if (leaderMember is null || leaderMember.Rank != GuildRank.Leader || _pm.FindOnlineByLogin(other) == 0)
                {
                    Notify(index, ServerStrings.Guild_OfferGone);
                    return;
                }
                SetMemberRank(guild, other, GuildRank.Officer);
                SetMemberRank(guild, sp.Login, GuildRank.Leader);
                GuildNotice(guild.Index, ServerStrings.Guild_LeadershipTransferred, ("Name", sp.Char.TrimmedName));
                break;
            }
            default:   // Invite: sp is the invited player joining.
            {
                if (sp.Guild != 0)
                {
                    Notify(index, ServerStrings.Guild_AlreadyInOne);
                    return;
                }
                JoinGuild(index, guild);
                break;
            }
        }
    }

    /// <summary>Decline the pending guild offer (just clears it).</summary>
    public void DeclineOffer(int index)
    {
        var sp = _pm[index];
        if (sp.IsPlaying) ClearOffer(sp);
    }

    /// <summary>Leader offers leadership to an online officer (by account login) — sets a pending
    /// transfer offer on that officer to accept. Both must be online.</summary>
    public void InitiateTransfer(int leaderIndex, string targetLogin)
    {
        var leader = _pm[leaderIndex];
        if (!leader.IsPlaying) return;
        var guild = GuildOf(leader);
        if (guild is null)
        {
            Notify(leaderIndex, ServerStrings.Guild_NotInOne);
            return;
        }
        if (leader.GuildRank != GuildRank.Leader)
        {
            Notify(leaderIndex, ServerStrings.Guild_NeedLeader);
            return;
        }

        var member = FindMember(guild, targetLogin);
        if (member is null || member.Rank != GuildRank.Officer)
        {
            Notify(leaderIndex, ServerStrings.Guild_TransferNeedsOfficer);
            return;
        }
        int targetIndex = _pm.FindOnlineByLogin(targetLogin);
        if (targetIndex == 0)
        {
            Notify(leaderIndex, ServerStrings.Guild_PlayerNotOnline);
            return;
        }

        SetOffer(_pm[targetIndex], guild.Index, leader.Login, GuildOfferKind.Transfer);
        _dispatcher.SendTo(targetIndex, new GuildOfferNotifyPacket
        { GuildName = guild.Name, OtherName = leader.Char.TrimmedName, Kind = GuildOfferKind.Transfer });
        NotifyOk(leaderIndex, ServerStrings.Guild_TransferOffered, ("Name", member.CharName));
    }

    // Add an (already-validated guildless) player to a guild as a Member: set membership + mirror,
    // sync the roster cache, persist, and announce to the guild.
    private void JoinGuild(int index, GuildRecord guild)
    {
        var sp = _pm[index];
        sp.Guild = guild.Index;
        sp.GuildRank = GuildRank.Member;
        _saver.MutateAccountInBackground(sp.Login, a => { a.Guild = guild.Index; a.GuildRank = GuildRank.Member; });

        guild.Members.Add(new GuildMember
        {
            Login = sp.Login,
            Rank = GuildRank.Member,
            LastSeenUtc = 0,   // no logout recorded yet; online-ness is derived live
            CharName = sp.Char.TrimmedName,
            CharClass = sp.Char.Class,
            CharLevel = sp.Char.Level,
        });
        SaveGuild(guild);
        BroadcastPlayerGuild(index);

        _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_MemberJoined,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild),
            ("Name", sp.Char.TrimmedName), ("GuildName", guild.Name));
        _logger.LogInformation("{Player} joined guild {Guild} (#{Id}).", sp.Char.TrimmedName, guild.Name, guild.Index);
    }

    private static void SetOffer(ServerPlayer target, int guildId, string otherLogin, GuildOfferKind kind)
    {
        target.GuildOfferGuild = guildId;
        target.GuildOfferOther = otherLogin;
        target.GuildOfferKind = kind;
        target.GuildOfferExpiresAt = Environment.TickCount64 + Constants.GuildInviteTimeoutSeconds * 1000L;
    }

    private static bool HasLiveOffer(ServerPlayer sp) =>
        sp.GuildOfferGuild != 0 && sp.GuildOfferExpiresAt != 0 && Environment.TickCount64 < sp.GuildOfferExpiresAt;

    private static void ClearOffer(ServerPlayer sp)
    {
        sp.GuildOfferGuild = 0;
        sp.GuildOfferOther = "";
        sp.GuildOfferKind = GuildOfferKind.Invite;
        sp.GuildOfferExpiresAt = 0;
    }
}
