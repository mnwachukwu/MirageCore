using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the player's full player-quest state (InProgress + Done entries), replaced wholesale on any
/// change. The client pairs each entry with the quest DEFINITION (sent at join, like items/npcs) to render the
/// quest log and drive the overhead ?/! glyphs.</summary>
public sealed record QuestLogPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.QuestLog;
    [JsonPropertyName("quests")] public List<Entry> Quests { get; init; } = new();
    // Quest numbers the player can ACCEPT right now — the server is the sole eligibility authority (requirements
    // + repeatable period/season relight), so the client renders the giver "?" glyph + accept menu from this set
    // instead of replicating that logic. Recomputed on every quest change.
    [JsonPropertyName("elig")] public int[] EligibleQuests { get; init; } = System.Array.Empty<int>();
    // Repeatable quests already completed in the CURRENT period, so re-accepting waits for the period to roll.
    // Pushed for the same reason as the eligible set — the client can't derive it (the period key is built from
    // server-local date + season state) — and it's the one ineligibility reason with no requirement line of its
    // own, which left the grayed Accept button with every listed requirement met and no stated cause.
    [JsonPropertyName("cool")] public int[] CooldownQuests { get; init; } = System.Array.Empty<int>();

    /// <summary>One quest's per-character state. Progress parallels the quest definition's objectives.</summary>
    public sealed record Entry
    {
        [JsonPropertyName("num")] public int QuestNum { get; init; }
        [JsonPropertyName("st")] public QuestStatus Status { get; init; }
        [JsonPropertyName("pr")] public int[] Progress { get; init; } = System.Array.Empty<int>();
    }
}

/// <summary>S→C: the quest DEFINITIONS (1-based; name/objectives/rewards/requirements/giver+turn-in NPCs),
/// sent once at join like items/npcs. The client pairs these with its QuestLog state to render the log, the
/// accept/turn-in dialog, and the overhead ?/! glyphs. Only non-empty quests are included.</summary>
public sealed record SendQuestsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendQuests;
    [JsonPropertyName("quests")] public List<QuestData> Quests { get; init; } = new();

    /// <summary>One quest definition. Objectives carry the def's Kind/Target/Count (Progress is per-player, from
    /// the QuestLog). TurnInNpc is the RAW value (0 = same as GiverNpc); the client resolves the effective one.</summary>
    public sealed record QuestData
    {
        [JsonPropertyName("num")] public int Num { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("desc")] public string Description { get; init; } = "";
        [JsonPropertyName("obj")] public List<Objective> Objectives { get; init; } = new();
        [JsonPropertyName("reqLvl")] public int ReqLevel { get; init; }
        [JsonPropertyName("reqStr")] public int ReqStr { get; init; }
        [JsonPropertyName("reqDef")] public int ReqDef { get; init; }
        [JsonPropertyName("reqSpd")] public int ReqSpd { get; init; }
        [JsonPropertyName("reqInt")] public int ReqInt { get; init; }
        [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
        [JsonPropertyName("prereq")] public int PrereqQuest { get; init; }
        [JsonPropertyName("rewExp")] public long RewardExp { get; init; }
        [JsonPropertyName("rewItems")] public List<QuestReward> RewardItems { get; init; } = new();
        [JsonPropertyName("repExp")] public long RepeatRewardExp { get; init; }
        [JsonPropertyName("repItems")] public List<QuestReward> RepeatRewardItems { get; init; } = new();
        [JsonPropertyName("giver")] public int GiverNpc { get; init; }
        [JsonPropertyName("turnIn")] public int TurnInNpc { get; init; }
        [JsonPropertyName("repeat")] public bool Repeatable { get; init; }
        [JsonPropertyName("cadence")] public QuestCadence Cadence { get; init; }
    }
}

/// <summary>S→C: open the client-built quest/gossip menu for the NPC at (map, slot) — the reply to a melee-key
/// NpcInteract on an NPC that has an actionable quest for this player. The client already holds the quest defs +
/// its log, so it builds the menu locally; this packet is just the trigger (mirrors OpenInnPacket).</summary>
public sealed record OpenNpcQuestMenuPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.OpenNpcQuestMenu;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int NpcSlot { get; init; }
}

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: accept a quest, from the giver's open menu. The NPC comes from that open menu rather than
/// from here — the server knows which one it opened and for whom — so this carries only which of its quests
/// was picked. The server validates that NPC is the quest's GiverNpc, and that the player is eligible.</summary>
public sealed record QuestAcceptPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.QuestAccept;
    [JsonPropertyName("num")] public int QuestNum { get; init; }
}

/// <summary>C→S: turn a completed quest in for its rewards, from the turn-in NPC's open menu. As with
/// accepting, the NPC comes from the open menu; the server validates it is the quest's EffectiveTurnInNpc.</summary>
public sealed record QuestTurnInPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.QuestTurnIn;
    [JsonPropertyName("num")] public int QuestNum { get; init; }
}

/// <summary>C→S: abandon an in-progress quest (drops it back to not-started, re-acceptable).</summary>
public sealed record QuestAbandonPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.QuestAbandon;
    [JsonPropertyName("num")] public int QuestNum { get; init; }
}
