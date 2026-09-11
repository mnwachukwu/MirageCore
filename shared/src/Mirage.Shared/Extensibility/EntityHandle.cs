using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>What kind of body an <see cref="EntityHandle"/> names.</summary>
public enum EntitySort : byte
{
    /// <summary>Nothing. The zero value, so an unwritten handle names nobody.</summary>
    None = 0,
    Player = 1,
    Npc = 2,
}

/// <summary>
/// A stable name for one body in the world, good across map seams and safe to hold between ticks.
///
/// <para><b>An NPC is named by where it spawns, not by where it stands.</b> A spawn map and slot are
/// authored facts that do not change when the NPC walks onto the next map; a current map and slot
/// change constantly, and a handle built from them names a different NPC — or nobody — a moment later.
/// An NPC standing on its home map and one visiting from two maps away are therefore the same kind of
/// thing here, addressed the same way.</para>
///
/// <para><b>It names, it does not resolve.</b> Holding a handle says nothing about whether that body
/// is still in the world; the thing that owns the roster answers that. So a handle kept across a tick
/// is safe to keep and must be re-resolved before it is used.</para>
/// </summary>
/// <param name="Sort">Which kind of body, and whether there is one at all.</param>
/// <param name="A">A player's index, or an NPC's spawn map.</param>
/// <param name="B">Zero for a player; an NPC's spawn slot.</param>
public readonly record struct EntityHandle(EntitySort Sort, int A, int B)
{
    /// <summary>Nobody. The zero value.</summary>
    public static EntityHandle None => default;

    /// <summary>True when this names a kind of body at all.</summary>
    public bool IsSet => Sort != EntitySort.None;

    public bool IsPlayer => Sort == EntitySort.Player;
    public bool IsNpc => Sort == EntitySort.Npc;

    /// <summary>The player this names, or 0 when it does not name a player.</summary>
    [JsonIgnore] public int PlayerIndex => Sort == EntitySort.Player ? A : 0;

    /// <summary>The map an NPC spawns on, or 0 when this does not name an NPC.</summary>
    [JsonIgnore] public int SpawnMap => Sort == EntitySort.Npc ? A : 0;

    /// <summary>The spawn slot on <see cref="SpawnMap"/>, or 0 when this does not name an NPC.</summary>
    [JsonIgnore] public int SpawnSlot => Sort == EntitySort.Npc ? B : 0;

    /// <summary>The player in slot <paramref name="index"/>. Index 0 names nobody.</summary>
    public static EntityHandle ForPlayer(int index)
        => index > 0 ? new EntityHandle(EntitySort.Player, index, 0) : None;

    /// <summary>The NPC that spawns in <paramref name="spawnSlot"/> on <paramref name="spawnMap"/>.
    /// Either being non-positive names nobody.</summary>
    public static EntityHandle ForNpc(int spawnMap, int spawnSlot)
        => spawnMap > 0 && spawnSlot > 0 ? new EntityHandle(EntitySort.Npc, spawnMap, spawnSlot) : None;

    public override string ToString() => Sort switch
    {
        EntitySort.Player => $"player:{A}",
        EntitySort.Npc => $"npc:{A}/{B}",
        _ => "none",
    };
}
