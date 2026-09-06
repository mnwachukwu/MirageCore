using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Server-authoritative blood as a per-map LIST of overlapping <see cref="BloodPool"/> rectangles (see
/// <see cref="GameWorld.MapBlood"/>).  A bleed is a footprint rectangle R (the bleeder's size×size tiles at its
/// anchor); <see cref="Deposit"/> merges it into the pool list by rectangle math:
/// <list type="bullet">
/// <item>ENVELOPED — some pool already contains R → just feed that pool (no new pool).</item>
/// <item>OTHERWISE — drop a new size-B pool at R, ABSORB every pool it fully contains (fold their amount in,
/// drop them), and add the hit amount to every pool it only PARTIALLY overlaps.</item>
/// </list>
/// This keeps one invariant — no pool is ever fully inside another; only partial overlaps coexist — so blood
/// collapses to the fewest pools while big/small decals still overlap where they genuinely stick out.
/// <para>A game-loop tick (<see cref="Constants.BloodTickIntervalMs"/>) decays every pool and, for any map a
/// deposit touched (Dirty), broadcasts that map's WHOLE current pool list (full-list replace — a merged-away
/// pool just drops out, so there is no per-pool removal wire).  Pure decay is never broadcast: each client
/// replays the same linear decay locally, so a fading map costs zero bandwidth.  Game thread only; no locks.</para>
/// </summary>
public sealed class BloodSystem : GameSystem
{
    private readonly GameWorld _world;

    // Fixed timestep: the loop drives this tick at BloodTickIntervalMs, so decay uses a constant dt.  (The client
    // fades with real per-frame dt; the server copy only feeds broadcasts/snapshots, where a small dt difference
    // is cosmetically irrelevant.)
    private const float Dt = Constants.BloodTickIntervalMs / 1000f;

    // Reused packing buffer (worst case = every pool). A right-sized copy is taken per send.  Safe
    // to share: every caller runs on the single game thread and never nests.
    private readonly List<byte> _payload = new(Constants.MaxMapBloodPools * BloodUpdatePacket.BytesPerPool);

    public BloodSystem(GameWorld world, IPacketDispatcher dispatcher)
        : base(dispatcher)
    {
        _world = world;
    }

    /// <summary>Deposit a bleed over a size×size footprint whose top-left tile is (x,y), sized by
    /// <paramref name="intensity"/> (see <see cref="Constants.BloodDepositStrength"/>).  Merges into the map's
    /// pool list per the rectangle rules in the type doc.  <paramref name="x"/>/<paramref name="y"/> is the
    /// anchor (always on-grid); the footprint may extend past the +x/+y edge (rendered spilling across a seam).
    /// Game thread only.</summary>
    public void Deposit(int mapNum, int x, int y, float intensity, int size = 1, WorldLayer layer = WorldLayer.Ground)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        if (!_world.Maps[mapNum].Contains(x, y)) return;   // anchor tile must be on this map's grid
        if (intensity <= 0f) return;
        size = Math.Clamp(size, 1, Constants.MaxNpcSize);
        float d = intensity * Constants.BloodPerHitScale;   // intensity may exceed 1 (near-death closeness boost)
        if (d <= 0f) return;

        if (!_world.MapBlood.TryGetValue(mapNum, out var field))
            _world.MapBlood[mapNum] = field = new BloodField();
        var pools = field.Pools;

        bool enveloped = false;
        List<BloodPool>? absorb = null;
        foreach (var p in pools)
        {
            if (p.Layer != layer) continue;   // two-layer world: only same-layer pools merge
            if (!RectIntersects(x, y, size, p.X, p.Y, p.Size)) continue;
            if (RectContains(p.X, p.Y, p.Size, x, y, size))          // p already contains the whole footprint
            {
                Redarken(p, d);
                enveloped = true;
            }
            else if (RectContains(x, y, size, p.X, p.Y, p.Size))     // the footprint fully contains p → absorb it
            {
                (absorb ??= new()).Add(p);
            }
            else                                                     // partial overlap → feed it too
            {
                Redarken(p, d);
            }
        }

        // Enveloped => the footprint sits entirely inside a bigger-or-equal pool, so no new decal (the invariant
        // guarantees nothing is fully contained in the footprint here, so there is nothing to absorb).
        if (enveloped)
        {
            field.Dirty = true;
            return;
        }

        var n = new BloodPool { X = x, Y = y, Size = size, Layer = layer };
        if (absorb is not null)
            foreach (var p in absorb) { n.Amount += p.Amount; pools.Remove(p); }
        Redarken(n, d);
        pools.Add(n);
        EnforcePoolCap(pools);
        field.Dirty = true;
    }

    /// <summary>A wounded entity dripping as it moves: leaves one trail drip (<see cref="Constants.BloodTrailStrength"/>)
    /// at (x,y) ONLY if that tile isn't already under a live pool — so it reads as a trail rather than
    /// re-darkening ground the entity revisits.  Game thread only.</summary>
    public void DepositTrail(int mapNum, int x, int y, int size = 1, WorldLayer layer = WorldLayer.Ground)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        if (!_world.Maps[mapNum].Contains(x, y)) return;
        if (_world.MapBlood.TryGetValue(mapNum, out var field))
        {
            foreach (var p in field.Pools)
            {
                if (p.Layer == layer && p.Amount > Constants.BloodVisibleEpsilon && RectContains(p.X, p.Y, p.Size, x, y, 1))
                    return;   // this tile is already bloody on this layer
            }
        }

        Deposit(mapNum, x, y, Constants.BloodTrailStrength, size, layer);
    }

    /// <summary>Send the current pool list of a map to a single client that just began observing it (login, warp,
    /// seamless neighbor reveal).  <c>Reset = true</c>.  Skipped when the map has no live pool.</summary>
    public void SendSnapshot(int index, int mapNum)
    {
        if (!_world.MapBlood.TryGetValue(mapNum, out var field)) return;
        if (!BuildPayload(field)) return;   // no live pool worth sending
        _dispatcher.SendTo(index, new BloodUpdatePacket { MapNum = mapNum, Reset = true, Pools = _payload.ToArray() });
    }

    /// <summary>Game-loop tick: decay every active map, broadcast the pool list of any map a deposit touched, free
    /// maps whose last pool decayed away.</summary>
    public void Tick()
    {
        if (_world.MapBlood.Count == 0) return;

        List<int>? dryMaps = null;
        foreach (var (mapNum, field) in _world.MapBlood)
        {
            Decay(field);
            if (field.Dirty)
            {
                Broadcast(mapNum, field);
                field.Dirty = false;
            }
            if (field.Pools.Count == 0) (dryMaps ??= new()).Add(mapNum);
        }
        if (dryMaps is not null)
            foreach (int m in dryMaps) _world.MapBlood.Remove(m);
    }

    // ── Simulation ─────────────────────────────────────────────────────────────

    private static void Decay(BloodField field)
    {
        // Linear decay so lifetime is proportional to amount (bigger pools last longer).  Decay does NOT set
        // Dirty — the client mirrors this exact step locally, so decay never goes on the wire.  A pool that dries
        // is dropped here AND on every client (both remove at the visibility floor), so it needs no removal wire.
        float d = Constants.BloodDissipationPerSec * Dt;
        var pools = field.Pools;
        for (int i = pools.Count - 1; i >= 0; i--)
        {
            var p = pools[i];
            p.Amount -= d;
            if (p.Amount <= Constants.BloodVisibleEpsilon) pools.RemoveAt(i);   // dried out
        }
    }

    private static void EnforcePoolCap(List<BloodPool> pools)
    {
        // Bound per-map pool count: evict the faintest (least-visible) pool past the cap.  Merge + decay usually
        // keep the list far under this; it is only a runaway guard for a prolonged multi-front battle.
        while (pools.Count > Constants.MaxMapBloodPools)
        {
            int min = 0;
            for (int i = 1; i < pools.Count; i++)
                if (pools[i].Amount < pools[min].Amount) min = i;
            pools.RemoveAt(min);
        }
    }

    // A deposit REDARKENS a pool: accumulate its amount (capped) and reset Peak so freshness (= Amount/Peak) = 1.
    private static void Redarken(BloodPool p, float d)
    {
        p.Amount = Math.Min(Constants.BloodMaxTileAmount, p.Amount + d);
        p.Peak = p.Amount;
    }

    // Does rect A (ax,ay,aSize) overlap rect B (bx,by,bSize) on at least one tile?
    private static bool RectIntersects(int ax, int ay, int aSize, int bx, int by, int bSize) =>
        ax < bx + bSize && bx < ax + aSize && ay < by + bSize && by < ay + aSize;

    // Does the OUTER rect (ox,oy,oSize) fully contain the INNER rect (ix,iy,iSize)?
    private static bool RectContains(int ox, int oy, int oSize, int ix, int iy, int iSize) =>
        ox <= ix && ix + iSize <= ox + oSize && oy <= iy && iy + iSize <= oy + oSize;

    // ── Broadcast ──────────────────────────────────────────────────────────────

    private void Broadcast(int mapNum, BloodField field)
    {
        var observers = _world.MapObservers[mapNum];
        if (observers.Count == 0) return;
        if (!BuildPayload(field)) return;
        _dispatcher.SendToObservers(observers, new BloodUpdatePacket { MapNum = mapNum, Reset = false, Pools = _payload.ToArray() });
    }

    // Packs every live pool into _payload, BloodUpdatePacket.BytesPerPool each: x and y as little-endian 16-bit, then size,
    // amount, freshness, layer. Returns false (and leaves _payload empty) when the map has no pool worth
    // sending.
    //
    // The coordinates are 16-bit because a tile coordinate has to be able to name any tile on the map, and
    // a byte stops at 255. Two bytes per pool against a 128-pool cap is a few hundred bytes on a broadcast
    // that only happens when blood actually changed.
    private bool BuildPayload(BloodField field)
    {
        _payload.Clear();
        foreach (var p in field.Pools)
        {
            if (p.Amount <= Constants.BloodVisibleEpsilon) continue;
            _payload.Add((byte)(p.X & 0xFF));
            _payload.Add((byte)((p.X >> 8) & 0xFF));
            _payload.Add((byte)(p.Y & 0xFF));
            _payload.Add((byte)((p.Y >> 8) & 0xFF));
            _payload.Add((byte)p.Size);
            _payload.Add(Quantize(p.Amount));
            _payload.Add(QuantizeFresh(p.Amount, p.Peak));
            _payload.Add((byte)p.Layer);
        }
        return _payload.Count > 0;
    }

    private static byte Quantize(float amount)
    {
        float f = Math.Clamp(amount, 0f, Constants.BloodMaxTileAmount) / Constants.BloodMaxTileAmount;
        int q = (int)MathF.Round(f * 255f, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(q, 0, 255);
    }

    // Freshness = amount/peak in 0..1 (1 when peak is unset), the client's OPACITY.
    private static byte QuantizeFresh(float amount, float peak)
    {
        float f = peak > 0f ? Math.Clamp(amount / peak, 0f, 1f) : 1f;
        return (byte)Math.Clamp((int)MathF.Round(f * 255f, MidpointRounding.AwayFromZero), 0, 255);
    }
}
