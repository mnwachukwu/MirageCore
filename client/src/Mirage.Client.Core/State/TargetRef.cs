namespace Mirage.Client.Core.State;

/// <summary>What kind of entity a <see cref="TargetRef"/> points at.</summary>
public enum TargetKind : byte { None, Player, Npc, Traversal }

/// <summary>
/// The local player's current target, addressable anywhere in the seamless 3×3 region — so the
/// target arrow can sit on a neighbor-map entity and tab-cycling can reach across borders.
/// Field meaning depends on <see cref="Kind"/>:
///   Player    → A = player index;            B unused
///   Npc       → A = slot;                     B = server map number of the cell it's on
///   Traversal → A = SpawnMapNum (identity);   B = SpawnSlot (identity)
/// </summary>
public readonly record struct TargetRef(TargetKind Kind, int A, int B)
{
    public bool IsNone => Kind == TargetKind.None;
}
