namespace Mirage.Shared.Records;

/// <summary>One item lying on a map. Held in a fixed per-map array, so a free entry is one whose
/// <see cref="DropSeq"/> is 0 rather than a null slot.</summary>
public sealed class MapItemRecord
{
    // Two-layer world: which logical layer this drop sits on (ground vs bridge-top fringe). Persisted with a
    // dropped item so it reloads on the right plane; the client store mirrors it for the render pass-split.
    public WorldLayer Layer { get; set; }
    // Stable per-map identifier assigned by GameWorld.AllocateMapItemSlot. Survives across packets so
    // clients can update or remove a specific drop without ambiguity (slot is also the wire handle).
    public int Slot { get; set; }
    /// <summary>Item slot number.</summary>
    public int Num { get; set; }
    /// <summary>Stack quantity, for a currency-type item.</summary>
    public int Quantity { get; set; }
    /// <summary>Current durability of this copy.</summary>
    public int Dur { get; set; }
    /// <summary>Map-local tile X.</summary>
    public int X { get; set; }
    /// <summary>Map-local tile Y.</summary>
    public int Y { get; set; }
    /// <summary>What put it here, which decides whether the slot respawns after pickup.</summary>
    public ItemSource Source { get; set; }
    public int TaggedToPlayer { get; set; }   // 1-based player index; 0 = untagged
    public long TagExpiresAt { get; set; }    // Environment.TickCount64 ms when tag expires; 0 = untagged
    public long DropSeq { get; set; }         // Monotonic drop sequence; highest at a tile = top of stack. 0 = empty slot.
}
