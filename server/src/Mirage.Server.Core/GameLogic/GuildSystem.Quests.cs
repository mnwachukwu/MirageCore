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

/// <summary>Guild quests: the weekly board, acquiring and abandoning, the cost charged to the
/// vault, and the kill-tracking that completes one.</summary>
public sealed partial class GuildSystem : GameSystem
{
    // ── Quests ──────────────────────────────────────────────────────────────────

    /// <summary>Acquire the guild's next quest (Leader only). Draws a random spawning NPC weighted toward
    /// the guild's level, sets a "kill N" objective with rewards scaled by mob difficulty + guild level,
    /// starts the 24h timer, and charges the acquire cost (500 * level; L0 free). Refused if a quest is
    /// already active, the day's acquisition cap is reached, or the leader can't pay.</summary>
    public void AcquireQuest(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (GuildOf(sp) is not { } guild) { Notify(index, ServerStrings.Guild_NotInOne); return; }
        if (sp.GuildRank < GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        if (guild.Quest is not null)
        {
            Notify(index, ServerStrings.Guild_QuestActive);
            return;
        }
        ResetQuestCountersIfNeeded(guild);
        if (guild.QuestsAcquiredToday >= Constants.GuildQuestMaxPerDay)
        {
            Notify(index, ServerStrings.Guild_QuestDailyCap);
            return;
        }

        var candidates = BuildQuestCandidates();
        if (candidates.Count == 0)
        {
            Notify(index, ServerStrings.Guild_QuestNoTargets);
            return;
        }
        if (!ChargeQuestCost(index, guild)) return;

        // Acquiring consumes one of the day's slots up front — abandoning later does NOT give it back.
        guild.QuestsAcquiredToday++;
        AssignRandomQuest(guild, candidates, _pm[index].Char.Level);
        SaveGuild(guild);
        NotifyOk(index, ServerStrings.Guild_QuestAcquired,
            ("Count", guild.Quest!.Objective.Count), ("Npc", _world.Npcs[guild.Quest.Objective.Target].TrimmedName));
    }

    /// <summary>Abandon the guild's active quest (Leader only): clears it — forfeiting all progress with NO
    /// gold refund — which frees the leader to acquire a fresh one. Abandoning itself is free (re-acquiring
    /// pays the normal acquire cost). Refused if there's no active quest.</summary>
    public void AbandonQuest(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (GuildOf(sp) is not { } guild) { Notify(index, ServerStrings.Guild_NotInOne); return; }
        if (sp.GuildRank < GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        if (guild.Quest is null)
        {
            Notify(index, ServerStrings.Guild_QuestNoneToAbandon);
            return;
        }

        StopQuestTracking(guild.Index);
        guild.Quest = null;
        SaveGuild(guild);
        _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_QuestAbandoned,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Guild));
    }

    /// <summary>True if <paramref name="guild"/> has an active KILL quest targeting <paramref name="npcNum"/>
    /// (0 = wildcard). Gates the per-contributor quest-valor roll, which the kill path runs BEFORE the objective
    /// kernel advances the quest (a completing kill would otherwise clear it before the valor roll sees it).</summary>
    public static bool QuestTargetsNpc(GuildRecord? guild, int npcNum)
        => guild?.Quest is { } q && q.Objective.Kind == ObjectiveKind.Kill
           && (q.Objective.Target == npcNum || q.Objective.Target == 0);

    /// <summary>Drop any guild quest past its 24h expiry (unrewarded) and announce it. Called from the
    /// guild schedule tick.</summary>
    public void ExpireDueQuests()
    {
        long nowUtc = NowUtc;
        foreach (var guild in _world.Guilds.Values)
        {
            if (guild.Quest is not { } quest || nowUtc < quest.ExpiresUtc) continue;
            StopQuestTracking(guild.Index);
            guild.Quest = null;
            SaveGuild(guild);
            _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_QuestExpired,
                new ChatMetadata(GameColor.Yellow, ChatChannel.Guild));
        }
    }

    /// <summary>Re-register every loaded guild's still-active quest with the objective kernel at boot. Kernel
    /// registrations live only in memory (the quest persisted on the guild record), so without this the kill
    /// path wouldn't resume advancing a guild quest that was active before the last shutdown. Called once,
    /// after guilds load.</summary>
    public void ReTrackActiveQuests()
    {
        foreach (var guild in _world.Guilds.Values)
        {
            if (guild.Quest is not null)
                TrackGuildQuest(guild);
        }
    }

    // Pay a guild quest's acquire cost from the requester's gold (L0 = free). Returns false (and
    // notifies) if they can't afford it.
    private bool ChargeQuestCost(int index, GuildRecord guild)
    {
        long cost = GuildQuests.AcquireCost(guild.Level);
        if (cost <= 0) return true;
        if (ItemSystem.CountItem(_pm[index].Char, _world.Items, Constants.GoldItemIndex) < cost)
        {
            Notify(index, ServerStrings.Guild_QuestNeedGold, ("Cost", cost));
            return false;
        }
        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);
        return true;
    }

    // Reward the guild for a completed quest, clear it, and forget its kernel handle. Invoked by the objective
    // kernel's onCompleted the instant the finishing kill lands (the registration is already marked done, so
    // this can't re-enter the kill walk). Announced on the Guild channel; AddGuildExp handles any level-up.
    private void CompleteQuest(GuildRecord guild)
    {
        var quest = guild.Quest!;
        guild.VaultGold += quest.RewardGold;
        guild.WeeklyIncome += quest.RewardGold;   // quest gold is earned income → counts on the vault dashboard's weekly total
        guild.Quest = null;
        _questHandles.Remove(guild.Index);   // the kernel auto-untracks a completed reg; just drop our stale handle
        _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_QuestComplete,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild), ("Exp", quest.RewardExp), ("Gold", quest.RewardGold));
        AddGuildExp(guild, quest.RewardExp);   // may level-up (persists + announces)
        SaveGuild(guild);                       // persist the reward gold + cleared quest
    }

    // Draw a weighted-random target from the candidates and build the quest (objective + scaled rewards + 24h timer).
    // playerLevel is the ACQUIRING member's character level — it scales the gold reward the same way it
    // scaled the acquire cost, so the quest stays a net vault gain at every band (EconomyFormulas.BandScale).
    private void AssignRandomQuest(GuildRecord guild, IReadOnlyList<(int NpcId, int Difficulty)> candidates, int playerLevel)
    {
        double pickRoll = Rng.Percent() / 100.0;
        int npcId = GuildQuests.PickQuestNpc(candidates, guild.Level, pickRoll);
        var npc = _world.Npcs[npcId];
        int difficulty = NpcDifficulty(npc);
        bool isBoss = npc.IsBoss;   // bosses get a compressed kill count + a reduced reward (GuildQuests)
        // A second, independent roll sizes the quest: it drives the kill count AND the rewards together, so a
        // bigger objective pays proportionally more XP + gold.
        double varRoll = Rng.Percent() / 100.0;
        guild.Quest = new GuildQuestDef
        {
            Objective = new Objective { Kind = ObjectiveKind.Kill, Target = npcId, Count = GuildQuests.KillCount(difficulty, varRoll, isBoss) },
            RewardExp = GuildQuests.RewardExp(difficulty, guild.Level, varRoll, isBoss),
            RewardGold = GuildQuests.RewardGold(difficulty, guild.Level, varRoll, isBoss),
            ExpiresUtc = NowUtc + Constants.GuildQuestDurationHours * 3600L,
        };
        TrackGuildQuest(guild);   // register with the objective kernel so the kill path advances it
    }

    // Register a guild's freshly-assigned quest objective with the shared objective kernel so the kill path
    // advances it — unifying guild quests with player quests. A kill
    // counts only if a contributor is in THIS guild; each advance flags the guild dirty (in-progress kills persist
    // on the next periodic save) and completion pays the vault via CompleteQuest. The handle is retained so an
    // abandon/expiry can Stop tracking before completion (a completed quest auto-untracks).
    private void TrackGuildQuest(GuildRecord guild)
    {
        _questHandles[guild.Index] = _objectives.Track(
            guild.Quest!.Objective,
            contributor => _pm[contributor].Guild == guild.Index,
            onCompleted: () => CompleteQuest(guild),
            onAdvanced: () => AdvanceQuest(guild));
    }

    // A guild quest is worked by everyone and watched by nobody, so each kill that counted is announced on the
    // Guild channel — otherwise the only visible states are "acquired" and "complete", hours apart. Runs BEFORE
    // onCompleted, so the finishing kill reads as its own full count and then the completion line.
    private void AdvanceQuest(GuildRecord guild)
    {
        _world.DirtyGuilds.Add(guild.Index);   // in-progress kills persist on the next periodic save
        if (guild.Quest is not { } quest) return;
        _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_QuestProgress,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild),
            ("Npc", _world.Npcs[quest.Objective.Target].TrimmedName),
            ("Have", quest.Objective.Progress), ("Need", quest.Objective.Count));
    }

    // Stop the kernel registration for a guild's quest and forget the handle (abandon/expiry). No-op when none
    // is tracked; a completed quest untracks itself, so this is only for an early cancel.
    private void StopQuestTracking(int guildIndex)
    {
        if (_questHandles.Remove(guildIndex, out var handle))
            handle.Stop();
    }

    // Distinct huntable mobs referenced by any map's NPC spawn slots, paired with difficulty (Str+Def+Int)
    // — the "set that spawns" the randomized quest target is drawn from.
    private List<(int NpcId, int Difficulty)> BuildQuestCandidates()
    {
        var seen = new HashSet<int>();
        var list = new List<(int, int)>();
        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            var map = _world.Maps[m];
            if (map is null) continue;
            foreach (var entry in map.Npcs)
            {
                int npcId = entry.Npc;
                if (npcId < 1 || npcId > _world.Limits.Npcs || !seen.Add(npcId)) continue;
                var npc = _world.Npcs[npcId];
                if (npc is null || npc.Behavior is not (NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)) continue;
                list.Add((npcId, NpcDifficulty(npc)));
            }
        }
        return list;
    }

    private static int NpcDifficulty(NpcRecord npc) => npc.Str + npc.Def + npc.Int;

    private void ResetQuestCountersIfNeeded(GuildRecord guild)
    {
        var today = DateOnly.FromDateTime(Clock.LocalNow);
        if (guild.QuestCounterDate == today) return;
        guild.QuestCounterDate = today;
        guild.QuestsAcquiredToday = 0;
    }
}
