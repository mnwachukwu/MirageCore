using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

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
    private readonly WorldQueries _queries;
    private readonly IPacketDispatcher _dispatcher;
    private readonly IClock _clock;

    public ServerWorld(GameWorld world, PlayerManager pm, AttributeSystem attributes, DeathSystem deaths,
                       MovementSystem movement, ItemSystem items, JoinLeaveSystem joinLeave,
                       DecalSystem decals, IPacketDispatcher dispatcher, IClock? clock = null)
    {
        _dispatcher = dispatcher;
        _world = world;
        _pm = pm;
        _attributes = attributes;
        _deaths = deaths;
        _movement = movement;
        _items = items;
        _joinLeave = joinLeave;
        _decals = decals;
        _queries = new WorldQueries(world, pm);
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
        return Locate(who) is { } at ? new WorldPlace(at.CurrentMap, at.Record.X, at.Record.Y) : WorldPlace.Nowhere;
    }

    // ── What a game says ──────────────────────────────────────────────────────

    /// <summary>The one chat path that carries text rather than a key: a game's words are its own, so
    /// there is nothing to look up per recipient.</summary>
    public void Tell(EntityHandle who, string text, ChatChannel channel, int color)
    {
        if (!who.IsPlayer || !IsInWorld(who) || string.IsNullOrEmpty(text)) return;

        _dispatcher.SendTo(who.PlayerIndex, PacketBuilder.ChatMsg(text, color, channel));
    }

    public void TellEveryone(string text, ChatChannel channel, int color)
    {
        if (string.IsNullOrEmpty(text)) return;

        _dispatcher.SendToAll(PacketBuilder.ChatMsg(text, color, channel));
    }

    public void TellEveryoneOn(int mapNum, string text, ChatChannel channel, int color)
    {
        if (string.IsNullOrEmpty(text) || mapNum <= 0 || mapNum > _world.Limits.Maps) return;

        // The observers of a map, not its occupants: the world scrolls contiguously, so the audience
        // for something happening here includes whoever is standing on the next map looking at it.
        _dispatcher.SendToObservers(_world.MapObservers[mapNum], PacketBuilder.ChatMsg(text, color, channel));
    }

    public void TellEveryoneNear(WorldPlace at, string text, ChatChannel channel, int color)
    {
        if (string.IsNullOrEmpty(text) || at.Map <= 0) return;

        _dispatcher.SendToViewportAt(at.Map, at.X, at.Y, PacketBuilder.ChatMsg(text, color, channel));
    }

    public void TellThese(IReadOnlyCollection<EntityHandle> them, string text,
                          ChatChannel channel, int color)
    {
        if (them is null || them.Count == 0 || string.IsNullOrEmpty(text)) return;

        // Built once and sent many times: the line is the same for everybody, and a game announcing
        // to a large guild would otherwise serialize it per recipient.
        var line = PacketBuilder.ChatMsg(text, color, channel);

        foreach (EntityHandle who in them)
        {
            if (who.IsPlayer && IsInWorld(who)) _dispatcher.SendTo(who.PlayerIndex, line);
        }
    }

    // ── Who somebody is with ───────────────────────────────────────────

    public string GuildOf(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return string.Empty;

        int id = _pm[who.PlayerIndex].Guild;
        return id >= 1 && _world.Guilds.TryGetValue(id, out var guild) ? guild.Name : string.Empty;
    }

    /// <summary>
    /// Everybody in the world sharing this body's guild.
    ///
    /// <para>Walked off the online roster rather than read from the guild's member list, and the two
    /// are different questions: the list is accounts, some of them logged out for a week, and the only
    /// thing a game does with this answer is act on the bodies in it.</para>
    /// </summary>
    public IReadOnlyList<EntityHandle> GuildmatesOf(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return [];

        int guild = _pm[who.PlayerIndex].Guild;
        if (guild < 1) return [];

        var found = new List<EntityHandle>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[i].IsPlaying && _pm[i].Guild == guild) found.Add(EntityHandle.ForPlayer(i));
        }

        return found;
    }

    /// <summary>
    /// This body's party, which is two people: a party is a pair here, and the partner is named
    /// rather than gathered.
    /// </summary>
    public IReadOnlyList<EntityHandle> PartyOf(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return [];

        var me = _pm[who.PlayerIndex];
        if (!me.InParty || me.PartyPlayer < 1 || !HasSlot(me.PartyPlayer)) return [];

        EntityHandle partner = EntityHandle.ForPlayer(me.PartyPlayer);
        return IsInWorld(partner) ? [who, partner] : [who];
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
        long until = seconds > 0 ? Environment.TickCount64 + seconds * 1000L : 0;

        if (who.IsPlayer && IsInWorld(who)) _pm[who.PlayerIndex].CombatExpiresAt = until;
        else if (Npc(who) is { } npc) npc.CombatExpiresAt = until;
    }

    /// <summary>
    /// ⚠ <b>Players only, and deliberately.</b> Downed is a body lying there waiting to get up, and a
    /// creature has no such state: one that runs out of health is despawned and its slot counts down to
    /// a respawn, which <see cref="Kill"/> and the spawn clock already own. Giving this an NPC meaning
    /// would be inventing a second, conflicting answer to "when does it come back".
    /// </summary>
    public void SetDowned(EntityHandle who, int seconds)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return;

        var p = _pm[who.PlayerIndex].Char;
        p.Dead = seconds > 0;
        p.RespawnReadyUtc = seconds > 0 ? _clock.UtcNowUnix + seconds : 0;
    }

    public void SetMarked(EntityHandle who, int seconds)
    {
        long until = seconds > 0 ? _clock.UtcNowUnix + seconds : 0;

        if (who.IsPlayer && IsInWorld(who)) _pm[who.PlayerIndex].Char.PkExpiryUtc = until;
        else if (Npc(who) is { } npc) npc.MarkedUntilUtc = until;
    }

    public void SetAggressor(EntityHandle who, int seconds)
    {
        long until = seconds > 0 ? Environment.TickCount64 + seconds * 1000L : 0;

        if (who.IsPlayer && IsInWorld(who)) _pm[who.PlayerIndex].PvpAttackerUntil = until;
        else if (Npc(who) is { } npc) npc.AggressorUntil = until;
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

    /// <summary>
    /// Whoever is standing on that square.
    ///
    /// <para>Players first, and the roster is walked rather than indexed by tile: nothing keeps a
    /// tile-to-player map, and the count is bounded by the slot limit.</para>
    ///
    /// <para>NPCs come from the viewport sweep around the square rather than from the map's own slots,
    /// because a body near a border stands on a map it has no slot on. The sweep already handles guests
    /// and the 3x3 grid, so asking it and filtering to the exact tile is the answer that stays right
    /// when somebody walks across a seam.</para>
    /// </summary>
    public EntityHandle At(WorldPlace place)
    {
        if (place.Map <= 0) return EntityHandle.None;

        // ⚠ This server's roster, not the protocol's ceiling. The shared maximum is the largest any
        // server may be configured for; indexing past _pm.Slots walks off the end of the array.
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var player = _pm[i];
            if (!player.IsPlaying) continue;

            var c = player.Char;
            if (c.Map == place.Map && c.X == place.X && c.Y == place.Y) return EntityHandle.ForPlayer(i);
        }

        foreach (var found in _queries.NpcsInViewport(place.Map, place.X, place.Y))
        {
            if (found.CurrentMap == place.Map && found.Record.X == place.X && found.Record.Y == place.Y)
            {
                // Named by where it SPAWNS, not by where it is standing: a handle has to outlive the
                // body walking onto another map, and the current slot is the thing that changes.
                return EntityHandle.ForNpc(found.CurrentMap, found.CurrentSlot);
            }
        }

        return EntityHandle.None;
    }

    /// <summary>
    /// Floats a line off a body, to everybody who can see it happen.
    ///
    /// <para>Addressed by SLOT rather than by tile, so the client can follow the body: an NPC travels
    /// as its CURRENT slot and map, which is what the client's own roster is keyed by — the spawn
    /// identity a handle carries means nothing to a client that has never seen it.</para>
    /// </summary>
    public void Float(EntityHandle who, string text, uint rgb, float splatter)
    {
        if (string.IsNullOrEmpty(text) && splatter <= 0f) return;

        WorldPlace at = PlaceOf(who);
        if (at.Map <= 0) return;

        var packet = new FloatingTextPacket
        {
            Text = text, Rgb = rgb, Splatter = splatter,
            MapNum = at.Map, X = at.X, Y = at.Y,
        };

        if (who.IsPlayer && IsInWorld(who))
        {
            packet = packet with { IsNpc = false, Index = who.PlayerIndex };
        }
        else if (Locate(who) is { } found)
        {
            packet = packet with { IsNpc = true, Index = found.CurrentSlot, NpcMap = found.CurrentMap };
        }
        else
        {
            return;
        }

        _dispatcher.SendToViewportAt(at.Map, at.X, at.Y, packet);
    }

    public void Sweep(EntityHandle who, bool connected) =>
        Show(who, EntityHandle.None, GameEffect.Sweep, default, 0, connected ? 1f : 0f);

    public void Throw(EntityHandle from, EntityHandle to, ProjectileStyle style, uint rgb) =>
        Show(from, to, GameEffect.Throw, style, rgb, 0f);

    public void Burst(EntityHandle who, uint rgb, float intensity) =>
        Show(who, EntityHandle.None, GameEffect.Burst, default, rgb, Math.Clamp(intensity, 0f, 1f));

    /// <summary>
    /// One effect, to everybody who can see where it happens.
    ///
    /// <para>The viewport rather than the observers of a map: this is a thing you watch happen to
    /// somebody, at the range you would see them.</para>
    ///
    /// <para>Sent from where the ACTOR is, even when it is aimed somewhere else — a throw is seen by
    /// whoever can see it leave, and the client already follows the target itself.</para>
    /// </summary>
    private void Show(EntityHandle from, EntityHandle to, GameEffect effect,
                      ProjectileStyle style, uint rgb, float intensity)
    {
        if (Sighted(from) is not { } origin) return;

        _dispatcher.SendToViewportAt(origin.MapNum, origin.X, origin.Y, new GameEffectPacket
        {
            Effect = effect,
            From = origin,
            To = Sighted(to) ?? GameEffectPacket.Body.None,
            Style = style,
            Rgb = rgb,
            Intensity = intensity,
        });
    }

    /// <summary>
    /// A body as the client's own roster knows it, or null when nothing is there.
    ///
    /// <para>An NPC travels as the slot and map it is standing on RIGHT NOW, not as the spawn identity
    /// its handle carries: a client has never seen a spawn identity.</para>
    /// </summary>
    private GameEffectPacket.Body? Sighted(EntityHandle who)
    {
        if (who.IsPlayer && IsInWorld(who))
        {
            var c = _pm[who.PlayerIndex].Char;
            return new GameEffectPacket.Body(false, who.PlayerIndex, 0, c.Map, c.X, c.Y, c.Dir);
        }

        if (Locate(who) is { } found)
        {
            return new GameEffectPacket.Body(
                true, found.CurrentSlot, found.CurrentMap,
                found.CurrentMap, found.Record.X, found.Record.Y, found.Record.Dir);
        }

        return null;
    }

    public IReadOnlyList<AttributeBag> RecordsOf(string familyId) => _world.ModuleRecords.All(familyId);

    public AttributeBag? RecordAt(string familyId, int num) => _world.ModuleRecords.Get(familyId, num);

    public string NameOf(EntityHandle who)
    {
        if (who.IsPlayer)
        {
            var player = _pm[who.PlayerIndex];
            return player.IsPlaying ? player.Char.Name.TrimEnd() : string.Empty;
        }

        // An NPC's name is on its TEMPLATE, so the body has to be located first to learn which template
        // it is. A handle whose body has left the world answers blank rather than guessing from the
        // slot it used to stand in.
        if (Locate(who) is not { } at || at.Record.Num <= 0) return string.Empty;

        return _world.Npcs[at.Record.Num].TrimmedName;
    }

    /// <summary>Where the NPC a handle names currently is, or null when nothing answers to that
    /// identity any more. A handle outlives the body it was made for, so this is asked rather than
    /// assumed everywhere above — and it resolves rather than indexing, because a chaser away from
    /// home has vacated the slot it is named after.</summary>
    private WorldQueries.NpcLocation? Locate(EntityHandle who)
        => who.IsNpc ? _queries.ResolveNpc(who.SpawnMap, who.SpawnSlot) : null;

    /// <summary>The NPC record a handle names, wherever that body currently stands.</summary>
    private Shared.Records.MapNpcRecord? Npc(EntityHandle who) => Locate(who)?.Record;
}
