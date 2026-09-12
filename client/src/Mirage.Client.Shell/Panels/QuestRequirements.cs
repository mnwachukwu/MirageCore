using Mirage.Client.Core.State;
using Mirage.Client.Shell.Localization;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Panels;

/// <summary>Shared builder for a quest's requirement checklist — each entry is the requirement text plus whether
/// the local player currently meets it (rendered green when met, red when not). Used by both the quest-log hover
/// tooltip and the quest-offer dialog's ineligible section, so the two always read identically. The list covers
/// every reason an offer the player can SEE is still unacceptable, so a grayed Accept always has a stated cause.</summary>
internal static class QuestRequirements
{
    public static List<(string Text, bool Met)> Build(ClientState state, int questNum, QuestRecord def)
    {
        var lines = new List<(string Text, bool Met)>();
        if (def.PrereqQuest > 0)
        {
            string pname = def.PrereqQuest < state.QuestDefs.Length ? (state.QuestDefs[def.PrereqQuest]?.TrimmedName ?? "?") : "?";
            bool met = state.FindQuest(def.PrereqQuest) is { Status: QuestStatus.Done };
            lines.Add((ClientStrings.Format(ClientStrings.QuestPanel_ReqPrereq, ("Quest", pname)), met));
        }
        // Last, because it's a gate on the quest rather than on the character: a repeatable already finished this
        // period. Always unmet when present — the server drops the quest from this set the moment it re-lights.
        if (state.IsQuestOnRepeatCooldown(questNum))
            lines.Add((ClientStrings.Get(CooldownKey(def.Cadence)), false));
        return lines;
    }

    // Which "already done this <period>" line a repeatable quest's cadence reads as. Cadence.None never re-opens,
    // so it states the completion without naming a period.
    private static string CooldownKey(QuestCadence cadence) => cadence switch
    {
        QuestCadence.Daily => ClientStrings.QuestPanel_ReqDoneToday,
        QuestCadence.Weekly => ClientStrings.QuestPanel_ReqDoneThisWeek,
        QuestCadence.Monthly => ClientStrings.QuestPanel_ReqDoneThisMonth,
        _ => ClientStrings.QuestPanel_ReqDoneAlready,
    };
}
