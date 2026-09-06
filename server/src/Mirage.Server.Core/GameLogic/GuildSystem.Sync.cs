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

/// <summary>What the guild sees: the guild info payload, roster snapshots kept current as members
/// come and go, the rank/kick/promote/demote operations that reshape it, and the guild and officer
/// chat channels those notices share.</summary>
public sealed partial class GuildSystem : GameSystem
{
    // ── Client sync ──────────────────────────────────────────────────────────────

    // Push a player's current guild state to everyone observing their map by re-broadcasting their
    // PlayerData with the (otherwise-omitted) guild fields set, so context menus / overhead stay
    // current on a membership or open-flag change.
    private void BroadcastPlayerGuild(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        var pkt = PacketBuilder.PlayerData(index, sp.Char, sp.Char.Map,
            sp.PkGraceUntilUtc, sp.AggressorUntilUtcNow,
            sp.Guild, sp.GuildRank, guild?.Name ?? "", guild?.OpenForMembership ?? false, guild?.Color ?? 0,
            guild?.ShowRankOverhead ?? false, guild?.SeasonStanding ?? 0);
        SendToMap(_world, sp.Char.Map, pkt);
    }

    // Re-broadcast guild state for every online member (e.g. after the open-for-membership flag changes).
    private void BroadcastGuildMembers(int guildId)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[i].IsPlaying && _pm[i].Guild == guildId)
                BroadcastPlayerGuild(i);
        }
    }

    /// <summary>Push the Social panel's Guild-tab data (identity + roster) to one player. Guildless
    /// recipients get <c>InGuild = false</c> so the tab can show its create/browse on-ramp. The client
    /// also re-requests this when the tab opens, which is what keeps the roster's live online column
    /// honest without every login/logout having to fan out a broadcast.</summary>
    public void SendGuildInfo(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (GuildOf(sp) is not { } guild)
        {
            _dispatcher.SendTo(index, new GuildInfoPacket { InGuild = false });
            return;
        }

        _dispatcher.SendTo(index, new GuildInfoPacket
        {
            InGuild = true,
            Index = guild.Index,
            Name = guild.Name,
            Motd = guild.Motd,
            Labels = new List<GuildLabel>(guild.Labels),
            OpenForMembership = guild.OpenForMembership,
            ShowRankOverhead = guild.ShowRankOverhead,
            Color = guild.Color,
            Level = guild.Level,
            Exp = guild.Exp,
            VaultGold = guild.VaultGold,
            VaultValor = guild.VaultValor,
            PerksActive = guild.PerksActive,
            PendingIncomeTotal = PendingIncomeTotalOf(guild),
            WeeklyIncome = guild.WeeklyIncome,
            WeeklyDonations = guild.WeeklyDonations,
            WeeklyWarCosts = guild.WeeklyWarCosts,
            DaysUntilTax = ComputeDaysUntilTax(guild),
            MyRank = sp.GuildRank,
            Roster = BuildRoster(guild),
            Applications = new List<string>(guild.Applications),
            Quest = QuestView(guild),
            Wars = WarViews(guild),
            // Pending war-requests are the leadership queue — shown to Officer+ only (mirrors the
            // Guild Officer channel where the requests are nudged).
            WarRequests = sp.GuildRank >= GuildRank.Officer ? RequestViews(guild) : new List<GuildWarRequestView>(),
            Territories = TerritoryViews(guild.Index),
            RecentDonations = new List<GuildDonationEntry>(guild.RecentDonations),
            RecentSpending = new List<GuildSpendingEntry>(guild.RecentSpending),
        });
    }

    // Gold this guild has earned but not yet banked — what the next daily settlement will pay into the vault.
    // Two accumulators, because the two reach the vault by different routes: the L5 perk pot on the guild
    // record, and the pending income of every territory it controls.
    private long PendingIncomeTotalOf(GuildRecord guild)
    {
        long pending = guild.PendingPerkIncome;
        foreach (var (_, terr) in _world.AllTerritories())
            if (terr.ControllingGuild == guild.Index) pending += terr.PendingTerritoryIncome;
        return pending;
    }

    // Days (1-7) until this guild's next weekly tax settlement (charged on its founding weekday). Today counts
    // as 7 — today's 00:00 tax already ran — so the vault dashboard can show tax on its own founding-weekday
    // cadence, distinct from the season-week running totals.
    private int ComputeDaysUntilTax(GuildRecord guild)
    {
        int d = ((int)guild.FoundingWeekday - (int)DateOnly.FromDateTime(Clock.LocalNow).DayOfWeek + 7) % 7;
        return d == 0 ? 7 : d;
    }

    // Every territory (all guilds), alphabetical by display name, for the Territories sub-tab.
    // Owner is blank when unclaimed; Contesting lists the registered challengers; ChallengedByUs is per-viewer.
    private List<TerritoryView> TerritoryViews(int viewerGuildIndex)
    {
        var views = new List<TerritoryView>();
        foreach (var (g, terr) in _world.AllTerritories())
        {
            var challengerNames = terr.Challengers
                .Select(c => _world.Guilds.GetValueOrDefault(c)?.Name)
                .Where(n => !string.IsNullOrEmpty(n));
            // Last week's figure is public — a settled number saying what the land is worth, which is the
            // point of listing it. The live ones are not: pending income tracks a guild's hunting hour by
            // hour, so it goes only to the guild it belongs to.
            bool ours = viewerGuildIndex > 0 && terr.ControllingGuild == viewerGuildIndex;
            views.Add(new TerritoryView
            {
                Index = g.Index,
                Name = string.IsNullOrWhiteSpace(g.DisplayName) ? g.Name : g.DisplayName.Trim(),
                Owner = terr.ControllingGuild > 0 ? (_world.Guilds.GetValueOrDefault(terr.ControllingGuild)?.Name ?? "") : "",
                WeeksHeld = terr.WeeksHeld,
                PreviousWeekIncome = terr.PreviousWeekIncome,
                PendingTerritoryIncome = ours ? terr.PendingTerritoryIncome : 0,
                IncomeThisWeek = ours ? terr.IncomeThisWeek : 0,
                OwnedByUs = ours,
                Contesting = string.Join(", ", challengerNames),
                ChallengedByUs = terr.Challengers.Contains(viewerGuildIndex),
            });
        }
        views.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return views;
    }

    // The guild's active wars as client-facing rows, each with its status + tug-of-war meters resolved now.
    private List<GuildWarView> WarViews(GuildRecord guild)
    {
        long now = NowUtc;
        var views = new List<GuildWarView>(guild.Wars.Count);
        foreach (var war in guild.Wars)
        {
            var oppWar = GuildById(war.OpponentIndex) is { } opp ? GuildWarFormulas.Find(opp, guild.Index) : null;
            views.Add(new GuildWarView
            {
                OpponentIndex = war.OpponentIndex,
                OpponentName = war.OpponentName,
                Status = GuildWarFormulas.Status(war, now),
                GoLiveUtc = war.GoLiveUtc,
                DeclaredUtc = war.DeclaredUtc,
                // Daily maintenance for the war-tab detail: only a one-sided war we declared costs upkeep
                // (mutual waives it; a pure defender pays nothing).
                DailyCost = war.WeDeclared && !war.TheyDeclared ? GuildWarFormulas.DailyMaintenance(war.DeclareCost) : 0,
                Attrition = war.Attrition,
                OpponentAttrition = oppWar?.Attrition ?? 0,
                PeaceOfferedByUs = war.PeaceOfferedByUs,
                PeaceOfferedByThem = oppWar?.PeaceOfferedByUs ?? false,
                PeaceEscrowByUs = war.PeaceEscrow,
                PeaceEscrowByThem = oppWar?.PeaceEscrow ?? 0,
                AnteEscrow = war.AnteEscrow,
                WagerProposedByUs = war.WagerProposedByUs,
                WagerProposedByThem = oppWar?.WagerProposedByUs ?? 0,
                WagerDeadlineUtc = war.MutualSinceUtc > 0 ? war.MutualSinceUtc + Constants.GuildWarWagerWindowSeconds : 0,
            });
        }
        return views;
    }

    // Pending officer war-requests as client-facing rows for the Leader's review UI.
    private static List<GuildWarRequestView> RequestViews(GuildRecord guild)
    {
        var views = new List<GuildWarRequestView>(guild.WarRequests.Count);
        foreach (var r in guild.WarRequests)
        {
            views.Add(new GuildWarRequestView
            {
                Kind = r.Kind,
                TargetIndex = r.TargetIndex,
                TargetName = r.TargetName,
                RequesterName = r.RequesterName,
            });
        }

        return views;
    }

    // The active quest as a client-facing view (target mob name resolved), or null if none.
    private GuildQuestView? QuestView(GuildRecord guild)
    {
        if (guild.Quest is not { } q) return null;
        return new GuildQuestView
        {
            TargetNpc = q.Objective.Target,
            TargetNpcName = _world.Npcs[q.Objective.Target]?.TrimmedName ?? "",
            Count = q.Objective.Count,
            Progress = q.Objective.Progress,
            RewardExp = q.RewardExp,
            RewardGold = q.RewardGold,
            ExpiresUtc = q.ExpiresUtc,
        };
    }

    /// <summary>Re-send the Guild-tab data to every online member — call after a guild mutation so an
    /// open panel reflects it immediately.</summary>
    public void BroadcastGuildInfo(int guildId)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[i].IsPlaying && _pm[i].Guild == guildId)
                SendGuildInfo(i);
        }
    }

    // Roster rows ordered by rank (Leader first) then character level, both descending. An online
    // member's character fields come from their LIVE character (the cached snapshot can lag a level-up
    // or a character switch); an offline member falls back to the cache StampMemberLastSeen froze.
    private List<SocialEntry> BuildRoster(GuildRecord guild) =>
        guild.Members
            .Select(m =>
            {
                int idx = _pm.FindOnlineByLogin(m.Login);
                var live = idx != 0 ? _pm[idx].Char : null;
                return new SocialEntry
                {
                    Login = m.Login,
                    Rank = m.Rank,
                    Online = live is not null,
                    LastSeenUtc = m.LastSeenUtc,
                    CharName = live?.TrimmedName ?? m.CharName,
                    CharClass = live?.Class ?? m.CharClass,
                    CharLevel = live?.Level ?? m.CharLevel,
                };
            })
            .OrderByDescending(r => (int)r.Rank)
            .ThenByDescending(r => r.CharLevel)
            .ToList();

    // ── Roster snapshot maintenance ──────────────────────────────────────────────
    // A GuildMember row caches the account's most-recently-active character so the panel can render a
    // meaningful row for an OFFLINE member. The snapshot is seeded at join and kept current here.
    // Online-ness is deliberately NOT stored: it's derived live from FindOnlineByLogin when the roster
    // is built, so a server crash can't leave the file claiming everyone is online. LastSeenUtc
    // therefore always means "last logout" (0 = never recorded).

    /// <summary>Refresh the member's cached character snapshot — call when a character enters the
    /// world, so the roster tracks level-ups and character switches instead of freezing at join.</summary>
    public void RefreshMemberSnapshot(int index)
    {
        var sp = _pm[index];
        if (GuildOf(sp) is not { } guild || FindMember(guild, sp.Login) is not { } member) return;
        member.CharName = sp.Char.TrimmedName;
        member.CharClass = sp.Char.Class;
        member.CharLevel = sp.Char.Level;
        SaveGuild(guild);
    }

    /// <summary>Stamp the member's last-seen time, freeze their character snapshot, and accrue the
    /// finished session into their rolling active-member total — call when a character leaves the world
    /// (including a combat ghost: the account has disconnected either way).</summary>
    public void StampMemberLastSeen(int index)
    {
        var sp = _pm[index];
        if (GuildOf(sp) is not { } guild || FindMember(guild, sp.Login) is not { } member) return;
        long now = NowUtc;
        // Accrue this session into the rolling active-member total: add the session length, but RESET
        // first if the offline gap before this session exceeded the active window (a lapsed streak starts fresh).
        long sessionSecs = sp.SessionStartUtc > 0 && now > sp.SessionStartUtc ? now - sp.SessionStartUtc : 0;
        long gapBeforeSession = member.LastSeenUtc > 0 ? sp.SessionStartUtc - member.LastSeenUtc : long.MaxValue;
        member.ActiveSeconds = (gapBeforeSession > Constants.GuildActiveMemberWindowSeconds ? 0 : member.ActiveSeconds) + sessionSecs;
        member.LastSeenUtc = now;
        member.CharName = sp.Char.TrimmedName;
        member.CharClass = sp.Char.Class;
        member.CharLevel = sp.Char.Level;
        SaveGuild(guild);
    }

    /// <summary>On a player entering the world: tell observers about the joiner's guild, and tell the
    /// joiner about every player it can see (a fresh login isn't covered by change-broadcasts alone).</summary>
    public void SyncOnJoin(int index)
    {
        var joiner = _pm[index];
        if (!joiner.IsPlaying) return;
        RefreshMemberSnapshot(index);
        // Explicit: a GUILDLESS joiner never touches SaveGuild, so this is the only way their client
        // learns to show the create/browse on-ramp.
        SendGuildInfo(index);
        BroadcastPlayerGuild(index);
        foreach (int i in _world.MapObservers[joiner.Char.Map])
        {
            if (i == index || !_pm[i].IsPlaying) continue;
            var osp = _pm[i];
            var guild = GuildOf(osp);
            _dispatcher.SendTo(index, PacketBuilder.PlayerData(i, osp.Char, osp.Char.Map,
                osp.PkGraceUntilUtc, osp.AggressorUntilUtcNow,
                osp.Guild, osp.GuildRank, guild?.Name ?? "", guild?.OpenForMembership ?? false, guild?.Color ?? 0,
                guild?.ShowRankOverhead ?? false, guild?.SeasonStanding ?? 0));
        }
    }

    // ── Membership management ────────────────────────────────────────────────────

    /// <summary>Leave my own guild. A leader must transfer leadership or disband instead.</summary>
    public void LeaveGuild(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank == GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_LeaderCantLeave);
            return;
        }

        string who = sp.Char.TrimmedName;
        int guildId = guild.Index;
        string gname = guild.Name;
        RemoveFromGuild(guild, sp.Login);   // also clears sp's own membership + broadcasts
        GuildNotice(guildId, ServerStrings.Guild_MemberLeft, ("Name", who));
        NotifyOk(index, ServerStrings.Guild_YouLeft, ("GuildName", gname));
        _logger.LogInformation("{Player} left guild {Guild} (#{Id}).", who, gname, guildId);
    }

    /// <summary>Kick a member (by account login). Officer+, and never an equal-or-higher rank.</summary>
    public void KickMember(int index, string targetLogin)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank < GuildRank.Officer)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return;
        }

        var member = FindMember(guild, targetLogin);
        if (member is null)
        {
            Notify(index, ServerStrings.Guild_NotAMember);
            return;
        }
        if (string.Equals(member.Login, sp.Login, StringComparison.OrdinalIgnoreCase))
        {
            Notify(index, ServerStrings.Guild_CantKickSelf);
            return;
        }
        if (member.Rank >= sp.GuildRank)
        {
            Notify(index, ServerStrings.Guild_CantKickRank);
            return;
        }

        string kicked = member.CharName;
        int guildId = guild.Index;
        int tIdx = _pm.FindOnlineByLogin(member.Login);
        RemoveFromGuild(guild, member.Login);
        if (tIdx != 0) Notify(tIdx, ServerStrings.Guild_YouWereKicked, ("GuildName", guild.Name));
        GuildNotice(guildId, ServerStrings.Guild_MemberKicked, ("Name", kicked));
        _logger.LogInformation("{Kicker} kicked {Kicked} from guild {Guild} (#{Id}).",
            sp.Char.TrimmedName, kicked, guild.Name, guildId);
    }

    /// <summary>Promote a Member to Officer (Leader only).</summary>
    public void PromoteMember(int index, string targetLogin)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }

        var member = FindMember(guild, targetLogin);
        if (member is null)
        {
            Notify(index, ServerStrings.Guild_NotAMember);
            return;
        }
        if (member.Rank != GuildRank.Member)
        {
            Notify(index, ServerStrings.Guild_CantPromote);
            return;
        }

        SetMemberRank(guild, member.Login, GuildRank.Officer);
        int tIdx = _pm.FindOnlineByLogin(member.Login);
        if (tIdx != 0) NotifyOk(tIdx, ServerStrings.Guild_YouWerePromoted, ("GuildName", guild.Name));
        GuildNotice(guild.Index, ServerStrings.Guild_MemberPromoted, ("Name", member.CharName));
    }

    /// <summary>Demote an Officer to Member (Leader only).</summary>
    public void DemoteMember(int index, string targetLogin)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }

        var member = FindMember(guild, targetLogin);
        if (member is null)
        {
            Notify(index, ServerStrings.Guild_NotAMember);
            return;
        }
        if (member.Rank != GuildRank.Officer)
        {
            Notify(index, ServerStrings.Guild_CantDemote);
            return;
        }

        SetMemberRank(guild, member.Login, GuildRank.Member);
        int tIdx = _pm.FindOnlineByLogin(member.Login);
        if (tIdx != 0) NotifyOk(tIdx, ServerStrings.Guild_YouWereDemoted, ("GuildName", guild.Name));
        GuildNotice(guild.Index, ServerStrings.Guild_MemberDemoted, ("Name", member.CharName));
    }

    private static GuildMember? FindMember(GuildRecord guild, string login) =>
        guild.Members.FirstOrDefault(m => string.Equals(m.Login, login, StringComparison.OrdinalIgnoreCase));

    // Remove an account from a guild: roster cache, persisted membership, and (if online) the live
    // mirror + a guild re-broadcast.
    private void RemoveFromGuild(GuildRecord guild, string login)
    {
        guild.Members.RemoveAll(m => string.Equals(m.Login, login, StringComparison.OrdinalIgnoreCase));
        _saver.MutateAccountInBackground(login, a => { a.Guild = 0; a.GuildRank = GuildRank.None; });
        int idx = _pm.FindOnlineByLogin(login);
        if (idx != 0)
        {
            _pm[idx].Guild = 0;
            _pm[idx].GuildRank = GuildRank.None;
            BroadcastPlayerGuild(idx);
        }
        SaveGuild(guild);
    }

    // Change an account's guild rank: roster cache, persisted rank, and (if online) the mirror + broadcast.
    private void SetMemberRank(GuildRecord guild, string login, GuildRank rank)
    {
        var member = FindMember(guild, login);
        if (member is not null) member.Rank = rank;
        _saver.MutateAccountInBackground(login, a => a.GuildRank = rank);
        int idx = _pm.FindOnlineByLogin(login);
        if (idx != 0)
        {
            _pm[idx].GuildRank = rank;
            BroadcastPlayerGuild(idx);
        }
        SaveGuild(guild);
    }

    // A guild-wide positive notice (to all online members) — on the Guild channel so lifecycle events
    // (leave/kick/promote/demote/leadership-transfer) are filterable alongside the rest of guild chat,
    // consistent with the member-joined line.
    private void GuildNotice(int guildId, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToGuild(guildId, key,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild), args);

    // ── Guild chat ─────────────────────────────────────────────────────────────────

    /// <summary>Route a guild-chat line to the whole guild (or, when <paramref name="officer"/>, just the
    /// leader + officers) with the right decorator and rank preface. A guildless sender is a silent no-op
    /// (the client guards too); the officer channel requires Officer+. Recipients who ignore the speaker's
    /// account are skipped by the dispatch (via <see cref="ChatMetadata.SpeakerLogin"/>).</summary>
    public void GuildChat(int index, string msg, bool officer)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null) return;                               // guildless: silent no-op
        if (officer && sp.GuildRank < GuildRank.Officer)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return;
        }  // officer channel = leader/officers only

        long nowUtc = NowUtc;
        bool showAsPk = sp.Char.IsPk(nowUtc) && sp.PkGraceUntilUtc <= nowUtc;
        string name = sp.Char.TrimmedName;
        bool ranked = sp.GuildRank > GuildRank.Member;           // only a Leader/Officer gets a rank preface
        string rankWord = sp.GuildRank switch
        {
            GuildRank.Leader => ServerStrings.Get(ServerStrings.Guild_RankLeader),
            GuildRank.Officer => ServerStrings.Get(ServerStrings.Guild_RankOfficer),
            _ => "",
        };
        int color = officer ? GameColor.GuildOfficer : GameColor.Guild;
        var channel = officer ? ChatChannel.GuildOfficer : ChatChannel.Guild;
        var meta = new ChatMetadata(color, channel, name, sp.Char.Access, showAsPk, sp.Login);
        string key = officer
            ? (ranked ? ServerStrings.GuildOfficer_ChatSayRanked : ServerStrings.GuildOfficer_ChatSay)
            : (ranked ? ServerStrings.Guild_ChatSayRanked : ServerStrings.Guild_ChatSay);

        if (officer)
            _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, key, meta, ("Rank", rankWord), ("Name", name), ("Msg", msg));
        else
            _dispatcher.SendLocalizedChatToGuild(guild.Index, key, meta, ("Rank", rankWord), ("Name", name), ("Msg", msg));
    }
}
