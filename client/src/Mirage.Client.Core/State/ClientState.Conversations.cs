using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;
/// <summary>NPC conversations: the dialogue-tree definitions, this character's visited-set, and the
/// overhead "..." glyph those resolve to on each NPC.</summary>
public sealed partial class ClientState
{
    // ConvDefs: dialogue-tree definitions cached at join (SendConversationsPacket), like items/npcs/quests — the
    // client walks a tree locally when a conversation opens. _spokenConversations: this character's visited-set
    // (ConversationLogPacket). NpcConvGlyph: the DERIVED overhead "..." marker per NPC, recomputed from the above
    // whenever the defs or the spoken-set change.

    /// <summary>Conversation definitions (1-based); null slot = no such conversation.</summary>
    public ConversationRecord[] ConvDefs { get; private set; } = new ConversationRecord[RecordLimits.Default.Conversations + 1];

    /// <summary>NPC template num → overhead conversation glyph (0 none / 1 gray "..." spoken / 2 yellow "..."
    /// unspoken; higher wins so an unspoken conversation outranks a spoken one). Derived, never pushed.</summary>
    public int[] NpcConvGlyph { get; private set; } = new int[RecordLimits.Default.Npcs + 1];

    public const int ConvGlyphNone = 0, ConvGlyphSpoken = 1, ConvGlyphUnspoken = 2;

    private HashSet<int> _spokenConversations = new();

    /// <summary>Replace the conversation definitions from the join-time SendConversations, then refresh glyphs.</summary>
    public void SetConvDefs(IEnumerable<(int Num, ConversationRecord Def)> defs)
    {
        Array.Clear(ConvDefs, 0, ConvDefs.Length);
        foreach (var (num, def) in defs)
            if (num >= 1 && num < ConvDefs.Length) ConvDefs[num] = def;
        RecomputeNpcConvGlyphs();
    }

    /// <summary>Replace ONE conversation definition (a live editor UpdateConversation broadcast) + refresh glyphs.</summary>
    public void SetConvDef(int num, ConversationRecord def)
    {
        if (num >= 1 && num < ConvDefs.Length) ConvDefs[num] = def;
        RecomputeNpcConvGlyphs();
    }

    /// <summary>Replace the character's spoken-conversation set (from a ConversationLogPacket) + refresh glyphs.</summary>
    public void SetConversationsSpoken(IEnumerable<int> spoken)
    {
        _spokenConversations = new HashSet<int>(spoken);
        RecomputeNpcConvGlyphs();
    }

    /// <summary>The conversation attached to NPC template <paramref name="npcNum"/> (SpeakerNpc), or 0 if none —
    /// drives the context-menu "Talk" item. First non-empty match wins (mirrors GameWorld.ConversationForNpc).</summary>
    public int ConversationForNpc(int npcNum)
    {
        if (npcNum <= 0) return 0;
        for (int c = 1; c < ConvDefs.Length; c++)
        {
            var def = ConvDefs[c];
            if (def is not null && def.SpeakerNpc == npcNum && def.TrimmedName.Length > 0) return c;
        }
        return 0;
    }

    private void RecomputeNpcConvGlyphs()
    {
        Array.Clear(NpcConvGlyph, 0, NpcConvGlyph.Length);
        for (int c = 1; c < ConvDefs.Length; c++)
        {
            var def = ConvDefs[c];
            if (def is null || def.TrimmedName.Length == 0) continue;
            int npc = def.SpeakerNpc;
            if (npc < 1 || npc >= NpcConvGlyph.Length) continue;
            int g = _spokenConversations.Contains(c) ? ConvGlyphSpoken : ConvGlyphUnspoken;
            if (g > NpcConvGlyph[npc]) NpcConvGlyph[npc] = g;   // yellow (unspoken) outranks gray (spoken)
        }
    }
}
