using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Changing what a body's attributes say, and telling whoever is entitled to know.
///
/// <para><b>This is the whole of Core's interest in attributes.</b> It stores them, ships the ones a
/// game declared, and never reads one. A game calls <see cref="Set"/> and the right values reach the
/// right sockets; what the key means, when it should change, and what follows from the change are all
/// the game's.</para>
///
/// <para><b>The write and the send are one call on purpose.</b> A bag is a plain dictionary and nothing
/// stops a game writing to it directly — that is the correct thing to do for a value nobody watches.
/// But a value that IS watched and is written directly simply stops updating on every client, with no
/// error and no symptom until somebody notices a stale number. Routing the watched case through one
/// method is what makes the quiet failure impossible to reach by accident.</para>
/// </summary>
public sealed class AttributeSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly WorldQueries _queries;

    public AttributeSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _queries = new WorldQueries(world, pm);
    }

    /// <summary>Writes one key on a body and syncs it. Returns false when the body is not in the world,
    /// in which case nothing was written either.</summary>
    public bool Set(EntityHandle who, string key, AttributeValue value)
        => SetMany(who, [new KeyValuePair<string, AttributeValue>(key, value)]);

    /// <summary>Writes several keys on a body and syncs them together, so a change that spans two
    /// values — a currency spent and a thing bought — does not reach a client as two states, one of
    /// which was never true.</summary>
    public bool SetMany(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values)
    {
        var bag = BagOf(who);
        if (bag is null) return false;

        foreach (var (key, value) in values) bag[key] = value;
        Sync(who, bag, [.. values.Select(v => v.Key)]);
        return true;
    }

    /// <summary>Removes a key and syncs the body's remaining visible values.
    ///
    /// <para>A removal cannot be expressed as a sync entry — the packet says "this is the new value",
    /// and there is no value that means "gone". So the whole visible set is re-sent and the client's
    /// copy is replaced rather than patched. Removing a live key is rare enough that the extra bytes
    /// are worth not inventing a second packet shape for.</para></summary>
    public bool Remove(EntityHandle who, string key)
    {
        var bag = BagOf(who);
        if (bag is null || !bag.Remove(key)) return false;

        Sync(who, bag, keys: null);
        return true;
    }

    /// <summary>Reads a body's bag, or null when it is not in the world. A game holding this may read
    /// and write it freely; only a WATCHED key written this way stops reaching clients, which is what
    /// <see cref="Set"/> exists for.</summary>
    public AttributeBag? BagOf(EntityHandle who)
    {
        if (who.IsPlayer)
        {
            // Against THIS server's roster: an operator sets how many players it holds, and a handle may
            // carry any number the protocol allows. A well-formed one can still name no slot here.
            if (who.PlayerIndex < 1 || who.PlayerIndex > _pm.Slots) return null;

            var sp = _pm[who.PlayerIndex];
            return sp.IsPlaying ? sp.Char.Attributes : null;
        }

        // An NPC is NAMED by where it spawns and may be standing two maps away, its home slot vacated
        // and reserved. Resolving the identity rather than indexing the slot is what keeps a game's
        // values readable on a body that is chasing somebody across the world.
        return who.IsNpc ? _queries.ResolveNpc(who.SpawnMap, who.SpawnSlot)?.Record.Attributes : null;
    }

    /// <summary>Ships <paramref name="keys"/> (or everything visible, when null) to everyone entitled:
    /// the body's own player as an owner, and every observer of its map as an onlooker.</summary>
    private void Sync(EntityHandle who, AttributeBag bag, IReadOnlyCollection<string>? keys)
    {
        var schema = _world.Attributes;
        if (schema.Declarations.Count == 0) return;   // nothing is watched; there is nobody to tell

        int mapNum = MapOf(who);
        if (mapNum <= 0) return;

        // The owner first and separately: they see their own values AND the public ones, which is a
        // different projection from the one everybody else gets rather than a superset sent twice.
        if (who.IsPlayer)
        {
            var own = PacketBuilder.AttributeSync(who, bag, schema, AttributeVisibility.Owner, keys);
            if (own is not null) _dispatcher.SendTo(who.PlayerIndex, own);
        }

        var seen = PacketBuilder.AttributeSync(who, bag, schema, AttributeVisibility.Viewport, keys);
        if (seen is null) return;

        foreach (int observer in _world.MapObservers[mapNum])
        {
            if (observer == who.PlayerIndex) continue;   // already told, with more
            _dispatcher.SendTo(observer, seen);
        }
    }

    /// <summary>Which map a body is standing on, or 0 when it is nowhere.</summary>
    private int MapOf(EntityHandle who)
    {
        if (who.IsPlayer)
        {
            var sp = _pm[who.PlayerIndex];
            return sp.IsPlaying ? sp.Char.Map : 0;
        }

        // Where the body is, not where it is named after: the onlookers entitled to see a chaser's
        // values are the ones on the map it is actually standing on.
        return who.IsNpc ? _queries.ResolveNpc(who.SpawnMap, who.SpawnSlot)?.CurrentMap ?? 0 : 0;
    }
}
