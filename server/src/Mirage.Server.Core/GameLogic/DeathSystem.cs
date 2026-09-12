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
/// </summary>
public sealed class DeathSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly MovementSystem _movement;
    private readonly IReadOnlyList<IDeathPolicy> _policies;

    public DeathSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       MovementSystem movement, IEnumerable<IDeathPolicy>? policies = null)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _movement = movement;
        _policies = policies is null ? [] : [.. policies];
    }

    /// <summary>Ends <paramref name="who"/>, if every policy allows it, and returns whether it happened.
    ///
    /// <para>The order is fixed and matters: refuse, then cost, then move. A policy that sheds inventory
    /// runs while the body is still where it fell, so what it drops lands on the tile the player can go
    /// back for — the one place a corpse is worth looking.</para></summary>
    public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "")
    {
        if (!who.IsPlayer) return false;   // NPC death is a spawner's business, not a warp's

        var sp = _pm[who.PlayerIndex];
        if (!sp.IsPlaying) return false;

        var death = new Death(who, killer, causeKey);
        foreach (var policy in _policies)
            if (!policy.MayDie(in death).Allowed) return false;

        foreach (var policy in _policies) policy.OnDied(in death);

        var (map, x, y) = HomeFor(death, sp.Char);
        _movement.PlayerWarp(who.PlayerIndex, map, x, y);
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
