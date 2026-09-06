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

/// <summary>Player quests. Accept and turn-in act on the NPC whose menu the player has open; abandon comes
/// from the quest log and involves no NPC at all.</summary>
public sealed partial class PacketHandler
{
    //  Quest handlers
    // ===========================================================================

    // ── Player quests ─────────────────────────────────────────────────────────
    // Accept and turn-in resolve the NPC from the OPEN MENU, not from the packet: reach is decided once, by
    // the interact spine that opened the menu, and the quest dialog pins the player where they stand — so a
    // giver wandering off mid-read must not cancel the accept. What each still checks is the NPC's ROLE, which
    // is what stops one NPC's menu accepting another's quest. QuestSystem owns eligibility and rewards.
    private void HandleQuestAccept(int index, QuestAcceptPacket p)
    {
        if (!IsActing(index) || !SlotValidation.IsValidQuestNum(p.QuestNum, _world.Limits.Quests)) return;
        int npcNum = _pm[index].ActiveQuestNpc(_world);
        if (npcNum <= 0) { NoQuestGiverOpen(index); return; }
        if (_world.Quests[p.QuestNum].GiverNpc != npcNum) return;   // accepting is only allowed at the giver
        _quests.Accept(index, p.QuestNum);
    }
    private void HandleQuestTurnIn(int index, QuestTurnInPacket p)
    {
        if (!IsActing(index) || !SlotValidation.IsValidQuestNum(p.QuestNum, _world.Limits.Quests)) return;
        int npcNum = _pm[index].ActiveQuestNpc(_world);
        if (npcNum <= 0) { NoQuestGiverOpen(index); return; }
        if (_world.Quests[p.QuestNum].EffectiveTurnInNpc != npcNum) return;   // turning in is only allowed at the turn-in NPC
        _quests.TurnIn(index, p.QuestNum);
    }

    /// <summary>No NPC's quest menu is open, so there is nobody to accept from or turn in to. Reachable when
    /// an offer is opened without the interact that claims the giver, and a refusal the player cannot see
    /// reads as a button that does nothing.</summary>
    private void NoQuestGiverOpen(int index) =>
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Quest_NoGiverOpen,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice));
    private void HandleQuestAbandon(int index, QuestAbandonPacket p) { if (IsActing(index)) _quests.Abandon(index, p.QuestNum); }
}
