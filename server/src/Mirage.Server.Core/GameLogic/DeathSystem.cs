using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// A body stops, and comes back somewhere.
///
/// <para><b>That is the whole primitive, and it is here because it is about the map.</b> Taking a body
/// out of play and putting it back is the engine's job — it is the same warp, viewport and roster work
/// every other move goes through, and a game module reaching in to do it would be reimplementing
/// movement. What a game supplies is everything else: whether the death happens, what it costs, and
/// where the body belongs afterwards.</para>
///
/// <para><b>Nothing in Core calls <see cref="Kill"/>.</b> Core has no rule that ends a life — no hit
/// points, no hunger, no drowning — so the only caller is a game module. An engine with no module
/// loaded has deaths it knows how to perform and nothing that asks for one, which is the correct
/// behavior rather than a gap.</para>
///
/// <para><b>One door, two kinds of body.</b> A creature killed through the same call goes to
/// <see cref="SpawnSystem.KillNpc"/>, because a creature comes back on a spawn clock rather than at a
/// respawn point. A game asks for a death the same way either way and is answered the same way.</para>
/// </summary>
public sealed class DeathSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly MovementSystem _movement;
    private readonly SpawnSystem _spawns;
    private readonly IReadOnlyList<IDeathPolicy> _policies;
    private readonly ILogger<DeathSystem> _logger;

    public DeathSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       MovementSystem movement, SpawnSystem spawns,
                       IEnumerable<IDeathPolicy>? policies = null,
                       ILogger<DeathSystem>? logger = null)
        : base(dispatcher)
    {
        _logger = logger ?? NullLogger<DeathSystem>.Instance;
        _world = world;
        _pm = pm;
        _movement = movement;
        _spawns = spawns;
        _policies = policies is null ? [] : [.. policies];
    }

    /// <summary>Ends <paramref name="who"/>, if every policy allows it, and returns whether it happened.
    ///
    /// <para>The order is fixed and matters: refuse, then cost, then move. A policy that sheds inventory
    /// runs while the body is still where it fell, so what it drops lands on the tile the player can go
    /// back for — the one place a corpse is worth looking.</para></summary>
    public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "")
    {
        // A creature's death is a slot's life rather than a warp, and the policies below are written
        // about a PLAYER - MayDie and OnDied take one, and a handler handed the wrong kind of body is
        // worse than one that is not called. So it goes to the spawner whole, and a game says what a
        // kill was worth at its own call site rather than through a handler about somebody else.
        if (who.IsNpc) return _spawns.KillNpc(who, killer);

        if (!who.IsPlayer) return false;

        var sp = _pm[who.PlayerIndex];
        if (!sp.IsPlaying) return false;

        var death = new Death(who, killer, causeKey);
        foreach (var policy in _policies)
        {
            var said = policy.MayDie(in death);
            if (said.Allowed) continue;

            // A refused death is a rule working, and it is indistinguishable from a broken one at the
            // call site: the caller gets false either way. Saying which policy said no, and why, is the
            // difference between reading a log and guessing.
            _logger.LogDebug("Death refused for player {Index} by {Policy}: {Reason}",
                             who.PlayerIndex, policy.GetType().Name, said.ReasonKey);
            return false;
        }

        _logger.LogDebug("Player {Index} died ({Cause}); {Policies} policy(s) asked.",
                         who.PlayerIndex, causeKey, _policies.Count);

        foreach (var policy in _policies) policy.OnDied(in death);

        var (map, x, y) = HomeFor(death, sp.Char);

        // A game that put the body OUT OF ACTION while the policies ran is keeping it here: the body
        // lies where it fell and moves when it gets up, which is what a corpse with a timer over it
        // means. Where it will come back is settled now, while the death is still in hand, and read
        // again by Rise.
        if (sp.Char.Downed)
        {
            sp.RiseMap = map;
            sp.RiseX = x;
            sp.RiseY = y;
            return true;
        }

        _movement.PlayerWarp(who.PlayerIndex, map, x, y);
        return true;
    }

    /// <summary>They asked to get up, and the deadline has passed.
    ///
    /// <para>The one way out of the downed state. Refused while the clock is still running, so a client
    /// asking early is ignored rather than trusted - the deadline is the server’s.</para>
    ///
    /// <para>Order matters: the state is cleared, then the body is moved, then the game is told. A rule
    /// restoring pools in <see cref="IDeathPolicy.OnRose"/> is writing onto a body that is already
    /// standing where it will be, and is no longer refused for being out of action.</para></summary>
    public bool Rise(EntityHandle who)
    {
        if (!who.IsPlayer) return false;

        var sp = _pm[who.PlayerIndex];
        if (!sp.IsPlaying || !sp.Char.Downed) return false;
        if (NowUtc < sp.Char.RespawnReadyUtc) return false;

        sp.Char.Downed = false;
        sp.Char.RespawnReadyUtc = 0;

        // A rise point that named a tile which is no longer there lands on real ground rather than
        // nowhere: coming back is the one move that may never be refused.
        var home = Config.Spawn.HomeFor(sp.Char);
        var (map, x, y) = sp.RiseMap > 0
            ? _world.RepairPosition(sp.RiseMap, sp.RiseX, sp.RiseY, home)
            : _world.RepairPosition(home.Map, home.X, home.Y, home);

        sp.RiseMap = 0;
        _movement.PlayerWarp(who.PlayerIndex, map, x, y);

        foreach (var policy in _policies) policy.OnRose(who);
        return true;
    }

    /// <summary>Where the body comes back: the first policy that names a place, or this world's own home
    /// for that character. Repaired against the map either way, so a policy naming a tile that is not
    /// there lands somewhere real rather than nowhere.</summary>
    private (int Map, int X, int Y) HomeFor(in Death death, Mirage.Shared.Records.PlayerRecord p)
    {
        var home = Config.Spawn.HomeFor(p);
        foreach (var policy in _policies)
        {
            var chosen = policy.RespawnFor(in death);
            if (!chosen.IsSet) continue;
            return _world.RepairPosition(chosen.Map, chosen.X, chosen.Y, home);
        }
        return _world.RepairPosition(home.Map, home.X, home.Y, home);
    }
}
