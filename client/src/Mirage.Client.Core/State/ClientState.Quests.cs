using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;
/// <summary>Player quests: definitions, the per-character log, eligibility, and the overhead ?/!
/// glyphs those resolve to on each NPC.</summary>
public sealed partial class ClientState
{
    // ── Player quests ─────────────────────────────────────────────────────────
    // QuestDefs: definitions cached at join (SendQuestsPacket), like items/npcs. Quests: the per-character log
    // pushed wholesale by QuestLogPacket. _eligibleQuests: the server-authoritative "can accept now" set, so
    // requirements + repeatable relight aren't re-derived here. NpcQuestGlyph: the DERIVED overhead ?/! marker
    // per NPC template, recomputed from the above whenever the defs or log change.

    /// <summary>Quest definitions (1-based); null slot = no such quest.</summary>
    public QuestRecord[] QuestDefs { get; private set; } = new QuestRecord[RecordLimits.Default.Quests + 1];

    /// <summary>NPC template num → overhead quest glyph (0 none / 1 gray "?" / 2 gray "!" / 3 yellow "?" /
    /// 4 yellow "!"; higher wins). Derived, never pushed. Parallel to NpcDefs.</summary>
    public int[] NpcQuestGlyph { get; private set; } = new int[RecordLimits.Default.Npcs + 1];

    /// <summary>The player's quest log — one entry per accepted quest (a never-started quest has no entry).
    /// Replaced whole by each QuestLogPacket.</summary>
    public List<PlayerQuest> Quests { get; private set; } = new();

    private HashSet<int> _eligibleQuests = new();
    // Repeatable quests already completed this period — pushed with the eligible set because the period key is
    // server-local. Not an input to any glyph (those already read as gray/blue); it exists so the quest panel can
    // state the reason a still-visible offer can't be accepted yet.
    private HashSet<int> _cooldownQuests = new();

    /// <summary>Bumped whenever the quest log / eligibility changes, so the quest-log panel rebuilds only on a
    /// real change (mirrors <see cref="MailVersion"/>).</summary>
    public int QuestVersion { get; private set; }

    // Overhead glyph codes — also the render priority (higher wins when an NPC fills several roles). A glyph is a
    // promise that the player can act at this NPC right now, and the context menu offers exactly what it promises;
    // the one non-actionable state is gray "!", which marks a quest already accepted and still running. Actionable
    // states split by repeatability: a repeatable quest you can accept / turn in shows BLUE, a one-time quest
    // YELLOW. Within a tier, one-time (yellow) outranks repeatable (blue); turn-in (!) outranks accept (?).
    public const int QuestGlyphNone = 0, QuestGlyphGrayBang = 1,
        QuestGlyphBlueQuestion = 2, QuestGlyphYellowQuestion = 3, QuestGlyphBlueBang = 4, QuestGlyphYellowBang = 5;

    /// <summary>What a player can do with a quest AT a given NPC (drives the interaction menu).</summary>
    public enum QuestAction { Accept, TurnIn }

    /// <summary>Replace the quest definitions from the join-time SendQuests, then refresh the overhead glyphs.</summary>
    public void SetQuestDefs(IEnumerable<(int Num, QuestRecord Def)> defs)
    {
        Array.Clear(QuestDefs, 0, QuestDefs.Length);
        foreach (var (num, def) in defs)
            if (num >= 1 && num < QuestDefs.Length) QuestDefs[num] = def;
        RecomputeNpcQuestGlyphs();
    }

    /// <summary>Replace ONE quest definition (a live editor UpdateQuest broadcast) + refresh the overhead glyphs.
    /// The server re-pushes the eligible set (QuestLog) separately, so a requirement edit relights too.</summary>
    public void SetQuestDef(int num, QuestRecord def)
    {
        if (num >= 1 && num < QuestDefs.Length) QuestDefs[num] = def;
        RecomputeNpcQuestGlyphs();
    }

    /// <summary>Replace the per-player quest log + the server's eligible and repeat-cooldown sets (from a
    /// QuestLogPacket), then refresh the overhead glyphs and bump <see cref="QuestVersion"/>.</summary>
    public void SetQuests(List<PlayerQuest> quests, IEnumerable<int> eligible, IEnumerable<int> cooldown)
    {
        Quests = quests;
        _eligibleQuests = new HashSet<int>(eligible);
        _cooldownQuests = new HashSet<int>(cooldown);
        QuestVersion++;
        RecomputeNpcQuestGlyphs();
    }

    public PlayerQuest? FindQuest(int questNum)
    {
        foreach (var pq in Quests) if (pq.QuestNum == questNum) return pq;
        return null;
    }

    /// <summary>Can the player accept this quest right now (server-authoritative eligible set)?</summary>
    public bool IsQuestEligible(int questNum) => _eligibleQuests.Contains(questNum);

    /// <summary>Is this a repeatable quest the player already completed in the current period, so it re-opens only
    /// once that period rolls over? Server-authoritative, like <see cref="IsQuestEligible"/>. Drives the "already
    /// done this day/week/..." line that explains a grayed Accept whose other requirements are all met.</summary>
    public bool IsQuestOnRepeatCooldown(int questNum) => _cooldownQuests.Contains(questNum);

    /// <summary>An active quest whose every objective is complete — ready to turn in.</summary>
    public bool IsQuestReadyToTurnIn(int questNum)
    {
        var pq = FindQuest(questNum);
        if (pq is null || !IsActiveQuestStatus(pq.Status)) return false;
        var def = questNum >= 1 && questNum < QuestDefs.Length ? QuestDefs[questNum] : null;
        if (def is null) return false;
        for (int k = 0; k < def.Objectives.Count; k++)
            if ((k < pq.Progress.Count ? pq.Progress[k] : 0) < def.Objectives[k].Count) return false;
        return true;
    }

    /// <summary>The quests actionable at NPC template <paramref name="npcNum"/> — eligible-to-accept givers and
    /// ready-to-turn-in quests. Drives the interaction menu, and is the same set the overhead "?"/"!" reflects, so
    /// the menu offers exactly what the glyph promised. Class-locked quests never reach the server's eligible set,
    /// so they are filtered here too. Mirrors QuestSystem.HasActionableQuestAt.</summary>
    public IEnumerable<(int QuestNum, QuestAction Action)> ActionableQuestsAt(int npcNum)
    {
        if (npcNum <= 0) yield break;
        for (int q = 1; q < QuestDefs.Length; q++)
        {
            var def = QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            if (def.GiverNpc == npcNum && IsQuestEligible(q)) yield return (q, QuestAction.Accept);
            if (def.EffectiveTurnInNpc == npcNum && IsQuestReadyToTurnIn(q)) yield return (q, QuestAction.TurnIn);
        }
    }

    private static bool IsActiveQuestStatus(QuestStatus s) => s is QuestStatus.InProgress or QuestStatus.InProgressRepeat;

    /// <summary>Refresh the derived overhead glyphs. Public so the local player's data load (which sets the class
    /// the glyph class-filter depends on) can relight them independently of a quest push.</summary>
    public void RefreshQuestGlyphs() => RecomputeNpcQuestGlyphs();

    private void RecomputeNpcQuestGlyphs()
    {
        Array.Clear(NpcQuestGlyph, 0, NpcQuestGlyph.Length);
        for (int q = 1; q < QuestDefs.Length; q++)
        {
            var def = QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            // A quest locked to a DIFFERENT class shows NO glyph at all — the NPC falls through to its other
            // actions. Only filter once the local class is known (0 = not yet loaded; the SendPlayerData refresh
            // relights it). Other unmet requirements (level/stats/prereq) still surface as a gray "?".
            if (Me.Class > 0 && !ClassGate.Allows(def.AllowedClasses, Me.Class)) continue;
            var pq = FindQuest(q);
            bool active = pq is not null && IsActiveQuestStatus(pq.Status);

            // Giver "?" : eligible to accept → yellow, UNLESS it's a repeatable you've completed before → blue
            // ("available again"). A quest whose level/stat/prereq requirements aren't met shows NOTHING — these
            // givers hold eighteen quests apiece, so marking every one you might someday take says only "this NPC
            // has quests", which the player already knows.
            if (IsQuestEligible(q) && def.GiverNpc >= 1 && def.GiverNpc < NpcQuestGlyph.Length)
            {
                int g = def.Repeatable && pq is { Status: QuestStatus.Done }
                    ? QuestGlyphBlueQuestion : QuestGlyphYellowQuestion;
                if (g > NpcQuestGlyph[def.GiverNpc]) NpcQuestGlyph[def.GiverNpc] = g;
            }
            // Turn-in "!" : ready → yellow (first run) / blue (a REPEAT run — done before); gray while not ready.
            int turnIn = def.EffectiveTurnInNpc;
            if (active && turnIn >= 1 && turnIn < NpcQuestGlyph.Length)
            {
                bool repeatRun = pq is { Status: QuestStatus.InProgressRepeat };
                int g = IsQuestReadyToTurnIn(q) ? (repeatRun ? QuestGlyphBlueBang : QuestGlyphYellowBang)
                       : QuestGlyphGrayBang;
                if (g > NpcQuestGlyph[turnIn]) NpcQuestGlyph[turnIn] = g;
            }
        }
    }
}
