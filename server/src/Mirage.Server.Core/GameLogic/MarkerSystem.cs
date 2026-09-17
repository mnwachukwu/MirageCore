using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Marks on the ground: putting one down, taking it away, and telling the people who may see it.
///
/// <para><b>Core performs this; it does not decide what any of it means.</b> Keeping a list per map,
/// working out who is owed which of them, sending only the maps that changed, and catching up somebody
/// who has just begun watching is the same machinery whatever is being marked. What a game supplies is
/// the call: where, what color, how wide, how far along, and who can see it.</para>
///
/// <para><b>Nothing in Core calls <see cref="Mark"/>.</b> An engine with no module loaded has ground
/// that can be marked and nothing that marks it.</para>
/// </summary>
public sealed class MarkerSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;

    public MarkerSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher) : base(dispatcher)
    {
        _world = world;
        _pm = pm;
    }

    /// <summary>Puts a marker down, or replaces the one already under that name.
    ///
    /// <para>Replacing rather than stacking keeps a marker that MOVES, or whose meter is counting,
    /// to one call rather than a remove and a place — and is why an id is required. A moved
    /// marker leaves its old map, so both maps are told.</para></summary>
    public bool Mark(WorldMarker marker)
    {
        if (marker is null || string.IsNullOrWhiteSpace(marker.Id)) return false;
        if (!_world.IsRealMap(marker.At.Map)) return false;

        var map = _world.Maps[marker.At.Map];
        if (marker.At.X < 0 || marker.At.Y < 0 || marker.At.X >= map.Width || marker.At.Y >= map.Height) return false;

        // One marker, one place: it is lifted from wherever it sits before it is put down. A marker
        // crossing maps changes two lists, and the map it leaves is owed the news as much as the map it
        // arrives on.
        int left = Take(marker.Id);

        if (!_world.Markers.TryGetValue(marker.At.Map, out var here))
            _world.Markers[marker.At.Map] = here = [];

        here.Add(marker);

        Tell(marker.At.Map);
        if (left > 0 && left != marker.At.Map) Tell(left);

        return true;
    }

    /// <summary>Takes one away by name. False when nothing was there under it, which is an ordinary
    /// answer for a game clearing up after something that ended on its own.</summary>
    public bool Unmark(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        int was = Take(id);
        if (was <= 0) return false;

        Tell(was);
        return true;
    }

    /// <summary>Whether a square is inside a marker's ring.
    ///
    /// <para>🔴 <b>The ring drawn and the ring asked about are the same marker.</b> A game scoring the
    /// ground inside a capture point asks this rather than doing the arithmetic itself, because the two
    /// answers drifting apart is invisible: the line a player can see would sit somewhere other than the
    /// line that counts, and every complaint about it would be about the wrong thing.</para>
    ///
    /// <para>Measured from tile center to tile center and inclusive, so the set of squares this answers
    /// yes for is exactly the staircase the client outlines. False for another map, and for the other
    /// plane — a ring on a bridge covers the bridge, and the water under it is somewhere else.</para></summary>
    public bool Inside(string id, WorldPlace place)
    {
        if (Find(id) is not { } found || found.Radius <= 0) return false;
        if (found.At.Map != place.Map || found.At.Layer != place.Layer) return false;

        int dx = place.X - found.At.X;
        int dy = place.Y - found.At.Y;

        return dx * dx + dy * dy <= found.Radius * found.Radius;
    }

    /// <summary>Sends one map's marks to a single client that has just begun watching it — a login, a
    /// warp, or a seamless neighbor coming into view.
    ///
    /// <para>Without this a joiner sees an unmarked map until something else changes, because a change
    /// is the only other thing that sends one.</para></summary>
    public void SendSnapshot(int index, int mapNum)
    {
        if (!_world.Markers.TryGetValue(mapNum, out var here) || here.Count == 0) return;

        var mine = Build(mapNum, here, index);
        if (mine.Markers.Count > 0) _dispatcher.SendTo(index, mine);
    }

    /// <summary>Everything on one map, filtered to what this client may see. Public so the join
    /// handshake sends a newly-watched map through the same shape a change does.</summary>
    public static MarkerUpdatePacket Build(int mapNum, IReadOnlyList<WorldMarker> here, int index)
    {
        var mine = new List<MarkerUpdatePacket.Entry>(here.Count);
        var who = EntityHandle.ForPlayer(index);

        for (int i = 0; i < here.Count; i++)
        {
            var m = here[i];

            // No audience means everybody who can see the square, which is the ordinary case.
            if (m.SeenBy.Count > 0 && !m.SeenBy.Contains(who)) continue;

            mine.Add(new MarkerUpdatePacket.Entry(m.At.X, m.At.Y, m.Rgb, m.Label, m.Radius,
                                                  m.Value, m.Ceiling, m.At.Layer));
        }

        return new MarkerUpdatePacket { MapNum = mapNum, Markers = mine };
    }

    /// <summary>One map's list, to everybody watching it — each of them their own, because a marker may
    /// name who sees it and two people on one square can be owed different lists.
    ///
    /// <para>⚠ An empty list is sent as well as a full one. A client that was shown a marker and is no
    /// longer owed it has no other way to learn that.</para></summary>
    private void Tell(int mapNum)
    {
        var here = _world.Markers.TryGetValue(mapNum, out var list) ? list : [];

        foreach (int index in _world.MapObservers[mapNum])
        {
            if (_pm[index].IsPlaying) _dispatcher.SendTo(index, Build(mapNum, here, index));
        }
    }

    /// <summary>Removes the marker under that name wherever it is, and answers which map it left. Zero
    /// when nothing was under it.</summary>
    private int Take(string id)
    {
        foreach (var (mapNum, here) in _world.Markers)
        {
            for (int i = 0; i < here.Count; i++)
            {
                if (!string.Equals(here[i].Id, id, StringComparison.Ordinal)) continue;

                here.RemoveAt(i);
                if (here.Count == 0) _world.Markers.Remove(mapNum);

                return mapNum;
            }
        }

        return 0;
    }

    private WorldMarker? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (var (_, here) in _world.Markers)
        {
            for (int i = 0; i < here.Count; i++)
                if (string.Equals(here[i].Id, id, StringComparison.Ordinal)) return here[i];
        }

        return null;
    }
}
