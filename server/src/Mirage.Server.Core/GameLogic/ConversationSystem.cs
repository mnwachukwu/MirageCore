using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// NPC conversations (dialogue trees) — a light per-character layer over the editor-authored
/// <see cref="GameWorld.Conversations"/>. It owns only the per-character VISITED-LOG
/// (<c>PlayerRecord.ConversationsSpoken</c>): the set of conversation numbers this character has opened,
/// which colors the overhead "..." glyph yellow (unspoken) / gray (spoken). The dialogue TREE itself is
/// client-driven — the defs are pushed at join and the client walks the tree locally — so this system holds
/// no per-node state and grants nothing. Opening a conversation is resolved by the interaction layer
/// (<c>PacketHandler.HandleNpcInteract</c>), which calls <see cref="MarkSpoken"/>. Runs on the game thread.
/// </summary>
public sealed class ConversationSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;

    public ConversationSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
    }

    /// <summary>Push the character's spoken-conversation set at login, so the client can color the "..." glyphs.</summary>
    public void OnPlayerJoin(int index) => SyncTo(index);

    /// <summary>Record that the character opened conversation <paramref name="convNum"/> — the visit that flips its
    /// overhead "..." glyph yellow → gray. No-op if it isn't a real conversation or was already spoken; otherwise
    /// it persists (marks the character dirty) and re-pushes the log to just that player.</summary>
    public void MarkSpoken(int index, int convNum)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying || !SlotValidation.IsValidConversationNum(convNum, _world.Limits.Conversations)) return;
        if (_world.Conversations[convNum].TrimmedName.Length == 0) return;   // not a real conversation
        var spoken = sp.Char.ConversationsSpoken;
        if (spoken.Contains(convNum)) return;                                // already gray — nothing to do
        spoken.Add(convNum);
        _pm.MarkDirty(index);
        SyncTo(index);
    }

    private void SyncTo(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        _dispatcher.SendTo(index, new ConversationLogPacket { Spoken = sp.Char.ConversationsSpoken.ToArray() });
    }
}
