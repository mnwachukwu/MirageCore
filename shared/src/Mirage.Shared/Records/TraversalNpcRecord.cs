namespace Mirage.Shared.Records;

/// <summary>
/// A hostile NPC that has temporarily left its home map to chase a player across a seamless
/// border.  It carries the full NPC state (inherited) plus its permanent home identity.  Traversal
/// NPCs live in <c>GameWorld.MapTraversalNpcs[currentMap]</c> rather than the destination map's
/// fixed slot array, so they never consume a native slot.  A guest pursues freely across any number
/// of maps (and through warps) until combat ends, at which point it is despawned and the native NPC
/// respawns on its home slot — so no per-hop depth bookkeeping is needed.
///
/// The <c>(SpawnMapNum, SpawnSlot)</c> pair is the NPC's universal identifier — used in every
/// packet and on the client to reference it regardless of which map it is currently on.
/// </summary>
public sealed class TraversalNpcRecord : MapNpcRecord
{
    public int SpawnMapNum { get; set; }   // home map — permanent identifier, never changes
    public int SpawnSlot { get; set; }     // slot on the home map — permanent identifier
    public int CurrentMapNum { get; set; } // which map this NPC is currently standing on

    // Timestamp of the AI tick this guest was last processed.  Maps are ticked in ascending order, so
    // a guest that crosses to a higher-numbered map would otherwise be processed a second time the same
    // tick; stamping lets RunTraversalAi skip it until the next tick (one action per guest per tick).
    public long LastAiTick { get; set; }

    // Guests use their permanent home identity, not their transient (currentMap, listIndex), so
    // NPC-vs-NPC contributors and targets remain stable across cross-seam transitions.
    public override (int SpawnMap, int SpawnSlot) GetSpawnIdentity(int mapNum, int slot) => (SpawnMapNum, SpawnSlot);
}
