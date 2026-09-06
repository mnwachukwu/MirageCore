using Mirage.Shared;

namespace Mirage.Client.Core.State;

/// <summary>
/// Client-side NPC slot on the current map.
/// Mirrors MapNpcRecord but adds MaxHp (sent by server in MapNpcsPacket) and
/// keeps all the client rendering state (offsets, animation flags).
///
/// Not sealed: <see cref="ClientTraversalNpc"/> inherits it so a chasing NPC visiting a
/// neighbor map reuses the same offset/animation/bar rendering as a native slot NPC.
/// </summary>
public class ClientMapNpc
{
    public int Num { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public int Sp { get; set; }
    public int MaxSp { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    // Two-layer world: which logical layer (ground vs bridge-top fringe) this NPC is on, for layer-correct
    // render occlusion + collision. Server-authoritative (move/spawn/mapnpcs/traversal packets carry it).
    public WorldLayer Layer { get; set; }
    // The layer this NPC was on BEFORE the current move-slide (see PlayerRecord.PrevLayer): while the walk-offset
    // animates, a cross-layer step renders on the higher layer so the sprite isn't occluded mid-slide.
    public WorldLayer PrevLayer { get; set; }
    public MovementType Moving { get; set; }
    public bool Attacking { get; set; }
    public long AttackTimer { get; set; }
    public float XOffset { get; set; }
    public float YOffset { get; set; }
    public long LastCombatMs { get; set; }
    public bool HasTarget { get; set; }

    // Animated display values for world-space bars (-1f = uninitialized → snap on first Tick)
    public float DispHp { get; set; } = -1f;
    public float DispMp { get; set; } = -1f;
    public float DispSp { get; set; } = -1f;
    // While Environment.TickCount64 < this, the HP bar holds instead of animating — used to keep the bar
    // in sync with an in-flight spell bolt (hit-timing deferral). 0 = not holding.
    public long BarHoldUntilMs { get; set; }

    // Chat bubble — AttackSay from this NPC, anchored above its head. Same head+drifter model as
    // PlayerRecord; Color is GameColor.BrightRed (hostile) or .BrightGreen (friendly/shopkeeper).
    public string? ChatBubbleText { get; set; }
    public long ChatBubbleEndMs { get; set; }
    public int ChatBubbleColor { get; set; }
    public List<NpcChatBubbleDrifter>? ChatBubbleDrifters { get; set; }

    /// <summary>Replace this slot's state with a server-authoritative snapshot. Returns true if
    /// the snapshot is the SAME NPC standing on the SAME tile (i.e. a mid-step re-sync arriving
    /// during a seam crossing) — in which case the caller should NOT reset the walk/attack
    /// interpolation, so the in-flight slide doesn't snap. Returns false on a real state change
    /// (new NPC in the slot, or a position update); the interp fields are cleared here in that
    /// branch. Combat-stamp conversion uses <paramref name="nowMs"/> as the local clock.</summary>
    public bool ApplySnapshot(int num, int hp, int maxHp, int mp, int maxMp, int sp, int maxSp,
                              int x, int y, Direction dir, WorldLayer layer, int msSinceCombat, bool hasTarget, long nowMs)
    {
        bool sameInPlace = Num == num && X == x && Y == y;
        Num = num;
        Hp = hp;
        MaxHp = maxHp;
        Mp = mp;
        MaxMp = maxMp;
        Sp = sp;
        MaxSp = maxSp;
        X = x;
        Y = y;
        Dir = dir;
        Layer = layer;
        HasTarget = hasTarget;
        if (msSinceCombat != int.MaxValue) LastCombatMs = nowMs - msSinceCombat;
        else if (!sameInPlace) LastCombatMs = 0;
        if (!sameInPlace)
        {
            Moving = MovementType.None;
            Attacking = false;
            XOffset = 0;
            YOffset = 0;
        }
        return sameInPlace;
    }
}

public readonly record struct NpcChatBubbleDrifter(string Text, int Color, long DemotedMs);

/// <summary>
/// A hostile NPC chasing a player across a seamless border.  It lives outside the per-map slot
/// arrays — addressed by its permanent <c>(SpawnMapNum, SpawnSlot)</c> identity — and renders on
/// whichever grid cell holds <see cref="CurrentMapNum"/>.  Mirrors the server's TraversalNpcRecord.
/// </summary>
public sealed class ClientTraversalNpc : ClientMapNpc
{
    public int SpawnMapNum { get; set; }
    public int SpawnSlot { get; set; }
    public int CurrentMapNum { get; set; }
}
