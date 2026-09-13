using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// The engine behind <see cref="IWorld"/>: what a game asks for, done.
///
/// <para><b>Every method is a delegation.</b> Nothing here decides anything — the rules belong to the
/// systems it calls and to the game that called it. What this type contributes is the boundary: one
/// place where a handle off the wire is checked against a body that is actually there, so no system
/// below has to cope with a game naming something that left.</para>
///
/// <para><b>Seconds, not clocks.</b> The timed states behind these setters run on two different clocks —
/// UTC seconds for what persists across a restart, monotonic milliseconds for what does not — and one of
/// them counts forward from a start rather than down to an end. A game says how long; the conversion
/// happens here, where the clocks are known.</para>
/// </summary>
public sealed class ServerWorld : IWorld
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly AttributeSystem _attributes;
    private readonly DeathSystem _deaths;
    private readonly MovementSystem _movement;
    private readonly ItemSystem _items;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly DecalSystem _decals;
    private readonly IClock _clock;

    public ServerWorld(GameWorld world, PlayerManager pm, AttributeSystem attributes, DeathSystem deaths,
                       MovementSystem movement, ItemSystem items, JoinLeaveSystem joinLeave,
                       DecalSystem decals, IClock? clock = null)
    {
        _world = world;
        _pm = pm;
        _attributes = attributes;
        _deaths = deaths;
        _movement = movement;
        _items = items;
        _joinLeave = joinLeave;
        _decals = decals;
        _clock = clock ?? SystemClock.Instance;
    }

    // ── Who is here ───────────────────────────────────────────────────────────

    public bool IsInWorld(EntityHandle who) =>
        who.IsPlayer ? HasSlot(who.PlayerIndex) && _pm[who.PlayerIndex].IsPlaying
                     : who.IsNpc && Npc(who) is not null;

    /// <summary>Whether this server has a slot with that number at all.
    ///
    /// <para>Against THIS server's roster, not the protocol's ceiling: an operator sets how many players
    /// a server holds, and the shared maximum is the largest any server may be configured for. A handle
    /// carrying a number between the two is well-formed and still names nothing here.</para></summary>
    private bool HasSlot(int index) => index >= 1 && index <= _pm.Slots;

    public WorldPlace PlaceOf(EntityHandle who)
    {
        if (who.IsPlayer && IsInWorld(who))
        {
            var p = _pm[who.PlayerIndex].Char;
            return new WorldPlace(p.Map, p.X, p.Y);
        }

        // An NPC is NAMED by where it spawns and may be standing somewhere else; the place is where the
        // body is, which is what a game asking "where is it" means.
        return Npc(who) is { } npc ? new WorldPlace(who.SpawnMap, npc.X, npc.Y) : WorldPlace.Nowhere;
    }

    // ── What a body carries ───────────────────────────────────────────────────

    public AttributeBag? AttributesOf(EntityHandle who) => _attributes.BagOf(who);

    public bool SetAttribute(EntityHandle who, string key, AttributeValue value)
        => _attributes.Set(who, key, value);

    public bool SetAttributes(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values)
        => _attributes.SetMany(who, values);

    public bool RemoveAttribute(EntityHandle who, string key) => _attributes.Remove(who, key);

    // ── What a body is doing ──────────────────────────────────────────────────

    public void SetEngaged(EntityHandle who, int seconds)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return;
        _pm[who.PlayerIndex].CombatExpiresAt = seconds > 0 ? Environment.TickCount64 + seconds * 1000L : 0;
    }

    public void SetDowned(EntityHandle who, int seconds)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return;

        var p = _pm[who.PlayerIndex].Char;
        p.Dead = seconds > 0;
        p.RespawnReadyUtc = seconds > 0 ? _clock.UtcNowUnix + seconds : 0;
    }

    public void SetMarked(EntityHandle who, int seconds)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return;
        _pm[who.PlayerIndex].Char.PkExpiryUtc = seconds > 0 ? _clock.UtcNowUnix + seconds : 0;
    }

    public void SetAggressor(EntityHandle who, int seconds)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return;
        _pm[who.PlayerIndex].PvpAttackerUntil = seconds > 0 ? Environment.TickCount64 + seconds * 1000L : 0;
    }

    /// <summary>The cooldown is a START stamp the bar measures forward from, not an expiry, so clearing
    /// it is zeroing the stamp rather than setting one in the past.</summary>
    public void SetActionCooldown(EntityHandle who, int seconds)
    {
        if (seconds <= 0)
        {
            if (who.IsPlayer && IsInWorld(who)) _pm[who.PlayerIndex].AttackTimer = 0;
            else if (Npc(who) is { } clearing) clearing.AttackTimer = 0;
            return;
        }

        long startedAt = Environment.TickCount64;
        if (who.IsPlayer && IsInWorld(who))
        {
            _pm[who.PlayerIndex].AttackTimer = startedAt;
            _pm[who.PlayerIndex].Char.AttackTimer = startedAt;
        }
        else if (Npc(who) is { } npc)
        {
            npc.AttackTimer = startedAt;
        }
    }

    // ── What can be done to a body ────────────────────────────────────────────

    public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "")
        => _deaths.Kill(who, killer, causeKey);

    public bool Warp(EntityHandle who, WorldPlace to)
        => who.IsPlayer && IsInWorld(who) && _movement.PlayerWarp(who.PlayerIndex, to.Map, to.X, to.Y);

    public void Give(EntityHandle who, int itemNum, int quantity = 1)
    {
        if (who.IsPlayer && IsInWorld(who)) _items.GiveItem(who.PlayerIndex, itemNum, quantity);
    }

    public void Take(EntityHandle who, int itemNum, int quantity = 1)
    {
        if (who.IsPlayer && IsInWorld(who)) _items.TakeItem(who.PlayerIndex, itemNum, quantity);
    }

    public void ReleaseGhost(EntityHandle who)
    {
        if (who.IsPlayer && HasSlot(who.PlayerIndex)) _joinLeave.ClearGhost(who.PlayerIndex);
    }

    // ── The world itself ──────────────────────────────────────────────────────

    public void Stain(WorldPlace at, int size, WorldLayer layer, float amount)
        => _decals.Deposit(at.Map, at.X, at.Y, size, layer, amount);

    public IReadOnlyList<AttributeBag> RecordsOf(string familyId) => _world.ModuleRecords.All(familyId);

    public AttributeBag? RecordAt(string familyId, int num) => _world.ModuleRecords.Get(familyId, num);

    /// <summary>The NPC a handle names, or null when its slot holds nothing. A handle outlives the body
    /// it was made for, so this is asked rather than assumed everywhere above.</summary>
    private Shared.Records.MapNpcRecord? Npc(EntityHandle who)
    {
        if (!who.IsNpc || !_world.IsRealMap(who.SpawnMap)) return null;
        if (!SlotValidation.IsValidNpcSlot(who.SpawnSlot)) return null;

        var npc = _world.MapNpcs[who.SpawnMap, who.SpawnSlot];
        return npc.Num > 0 ? npc : null;
    }
}
