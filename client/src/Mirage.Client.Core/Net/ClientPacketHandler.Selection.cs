using Mirage.Client.Core.State;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>What the player has selected, as the server resolved it.
///
/// <para>The server owns the answer: a click sends a proposal, and what comes back is either the body
/// it resolved to or an instruction to drop the guess. So the client never decides it has selected
/// something — it is told.</para></summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    private void HandleSetTarget(SetTargetPacket p)
        => TargetAssigned?.Invoke(TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot));

    /// <summary>The server refused a proposal — the body moved, despawned, or was never observable.
    /// The local guess is dropped so the two ends agree that nothing is selected.</summary>
    private void HandleClearTarget() => TargetAssigned?.Invoke(default);

    // Translates the server's TargetType convention (0=player, 1=npc, 2=self, 3=visitor) into the
    // client's TargetRef shape. Self collapses to a Player ref on the local index, matching the
    // click-your-own-tile path the server takes.
    private TargetRef TargetRefFrom(byte targetType, int target, int targetMap, int spawnMap, int spawnSlot)
        => targetType switch
        {
            0 => new TargetRef(TargetKind.Player, target, 0),
            1 => new TargetRef(TargetKind.Npc, target, targetMap),
            2 => new TargetRef(TargetKind.Player, _state.MyIndex, 0),
            3 => new TargetRef(TargetKind.Traversal, spawnMap, spawnSlot),
            _ => default,
        };
}
