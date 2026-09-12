using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Stains on the ground: putting one down, drying them out, and telling the people who can see them.
///
/// <para><b>Core performs this; it does not decide when.</b> Accumulating overlapping rectangles, merging
/// them, decaying them on a shared clock, and broadcasting only what actually changed is real machinery,
/// and it is the same machinery whatever spilled. What a game supplies is the call: where, how big, what
/// color, how much.</para>
///
/// <para><b>Nothing in Core calls <see cref="Deposit"/>.</b> An engine with no module loaded has ground
/// that can be stained and nothing that stains it.</para>
/// </summary>
public sealed class DecalSystem : GameSystem
{
    private readonly GameWorld _world;

    public DecalSystem(GameWorld world, IPacketDispatcher dispatcher) : base(dispatcher) => _world = world;

    /// <summary>Puts a stain down, or feeds one already there.
    ///
    /// <para>A deposit onto a stain with the same rectangle and layer FEEDS it rather than stacking a second
    /// one — otherwise a body bleeding on one tile would pile up a hundred rectangles that draw identically
    /// and cost the wire every one of them.</para></summary>
    /// <param name="mapNum">Which map.</param>
    /// <param name="x">Top-left tile, map-local.</param>
    /// <param name="y">Top-left tile, map-local.</param>
    /// <param name="size">Edge length in tiles; a big body leaves a big stain.</param>
    /// <param name="layer">Ground or the deck above it.</param>
    /// <param name="amount">How much, in the same dimensionless units <see cref="Constants.DecalMaxAmount"/>
    /// caps. Non-positive deposits nothing.</param>
    public void Deposit(int mapNum, int x, int y, int size, WorldLayer layer, float amount)
    {
        if (amount <= 0f || size < 1) return;
        if (!_world.IsRealMap(mapNum)) return;

        var map = _world.Maps[mapNum];
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return;
        size = Math.Min(size, Math.Min(map.Width - x, map.Height - y));

        if (!_world.MapDecals.TryGetValue(mapNum, out var field))
            _world.MapDecals[mapNum] = field = new DecalField();

        foreach (var d in field.Decals)
        {
            if (d.X != x || d.Y != y || d.Size != size || d.Layer != layer) continue;
            d.Amount = Math.Min(d.Amount + amount, Constants.DecalMaxAmount);
            d.Peak = d.Amount;          // a fresh spill darkens the stain back to full
            field.Dirty = true;
            return;
        }

        // Past the cap the faintest goes, not the oldest: what a viewer would miss least is what is nearly
        // dry, and an oldest-first rule would drop the big stain under a body that is still standing there.
        if (field.Decals.Count >= Constants.MaxMapDecals)
        {
            int faintest = 0;
            for (int i = 1; i < field.Decals.Count; i++)
                if (field.Decals[i].Amount < field.Decals[faintest].Amount) faintest = i;
            field.Decals.RemoveAt(faintest);
        }

        float put = Math.Min(amount, Constants.DecalMaxAmount);
        field.Decals.Add(new Decal { X = x, Y = y, Size = size, Layer = layer, Amount = put, Peak = put });
        field.Dirty = true;
    }

    /// <summary>Dries every map's stains by one tick and broadcasts the maps whose list actually changed.
    ///
    /// <para>Decay alone never broadcasts: the client runs the same linear fade from the same constant, so
    /// a drying map converges without a byte on the wire. A stain that dries out completely IS a change —
    /// it leaves the list — so that map is re-sent.</para></summary>
    public void Tick()
    {
        if (_world.MapDecals.Count == 0) return;
        float dry = Constants.DecalDryingPerSec * (Constants.DecalTickIntervalMs / 1000f);

        List<int>? emptied = null;
        foreach (var (mapNum, field) in _world.MapDecals)
        {
            for (int i = field.Decals.Count - 1; i >= 0; i--)
            {
                var d = field.Decals[i];
                d.Amount -= dry;
                if (d.Amount > Constants.DecalVisibleEpsilon) continue;
                field.Decals.RemoveAt(i);
                field.Dirty = true;     // gone is a change the client cannot derive
            }

            if (field.Decals.Count == 0) (emptied ??= []).Add(mapNum);
            if (!field.Dirty) continue;

            field.Dirty = false;
            if (_world.MapObservers[mapNum].Count > 0)
                SendToMap(_world, mapNum, Build(mapNum, field));
        }

        // A map nobody has spilled on any more carries nothing at all, so an idle world holds no state.
        foreach (int m in emptied ?? []) _world.MapDecals.Remove(m);
    }

    /// <summary>Sends one map's current stains to a single client that has just begun observing it — a login,
    /// a warp, or a seamless neighbor coming into view.
    ///
    /// <para>Without this a joiner sees a stained map as clean until something spills on it again, because the
    /// tick broadcasts only what CHANGED.</para></summary>
    public void SendSnapshot(int index, int mapNum)
    {
        if (!_world.MapDecals.TryGetValue(mapNum, out var field) || field.Decals.Count == 0) return;
        _dispatcher.SendTo(index, Build(mapNum, field));
    }

    /// <summary>One map's whole current list, as the client will receive it. Public so the join handshake
    /// can send a newly-observed map's stains through the same shape the tick broadcasts.</summary>
    public static DecalUpdatePacket Build(int mapNum, DecalField field) =>
        new()
        {
            MapNum = mapNum,
            Decals = [.. field.Decals.Select(d => new DecalUpdatePacket.Entry(
                d.X, d.Y, d.Size,
                DecalUpdatePacket.Quantize(d.Amount),
                DecalUpdatePacket.Quantize(d.Peak),
                d.Layer))],
        };
}
