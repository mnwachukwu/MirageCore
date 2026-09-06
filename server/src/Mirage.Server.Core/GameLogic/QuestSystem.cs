using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Player quests — the FIRST real customer of the shared objective kernel. Accepting a quest registers each of
/// its objectives with <see cref="ObjectiveSystem"/> (kills advance them live via onAdvanced, complete via
/// onCompleted); rewards are granted on TURN-IN at the turn-in NPC (items/gold/exp, mailed if the bag is full,
/// so nothing is lost). Per-character state persists on <c>PlayerRecord.Quests</c>; the runtime Track handles
/// live HERE and are re-established on login / torn down on logout. Repeatable quests re-open via a lazy
/// period-key compare (no scheduler). Runs on the game thread (no locks). This layer does eligibility only —
/// giver/turn-in NPC proximity is enforced by the interaction layer (a later slice).
/// </summary>
public sealed class QuestSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly MailSystem _mail;
    private readonly ObjectiveSystem _objectives;
    // Lazy to break a DI cycle: CombatSystem → JoinLeaveSystem → QuestSystem → CombatSystem. We only need
    // its level-up entry point at reward time (turn-in), by which point everything is constructed.
    private readonly Lazy<CombatSystem> _combat;
    private readonly GuildScheduleSystem _guildSchedule;

    // Per online player: the kernel handles for each active quest's tracked objectives, so they can be Stopped
    // on turn-in / abandon / logout. Keyed by quest number.
    private readonly Dictionary<int, List<ObjectiveSystem.Handle>>[] _tracked;

    private static string QuestSender => ServerStrings.Get(ServerStrings.Quest_Sender);

    public QuestSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items,
        MailSystem mail, ObjectiveSystem objectives, Lazy<CombatSystem> combat, GuildScheduleSystem guildSchedule,
                       IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _mail = mail;
        _objectives = objectives;
        _combat = combat;
        _guildSchedule = guildSchedule;
        _tracked = new Dictionary<int, List<ObjectiveSystem.Handle>>[_pm.Slots + 1];
        for (int i = 0; i <= _pm.Slots; i++) _tracked[i] = new();
    }

    // ── Accept ──────────────────────────────────────────────────────────────────

    public void Accept(int index, int questNum)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying || !QuestExists(questNum)) return;
        if (!IsEligible(index, questNum))
        {
            SendMsg(index, ServerStrings.Quest_NotEligible, GameColor.BrightRed);
            return;
        }
        if (CountInProgress(index) >= Constants.MaxActiveQuests)
        {
            SendMsg(index, ServerStrings.Quest_TooMany, GameColor.BrightRed);
            return;
        }

        var q = _world.Quests[questNum];
        var pq = Find(index, questNum);
        // Re-accepting a quest you've completed before makes it a REPEAT run — the state alone tracks that, so
        // abandon reverts it to Done (not a fresh first run) and turn-in pays the repeat rewards.
        bool repeatRun = pq is { Status: QuestStatus.Done };
        if (pq is null)
        {
            pq = new PlayerQuest { QuestNum = questNum };
            sp.Char.Quests.Add(pq);
        }
        pq.Status = repeatRun ? QuestStatus.InProgressRepeat : QuestStatus.InProgress;
        pq.Progress = new List<int>(new int[q.Objectives.Count]);   // fresh per-objective progress
        // PeriodKey is preserved across a re-accept — it gates when a repeatable quest can re-open.

        TrackQuest(index, pq, q);
        _pm.MarkDirty(index);
        SendMsg(index, ServerStrings.Quest_Accepted, GameColor.BrightGreen, ("Name", q.TrimmedName));
        // A quest with no trackable objectives (e.g. talk-to-X) is instantly ready to turn in.
        if (AllObjectivesComplete(index, questNum))
            SendMsg(index, ServerStrings.Quest_ReadyToTurnIn, GameColor.Yellow, ("Name", q.TrimmedName));
        SyncTo(index);
    }

    // ── Turn in ─────────────────────────────────────────────────────────────────

    public void TurnIn(int index, int questNum)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var pq = Find(index, questNum);
        if (pq is null || !IsActive(pq.Status)) return;
        if (!AllObjectivesComplete(index, questNum))
        {
            SendMsg(index, ServerStrings.Quest_NotComplete, GameColor.BrightRed);
            return;
        }

        var q = _world.Quests[questNum];
        GrantRewards(index, q, pq);   // reads pq.Status (InProgress vs InProgressRepeat) to pick the reward set
        pq.Status = QuestStatus.Done;
        pq.PeriodKey = q.Repeatable ? PeriodKeyFor(q.Cadence) : "";
        StopTracking(index, questNum);
        _pm.MarkDirty(index);
        SendMsg(index, ServerStrings.Quest_Complete, GameColor.BrightGreen, ("Name", q.TrimmedName));
        SyncTo(index);
    }

    private void GrantRewards(int index, QuestRecord q, PlayerQuest pq)
    {
        // The same rule the journal and the giver's offer quote, so the amount promised is the amount paid.
        bool useRepeat = q.PaysRepeatRewards(pq.Status);
        long rewardExp = useRepeat ? q.RepeatRewardExp : q.RewardExp;
        var rewardItems = useRepeat ? q.RepeatRewardItems : q.RewardItems;

        foreach (var r in rewardItems)   // gold is just item #1 here; currency stacks, so it never mails
        {
            if (r.ItemNum <= 0 || r.ItemNum > _world.Limits.Items || r.Quantity <= 0) continue;
            if (!_items.TryGiveItem(index, r.ItemNum, r.Quantity, 0))   // bag full (a non-currency item) → mail it, never lost
            {
                _mail.Deliver(_pm[index].Login, QuestSender,
                    ServerStrings.Get(ServerStrings.Quest_RewardMailSubject),
                    ServerStrings.Get(ServerStrings.Quest_RewardMailBody),
                    new List<MailAttachment> { new() { ItemNum = r.ItemNum, Quantity = r.Quantity } });
            }
        }
        if (rewardExp > 0)
        {
            var p = _pm[index].Char;
            p.Exp = Math.Min(p.Exp + rewardExp, ExpFormulas.MaxTotalExp);
            // Name the number. The exp bar reads WITHIN the current level, so a reward lands as a nudge to a
            // bar rather than a figure — and with two quests in progress there is nothing tying the amount a
            // board promised to the one that just paid out.
            SendMsg(index, ServerStrings.Quest_RewardExp, GameColor.BrightBlue, ChatChannel.Rewards, ("Exp", rewardExp));
            // 🔴 And push the stats, or the reward is invisible: the client's exp comes from this packet alone,
            // so without it the bar does not move and the overhead number never floats — the client raises both
            // off an exp that arrived higher than the one it had. A kill syncs here; a turn-in did not.
            _dispatcher.SendTo(index, PacketBuilder.SendStats(p));
            _combat.Value.CheckPlayerLevelUp(index);
        }
    }

    // ── Abandon ─────────────────────────────────────────────────────────────────

    public void Abandon(int index, int questNum)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var pq = Find(index, questNum);
        if (pq is null || !IsActive(pq.Status)) return;
        StopTracking(index, questNum);
        if (pq.Status == QuestStatus.InProgress)
        {
            // Never completed → drop the entry entirely: a clean first-time reset, eligible for the main rewards again.
            sp.Char.Quests.Remove(pq);
        }
        else   // InProgressRepeat
        {
            // A completed-before repeatable being re-run: revert to Done (still period-gated) instead of wiping,
            // so you can't abandon-and-re-run to re-farm the richer first-completion rewards. Clear this attempt.
            pq.Status = QuestStatus.Done;
            pq.Progress = new List<int>();
        }
        _pm.MarkDirty(index);
        SendMsg(index, ServerStrings.Quest_Abandoned, GameColor.Pink, ("Name", _world.Quests[questNum].TrimmedName));
        SyncTo(index);
    }

    // ── Login / logout ──────────────────────────────────────────────────────────

    /// <summary>Re-establish kernel tracking for a player's in-progress quests on login, then push their log.</summary>
    public void OnPlayerJoin(int index)
    {
        foreach (var pq in _pm[index].Char.Quests)
        {
            if (IsActive(pq.Status) && QuestExists(pq.QuestNum))
                TrackQuest(index, pq, _world.Quests[pq.QuestNum]);
        }

        SyncTo(index);
    }

    /// <summary>Stop tracking all of a player's quests on logout (the persisted state carries progress).</summary>
    public void OnPlayerGone(int index)
    {
        foreach (var handles in _tracked[index].Values)
            foreach (var h in handles) h.Stop();
        _tracked[index].Clear();
    }

    // ── Kernel tracking ─────────────────────────────────────────────────────────

    private void TrackQuest(int index, PlayerQuest pq, QuestRecord q)
    {
        StopTracking(index, pq.QuestNum);
        // Normalize progress length to the current definition (a def could have been re-authored).
        while (pq.Progress.Count < q.Objectives.Count) pq.Progress.Add(0);

        var handles = new List<ObjectiveSystem.Handle>();
        for (int k = 0; k < q.Objectives.Count; k++)
        {
            var def = q.Objectives[k];
            if (def.Count <= 0 || pq.Progress[k] >= def.Count) continue;   // done / degenerate — no need to track
            var live = new Objective { Kind = def.Kind, Target = def.Target, Count = def.Count, Progress = pq.Progress[k] };
            int objIndex = k, quest = pq.QuestNum;
            var h = _objectives.Track(live,
                contributor => contributor == index,
                onCompleted: () => OnObjectiveCompleted(index, quest),
                onAdvanced: () => OnObjectiveAdvanced(index, quest, objIndex, live.Progress));
            handles.Add(h);
        }
        if (handles.Count > 0) _tracked[index][pq.QuestNum] = handles;
    }

    private void OnObjectiveAdvanced(int index, int questNum, int objIndex, int progress)
    {
        var pq = Find(index, questNum);
        if (pq is null || objIndex >= pq.Progress.Count) return;
        pq.Progress[objIndex] = progress;
        _pm.MarkDirty(index);
        SyncTo(index);

        // Say it out loud as well as syncing the log: a kill that counted looks exactly like one that did not
        // unless the panel happens to be open, and a quest is mostly played with it closed.
        var objectives = _world.Quests[questNum].Objectives;
        if (objIndex < objectives.Count)
        {
            var o = objectives[objIndex];
            SendMsg(index, ServerStrings.Quest_ObjectiveProgress, GameColor.Yellow,
                ("Target", TargetName(o.Target)), ("Have", progress), ("Need", o.Count));
        }
    }

    /// <summary>What an objective's target is called. A target of 0 is the wildcard "anything of this kind",
    /// which has no name to give.</summary>
    private string TargetName(int npcNum)
        => npcNum >= 1 && npcNum < _world.Npcs.Length && _world.Npcs[npcNum].TrimmedName.Length > 0
            ? _world.Npcs[npcNum].TrimmedName
            : ServerStrings.Get(ServerStrings.Quest_AnyTarget);

    private void OnObjectiveCompleted(int index, int questNum)
    {
        // One objective finished; if the WHOLE quest is now complete, tell the player it's ready to turn in.
        // (Progress + sync were already pushed by OnObjectiveAdvanced on this same kill.)
        if (AllObjectivesComplete(index, questNum))
            SendMsg(index, ServerStrings.Quest_ReadyToTurnIn, GameColor.Yellow, ("Name", _world.Quests[questNum].TrimmedName));
    }

    private void StopTracking(int index, int questNum)
    {
        if (_tracked[index].TryGetValue(questNum, out var handles))
        {
            foreach (var h in handles) h.Stop();
            _tracked[index].Remove(questNum);
        }
    }

    // ── Eligibility ─────────────────────────────────────────────────────────────

    /// <summary>Can this player accept this quest right now? Requirements met, not already in progress, and —
    /// if already Done — not repeatable=never, or its repeat period has rolled over.</summary>
    public bool IsEligible(int index, int questNum)
    {
        if (!QuestExists(questNum)) return false;
        var q = _world.Quests[questNum];
        var pq = Find(index, questNum);
        if (pq is not null && IsActive(pq.Status)) return false;          // already active (first run or repeat)
        if (pq is { Status: QuestStatus.Done })
        {
            if (!q.Repeatable) return false;                       // done forever
            if (IsOnRepeatCooldown(index, questNum)) return false;  // this period already done
        }
        return RequirementsMet(index, q);
    }

    /// <summary>Is this a REPEATABLE quest the player already completed in the current period, so re-accepting has
    /// to wait for the period to roll over? The one ineligibility reason the client can't derive for itself (the
    /// period key is built from server-local date + season state), so it rides along in the QuestLog push and the
    /// quest panel can name it instead of graying Accept with no visible cause. Cadence.None never re-opens, and a
    /// zero key compares equal to its own, so such a quest correctly reports a permanent cooldown.</summary>
    public bool IsOnRepeatCooldown(int index, int questNum)
    {
        if (!QuestExists(questNum)) return false;
        var q = _world.Quests[questNum];
        if (!q.Repeatable) return false;
        var pq = Find(index, questNum);
        return pq is { Status: QuestStatus.Done } && pq.PeriodKey == PeriodKeyFor(q.Cadence);
    }

    /// <summary>Why a character could not hold a quest, or <see cref="Ok"/>.</summary>
    public enum HoldResult
    {
        Ok = 0,
        LevelTooLow,
        StatTooLow,
        WrongClass,
        PrereqNotDone,
    }

    /// <summary>
    /// Whether <paramref name="p"/> meets what <paramref name="q"/> asks of them — the accept requirements,
    /// and nothing about whether they already hold it.
    ///
    /// <para>Takes the RECORD rather than a player slot so the editor's account browser can ask it of a
    /// character who is nowhere near one, and shares the answer with the accept path so an operator cannot
    /// put a quest somewhere the game would not.</para>
    /// </summary>
    public static HoldResult CanHold(PlayerRecord p, QuestRecord q)
    {
        if (p.Level < q.ReqLevel) return HoldResult.LevelTooLow;
        if (p.Str < q.ReqStr || p.Def < q.ReqDef || p.Spd < q.ReqSpd || p.Int < q.ReqInt) return HoldResult.StatTooLow;
        if (!ClassGate.Allows(q.AllowedClasses, p.Class)) return HoldResult.WrongClass;
        if (q.PrereqQuest > 0 && FindQuest(p, q.PrereqQuest) is not { Status: QuestStatus.Done })
            return HoldResult.PrereqNotDone;
        return HoldResult.Ok;
    }

    /// <summary>That character's row for a quest, or null when it is not in their log.</summary>
    public static PlayerQuest? FindQuest(PlayerRecord p, int questNum)
    {
        foreach (var pq in p.Quests) if (pq.QuestNum == questNum) return pq;
        return null;
    }

    private bool RequirementsMet(int index, QuestRecord q) => CanHold(_pm[index].Char, q) == HoldResult.Ok;

    private int CountInProgress(int index)
    {
        int n = 0;
        foreach (var pq in _pm[index].Char.Quests) if (IsActive(pq.Status)) n++;
        return n;
    }

    // "Active" = accepted and not yet turned in — a first run or a repeat run alike.
    private static bool IsActive(QuestStatus s) => s is QuestStatus.InProgress or QuestStatus.InProgressRepeat;

    private bool IsDone(int index, int questNum) => Find(index, questNum) is { Status: QuestStatus.Done };

    private bool QuestExists(int questNum) =>
        SlotValidation.IsValidQuestNum(questNum, _world.Limits.Quests) && _world.Quests[questNum].TrimmedName.Length > 0;

    private PlayerQuest? Find(int index, int questNum) => FindQuest(_pm[index].Char, questNum);

    /// <summary>Are all of a quest's objectives complete for this player? (A quest with zero objectives counts
    /// as complete — a talk / turn-in-only quest.)</summary>
    public bool AllObjectivesComplete(int index, int questNum)
    {
        var pq = Find(index, questNum);
        if (pq is null) return false;
        var q = _world.Quests[questNum];
        for (int k = 0; k < q.Objectives.Count; k++)
            if (GetProgress(pq, k) < q.Objectives[k].Count) return false;
        return true;
    }

    /// <summary>Does NPC template <paramref name="npcNum"/> offer this player an ACTIONABLE quest right now — a
    /// quest it gives that's eligible to accept, or an active quest that turns in here and is ready? Drives the
    /// interaction spine's decision to open the quest/gossip menu.</summary>
    public bool HasActionableQuestAt(int index, int npcNum)
    {
        if (npcNum <= 0 || !_pm[index].IsPlaying) return false;
        for (int q = 1; q <= _world.Limits.Quests; q++)
        {
            if (!QuestExists(q)) continue;
            var quest = _world.Quests[q];
            if (quest.GiverNpc == npcNum && IsEligible(index, q)) return true;
            if (quest.EffectiveTurnInNpc == npcNum && ReadyToTurnIn(index, q)) return true;
        }
        return false;
    }

    // A quest is ready to turn in when the player has it active and every objective is complete.
    private bool ReadyToTurnIn(int index, int questNum)
        => Find(index, questNum) is { } pq && IsActive(pq.Status) && AllObjectivesComplete(index, questNum);

    /// <summary>Re-push the player's quest log + eligible set — call after something (a level-up, stat training)
    /// may have newly met a quest's accept requirements, so the giver "?" glyph relights without a quest event.</summary>
    public void RefreshEligibility(int index) => SyncTo(index);

    private static int GetProgress(PlayerQuest pq, int k) => k < pq.Progress.Count ? pq.Progress[k] : 0;

    // The stable period key a Repeatable quest's completion is stamped with; eligibility re-lights when the
    // current key differs. Server-local dates (matching the guild schedule boundaries), no scheduler needed.
    private string PeriodKeyFor(QuestCadence cadence) => cadence switch
    {
        QuestCadence.Daily => DateOnly.FromDateTime(Clock.LocalNow).ToString("yyyy-MM-dd"),
        QuestCadence.Weekly => WeekKey(),
        QuestCadence.Monthly => Clock.LocalNow.ToString("yyyy-MM"),
        QuestCadence.Seasonally => "S" + _guildSchedule.SeasonNumber,
        _ => "",
    };

    // Current week bucket, keyed by the most recent territory reset weekday (matches the guild weekly boundary).
    private string WeekKey()
    {
        var today = DateOnly.FromDateTime(Clock.LocalNow);
        int back = ((int)today.DayOfWeek - (int)Config.Schedule.WeekResetDay + 7) % 7;
        return today.AddDays(-back).ToString("yyyy-MM-dd");
    }

    private void SyncTo(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var entries = new List<QuestLogPacket.Entry>(sp.Char.Quests.Count);
        foreach (var pq in sp.Char.Quests)
            entries.Add(new QuestLogPacket.Entry { QuestNum = pq.QuestNum, Status = pq.Status, Progress = pq.Progress.ToArray() });
        // The client's giver "?" glyph + accept menu key off this authoritative eligible set (so requirements +
        // repeatable relight aren't re-derived client-side). Small scan over the static quest table on change.
        var eligible = new List<int>();
        // Cooldown rides along so the quest panel can say WHY an offer it can still see is grayed out.
        var cooldown = new List<int>();
        for (int q = 1; q <= _world.Limits.Quests; q++)
        {
            if (IsEligible(index, q)) eligible.Add(q);
            else if (IsOnRepeatCooldown(index, q)) cooldown.Add(q);
        }
        _dispatcher.SendTo(index, new QuestLogPacket
        {
            Quests = entries,
            EligibleQuests = eligible.ToArray(),
            CooldownQuests = cooldown.ToArray(),
        });
    }
}
