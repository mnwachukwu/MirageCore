using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
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
    private readonly NpcAiSystem _ai;

    /// <summary>The engine's own guild bookkeeping, for the two things a game cannot do by
    /// writing a number: saving a guild, and spending from its vault through the ledger.</summary>
    private readonly GuildSystem _guilds;

    /// <summary>How a record a game writes at run time gets onto disk. Both are null in a harness with
    /// no persistence, where a write still lands in memory and simply is not saved.</summary>
    private readonly IPersistenceService? _persistence;
    private readonly IBackgroundPersistence? _bg;
    private readonly WorldQueries _queries;
    private readonly IPacketDispatcher _dispatcher;
    private readonly IClock _clock;

    public ServerWorld(GameWorld world, PlayerManager pm, AttributeSystem attributes, DeathSystem deaths,
                       MovementSystem movement, ItemSystem items, JoinLeaveSystem joinLeave,
                       DecalSystem decals, NpcAiSystem ai, GuildSystem guilds,
                       IPacketDispatcher dispatcher, IClock? clock = null,
                       IPersistenceService? persistence = null, IBackgroundPersistence? bg = null)
    {
        _ai = ai;
        _guilds = guilds;
        _persistence = persistence;
        _bg = bg;
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

    // ── Guilds, as something a game can act on ────────────────────────────────

    public int GuildNumber(EntityHandle who) =>
        who.IsPlayer && IsInWorld(who) ? _pm[who.PlayerIndex].Guild : 0;

    public string GuildName(int guild) => Guild(guild)?.Name ?? string.Empty;

    public int GuildNamed(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;

        foreach (var (id, guild) in _world.Guilds)
        {
            // The same comparison the engine's own founding check makes, so a game cannot create a
            // second guild under a name a player would read as the one already there.
            if (!guild.Disbanded && string.Equals(guild.Name, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return 0;
    }

    public string AccessOf(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return string.Empty;

        return _pm[who.PlayerIndex].Char.Access switch
        {
            AdminLevel.Creator => "creator",
            AdminLevel.Developer => "developer",
            AdminLevel.Mapper => "mapper",
            AdminLevel.Monitor => "monitor",
            _ => "player",
        };
    }

    public string GuildRankOf(EntityHandle who)
    {
        if (Guild(GuildNumber(who)) is not { } guild) return string.Empty;

        string login = _pm[who.PlayerIndex].Login;

        foreach (var member in guild.Members)
        {
            if (!string.Equals(member.Login, login, StringComparison.OrdinalIgnoreCase)) continue;

            return member.Rank switch
            {
                GuildRank.Leader => "leader",
                GuildRank.Officer => "officer",
                GuildRank.Member => "member",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    public AttributeBag? GuildValues(int guild) => Guild(guild)?.Attributes;

    public bool SetGuildValue(int guild, string key, AttributeValue value)
    {
        if (Guild(guild) is not { } found || string.IsNullOrWhiteSpace(key)) return false;

        found.Attributes.Set(key, value);

        // ⚠ Saved on every write. A guild is not a body: nothing logs it out, so there is no later
        // moment where its values would be written anyway.
        _guilds.SaveGuild(found);
        return true;
    }

    public IReadOnlyList<EntityHandle> MembersOf(int guild)
    {
        if (guild < 1) return [];

        var found = new List<EntityHandle>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[i].IsPlaying && _pm[i].Guild == guild) found.Add(EntityHandle.ForPlayer(i));
        }

        return found;
    }

    public long GuildGold(int guild) => Guild(guild)?.VaultGold ?? 0L;

    public bool GiveGuildGold(int guild, long amount)
    {
        if (Guild(guild) is not { } found || amount <= 0) return false;

        // Through the engine's own credit, which is what keeps the vault's ceiling and its rounding in
        // one place rather than in every game that pays a guild.
        GuildSystem.CreditVault(found, amount);
        _guilds.SaveGuild(found);
        return true;
    }

    // ── Worn gear, and what wears it out ──────────────────────────────────────

    public IReadOnlyList<int> WornBy(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return [];

        var p = _pm[who.PlayerIndex].Char;
        var worn = new List<int>();

        foreach (var slot in _world.EquipSlots.Slots)
        {
            int bagSlot = p.EquippedIn(slot.Key);
            if (bagSlot >= 1 && p.Inv[bagSlot].Num > 0) worn.Add(p.Inv[bagSlot].Num);
        }

        return worn;
    }

    public int WornIn(EntityHandle who, string slotKey)
    {
        if (!who.IsPlayer || !IsInWorld(who) || string.IsNullOrEmpty(slotKey)) return 0;

        var p = _pm[who.PlayerIndex].Char;
        int bagSlot = p.EquippedIn(slotKey);

        return bagSlot >= 1 ? p.Inv[bagSlot].Num : 0;
    }

    public (int Left, int Full) DurabilityOf(EntityHandle who, int itemNum)
    {
        if (WornSlot(who, itemNum) is not { } bagSlot) return (0, 0);

        return (_pm[who.PlayerIndex].Char.Inv[bagSlot].Dur, _world.Items[itemNum].Durability);
    }

    public int Wear(EntityHandle who, int itemNum, int points)
    {
        if (points <= 0 || WornSlot(who, itemNum) is not { } bagSlot) return 0;

        var inv = _pm[who.PlayerIndex].Char.Inv[bagSlot];
        int taken = Math.Min(points, inv.Dur);

        if (taken <= 0) return 0;

        inv.Dur -= taken;

        // Push the slot back to its owner, so anything drawn off durability follows the wear.
        _items.SendInventoryUpdate(who.PlayerIndex, bagSlot);
        return taken;
    }

    public int RepairCost(int itemNum, int points)
    {
        if (itemNum < 1 || itemNum > _world.Limits.Items || points <= 0) return 0;

        return EconomyFormulas.RepairCost(points, _world.Items[itemNum]);
    }

    public double RepairRateAt(int tier) => EconomyFormulas.RepairGoldPerDurabilityPoint(tier);

    /// <summary>The bag slot holding the copy of that item they are WEARING, or null. Wearing is what
    /// makes it findable: two copies in the bag are two different amounts of wear, and a rule about what
    /// a death cost means the one that was on them.</summary>
    private int? WornSlot(EntityHandle who, int itemNum)
    {
        if (!who.IsPlayer || !IsInWorld(who) || itemNum < 1 || itemNum > _world.Limits.Items) return null;

        var p = _pm[who.PlayerIndex].Char;

        foreach (var slot in _world.EquipSlots.Slots)
        {
            int bagSlot = p.EquippedIn(slot.Key);
            if (bagSlot >= 1 && p.Inv[bagSlot].Num == itemNum) return bagSlot;
        }

        return null;
    }

    public int MapGroupOf(int mapNum) =>
        mapNum >= 1 && mapNum <= _world.Limits.Maps ? _world.Maps[mapNum].MapGroup : 0;

    public long Now() => _clock.UtcNowUnix;

    public bool SpendGuildGold(int guild, long amount, EntityHandle by)
    {
        if (Guild(guild) is not { } found || amount <= 0 || found.VaultGold < amount) return false;

        found.VaultGold -= amount;

        // 🔴 Through the engine's own ledger. A vault that went down with nothing in the spending log
        // is money a guild cannot account for, and accounting for it is most of what a vault is for.
        _guilds.RecordSpending(found,
            by.IsPlayer && IsInWorld(by) ? _pm[by.PlayerIndex].Login : string.Empty,
            NameOf(by), amount);

        return true;
    }

    /// <summary>The guild that number names, or null for one that is not there or was disbanded. A
    /// disbanded guild still occupies its number so nothing reuses it, and answering about one would be
    /// answering about a guild nobody can join.</summary>
    private Shared.Records.GuildRecord? Guild(int guild) =>
        guild >= 1 && _world.Guilds.TryGetValue(guild, out var found) && !found.Disbanded ? found : null;

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

    // ── ... and reading it back ────────────────────────────────────────────
    //
    // Each answers off the same field its setter wrote, so there is no second clock to keep in step.

    public bool IsEngaged(EntityHandle who)
    {
        if (who.IsPlayer && IsInWorld(who)) return _pm[who.PlayerIndex].IsInCombat(Environment.TickCount64);

        return Npc(who) is { } npc && npc.CombatExpiresAt > 0
            && Environment.TickCount64 < npc.CombatExpiresAt;
    }

    /// <summary>⚠ A creature has no downed state — see <see cref="SetDowned"/> — so it is never in one.</summary>
    public bool IsDowned(EntityHandle who) =>
        who.IsPlayer && IsInWorld(who) && _pm[who.PlayerIndex].Char.Dead;

    public bool IsMarked(EntityHandle who)
    {
        if (who.IsPlayer && IsInWorld(who)) return _pm[who.PlayerIndex].Char.IsPk(_clock.UtcNowUnix);

        return Npc(who) is { } npc && npc.MarkedUntilUtc > _clock.UtcNowUnix;
    }

    public bool IsAggressor(EntityHandle who)
    {
        long now = Environment.TickCount64;

        if (who.IsPlayer && IsInWorld(who))
        {
            return _pm[who.PlayerIndex].PvpAttackerUntil > 0 && now < _pm[who.PlayerIndex].PvpAttackerUntil;
        }

        return Npc(who) is { } npc && npc.AggressorUntil > 0 && now < npc.AggressorUntil;
    }

    /// <summary>
    /// ⚠ The cooldown is a START stamp rather than an expiry, so "still waiting" is measured FORWARD
    /// from it — the same direction the bar drawing it measures.
    /// </summary>
    public bool IsWaiting(EntityHandle who)
    {
        long now = Environment.TickCount64;

        if (who.IsPlayer && IsInWorld(who))
        {
            long started = _pm[who.PlayerIndex].AttackTimer;
            return started > 0 && now - started < Constants.PlayerAttackCooldownMs;
        }

        return Npc(who) is { } npc && npc.AttackTimer > 0
            && now - npc.AttackTimer < Constants.NpcAttackCooldownMs;
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

    public IReadOnlyList<int> BagOf(EntityHandle who)
    {
        if (!who.IsPlayer || !IsInWorld(who)) return [];

        var p = _pm[who.PlayerIndex].Char;
        var held = new List<int>();

        for (int slot = 1; slot <= Constants.MaxInv; slot++)
        {
            if (p.Inv[slot].Num > 0) held.Add(slot);
        }

        return held;
    }

    public (int ItemNum, int Quantity, bool Worn) InSlot(EntityHandle who, int slot)
    {
        if (!who.IsPlayer || !IsInWorld(who) || slot < 1 || slot > Constants.MaxInv) return (0, 0, false);

        var p = _pm[who.PlayerIndex].Char;
        int itemNum = p.Inv[slot].Num;

        if (itemNum <= 0) return (0, 0, false);

        // A stack answers with its count and everything else with one, so a rule can price a slot
        // without first asking what kind of thing is in it.
        int quantity = Math.Max(p.Inv[slot].Quantity, 1);

        return (itemNum, quantity, p.IsEquipped(slot));
    }

    public bool DropFrom(EntityHandle who, int slot, int quantity = 0)
    {
        if (InSlot(who, slot).ItemNum <= 0) return false;

        _items.PlayerMapDropItem(who.PlayerIndex, slot, Math.Max(quantity, 0));
        return true;
    }

    public long Carrying(EntityHandle who, int itemNum)
    {
        if (!who.IsPlayer || !IsInWorld(who) || itemNum < 1 || itemNum > _world.Limits.Items) return 0L;

        return ItemSystem.CountItem(_pm[who.PlayerIndex].Char, _world.Items, itemNum);
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

    public IReadOnlyList<AttributeBag> RecordsOf(string familyId) => familyId switch
    {
        CoreRecordFamilies.Items => Bags(_world.Items, _world.Limits.Items, i => i.Attributes),
        CoreRecordFamilies.Npcs => Bags(_world.Npcs, _world.Limits.Npcs, n => n.Attributes),
        CoreRecordFamilies.Maps => Bags(_world.Maps, _world.Limits.Maps, m => m.Attributes),
        CoreRecordFamilies.MapGroups => GroupBags(),
        _ => _world.ModuleRecords.All(familyId),
    };

    public AttributeBag? RecordAt(string familyId, int num) => familyId switch
    {
        CoreRecordFamilies.Items => num >= 1 && num <= _world.Limits.Items ? _world.Items[num].Attributes : null,
        CoreRecordFamilies.Npcs => num >= 1 && num <= _world.Limits.Npcs ? _world.Npcs[num].Attributes : null,
        CoreRecordFamilies.Maps => MapBag(num),
        CoreRecordFamilies.MapGroups => GroupBag(num),
        _ => _world.ModuleRecords.Get(familyId, num),
    };

    public string RecordName(string familyId, int num) => familyId switch
    {
        CoreRecordFamilies.Items =>
            num >= 1 && num <= _world.Limits.Items ? _world.Items[num].TrimmedName : string.Empty,
        CoreRecordFamilies.Npcs =>
            num >= 1 && num <= _world.Limits.Npcs ? _world.Npcs[num].TrimmedName : string.Empty,
        CoreRecordFamilies.Shops =>
            num >= 1 && num <= _world.Limits.Shops ? _world.Shops[num].TrimmedName : string.Empty,
        CoreRecordFamilies.Conversations =>
            num >= 1 && num <= _world.Limits.Conversations ? _world.Conversations[num].TrimmedName : string.Empty,
        CoreRecordFamilies.Maps =>
            num >= 1 && num <= _world.Limits.Maps && num < _world.Maps.Length
                ? MapGroupResolve.DisplayName(_world.Maps[num], _world.GroupOf(num)) : string.Empty,
        CoreRecordFamilies.MapGroups =>
            _world.MapGroups.TryGetValue(num, out var group) ? group.Name.Trim() : string.Empty,
        _ => RecordAt(familyId, num) is { } row && row.TryGet("name", out AttributeValue named)
                ? named.AsText() : string.Empty,
    };

    /// <summary>A map's game fields WITH its group's behind them — the map's own value when it carries the
    /// key, else the group's, else nothing. The read every rule wants; <see cref="RecordAt"/> answers with
    /// the map's own bag alone, which is what an editor authoring that one map needs.</summary>
    public WorldPlace ExitFrom(int mapNum)
    {
        if (mapNum < 1 || mapNum > _world.Limits.Maps || mapNum >= _world.Maps.Length) return WorldPlace.Nowhere;

        var map = _world.Maps[mapNum];
        var group = _world.GroupOf(mapNum);
        int exit = MapGroupResolve.ExitMap(map, group);

        return exit > 0
            ? new WorldPlace(exit, MapGroupResolve.ExitX(map, group), MapGroupResolve.ExitY(map, group))
            : WorldPlace.Nowhere;
    }

    public AttributeValue? MapValue(int mapNum, string key)
    {
        if (mapNum < 1 || mapNum > _world.Limits.Maps || mapNum >= _world.Maps.Length) return null;

        return MapGroupResolve.Value(_world.Maps[mapNum], _world.GroupOf(mapNum), key);
    }

    private AttributeBag? MapBag(int num) =>
        num >= 1 && num <= _world.Limits.Maps && num < _world.Maps.Length ? _world.Maps[num].Attributes : null;

    /// <summary>A map group's own bag, or null for a group that is not there. A group is only there once
    /// somebody authored it, unlike an item or a creature, which occupy every slot up to the world's
    /// limit whether or not anything was written in them.</summary>
    /// <summary>Every group's bag, 1-based and dense up to the highest one authored. A gap reads as
    /// an empty bag, so a game counting regions gets the same shape every other family has.</summary>
    private IReadOnlyList<AttributeBag> GroupBags()
    {
        int most = 0;
        foreach (int id in _world.MapGroups.Keys) most = Math.Max(most, id);

        var all = new List<AttributeBag>(most);
        for (int id = 1; id <= most; id++) all.Add(GroupBag(id) ?? new AttributeBag());

        return all;
    }

    private AttributeBag? GroupBag(int num) =>
        num >= 1 && _world.MapGroups.TryGetValue(num, out var group) ? group.Attributes : null;

    /// <summary>Write one of a GAME'S fields on a record, whichever family it belongs to.
    ///
    /// <para>The engine's own properties are not reachable this way. An item's power and a map's exit are
    /// normalized by the typed paths that own them, and a value written straight into the array would skip
    /// that; the extension bag has nothing to normalize, because Core has never heard of a key in it.</para></summary>
    public bool SetRecordValue(string familyId, int num, string key, AttributeValue value)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (RecordAt(familyId, num) is not { } row) return false;

        row.Set(key, value);

        // Saved on the spot. A record a game writes at run time is state, and nothing else in the
        // engine comes back later to write it.
        SaveRecord(familyId, num, row);

        return true;
    }

    private void SaveRecord(string familyId, int num, AttributeBag row)
    {
        if (_persistence is null || _bg is null) return;

        switch (familyId)
        {
            case CoreRecordFamilies.Items:
                _bg.Run(_persistence.SaveItemAsync(num, _world.Items[num]), nameof(IPersistenceService.SaveItemAsync));
                break;

            case CoreRecordFamilies.Npcs:
                _bg.Run(_persistence.SaveNpcAsync(num, _world.Npcs[num]), nameof(IPersistenceService.SaveNpcAsync));
                break;

            case CoreRecordFamilies.Maps:
                _bg.Run(_persistence.SaveMapAsync(num, _world.Maps[num]), nameof(IPersistenceService.SaveMapAsync));
                break;

            case CoreRecordFamilies.MapGroups when _world.MapGroups.TryGetValue(num, out var group):
                _bg.Run(_persistence.SaveMapGroupAsync(num, group), nameof(IPersistenceService.SaveMapGroupAsync));
                break;

            default:
                if (_world.ModuleRecords.Family(familyId) is { } family)
                    _bg.Run(_persistence.SaveModuleRecordAsync(family, num, row), nameof(IPersistenceService.SaveModuleRecordAsync));
                break;
        }
    }

    /// <summary>A family the ENGINE owns, read as the bags a game hung on it.
    ///
    /// <para>🔴 <b>What a game reads back is its OWN fields, not the engine's.</b> An item's power and
    /// its type are properties Core acts on and a game has no business rewriting; the bag is where a
    /// game's own facts about that item live, and it is all a rule ever needs to ask for.</para>
    ///
    /// <para>1-based, as every record family is, so slot 0 is not in the answer.</para></summary>
    private static IReadOnlyList<AttributeBag> Bags<T>(T[] records, int limit, Func<T, AttributeBag> bagOf)
    {
        var all = new List<AttributeBag>(limit);
        for (int num = 1; num <= limit && num < records.Length; num++) all.Add(bagOf(records[num]));

        return all;
    }

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

    public int KindOf(EntityHandle who)
    {
        // A player is not a copy of anything, so there is no kind to answer with — and 0 is the same
        // answer a handle naming nobody gets, because both mean "no creature record".
        if (!who.IsNpc) return 0;

        return Locate(who) is { } at ? at.Record.Num : 0;
    }

    public bool Provoke(EntityHandle npc, EntityHandle quarry) => _ai.Rouse(npc, quarry);

    public bool Forget(EntityHandle npc) => _ai.Calm(npc);

    public IReadOnlyList<EntityHandle> NpcsNear(WorldPlace at, int tiles)
    {
        if (at.Map < 1 || at.Map > _world.Limits.Maps || tiles < 0) return [];

        var found = new List<(int Gap, EntityHandle Who)>();

        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[at.Map, slot];
            // A reserved slot is a body that is away on another map, not a body standing here.
            if (mn.Num <= 0 || mn.IsReservedSlot) continue;

            int gap = Math.Abs(mn.X - at.X) + Math.Abs(mn.Y - at.Y);
            if (gap > tiles) continue;

            var (spawnMap, spawnSlot) = mn.GetSpawnIdentity(at.Map, slot);
            found.Add((gap, EntityHandle.ForNpc(spawnMap, spawnSlot)));
        }

        var guests = _world.MapTraversalNpcs[at.Map];
        for (int g = 0; g < guests.Count; g++)
        {
            var t = guests[g];
            if (t.Num <= 0) continue;

            int gap = Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y);
            if (gap > tiles) continue;

            found.Add((gap, EntityHandle.ForNpc(t.SpawnMapNum, t.SpawnSlot)));
        }

        // Nearest first, because a rule that takes a few of them wants the near ones — a game asking for
        // one body and getting whichever slot happened to be lowest would read as picking at random.
        found.Sort((a, b) => a.Gap.CompareTo(b.Gap));
        return [.. found.Select(f => f.Who)];
    }

    // ── Asking about the ground ───────────────────────────────────────────────

    public string TileAt(WorldPlace place)
    {
        if (Map(place) is not { } map) return string.Empty;

        return map.Tile[place.X, place.Y].Type switch
        {
            TileType.Blocked => "blocked",
            TileType.Warp => "warp",
            TileType.Item => "item",
            TileType.NpcAvoid => "npcavoid",
            TileType.Door => "door",
            TileType.Plate => "plate",
            TileType.LayerRamp => "ramp",
            _ => "walkable",
        };
    }

    public bool CanSee(WorldPlace from, WorldPlace to)
    {
        if (Map(from) is null || Map(to) is null) return false;   // one of them is not a square


        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, from.Map);
        var (fromX, fromY) = grid.CenterToWorld(from.X, from.Y);

        // Null when the target map is not one of the nine around this one, which is further than sight
        // is ever asked about.
        if (grid.ToWorldRelative(to.Map, to.X, to.Y) is not { } there) return false;

        return WorldCoordHelper.HasClearLineOfSight(
            fromX, fromY, there.worldX, there.worldY,
            new WorldLosPredicate(_world, grid, WorldLayer.Ground));
    }

    public int Distance(WorldPlace from, WorldPlace to)
    {
        if (Map(from) is null || Map(to) is null) return -1;

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, from.Map);
        var (fromX, fromY) = grid.CenterToWorld(from.X, from.Y);

        if (grid.ToWorldRelative(to.Map, to.X, to.Y) is not { } there) return -1;

        return WorldCoordHelper.WorldManhattan(fromX, fromY, there.worldX, there.worldY);
    }

    public string WeatherOn(int mapNum)
    {
        if (mapNum < 1 || mapNum > _world.Limits.Maps) return string.Empty;

        return _world.WeatherOn(mapNum) switch
        {
            WeatherType.Rain => "rain",
            WeatherType.Snow => "snow",
            WeatherType.HeatWave => "heatwave",
            WeatherType.HeavyWind => "heavywind",
            _ => "clear",
        };
    }

    /// <summary>The map a square is on, or null for a square that is not on one.
    ///
    /// <para>⚠ A world holds a FIXED number of map slots and every one of them is a real map — an
    /// unauthored one is blank rather than absent. So the only way to name no map is to name a number
    /// outside the world's own count, and the only way to name no square is to name coordinates outside
    /// that map's own size.</para></summary>
    private Shared.Records.MapRecord? Map(WorldPlace place)
    {
        if (place.Map < 1 || place.Map > _world.Limits.Maps) return null;

        var map = _world.Maps[place.Map];

        return map.Contains(place.X, place.Y) ? map : null;
    }

    // ── Doing ─────────────────────────────────────────────────────────────────

    public bool Wear(EntityHandle who, int itemNum)
    {
        if (!who.IsPlayer || !IsInWorld(who) || itemNum <= 0) return false;

        return _items.WearFromBag(who.PlayerIndex, itemNum);
    }

    public bool Remove(EntityHandle who, int itemNum)
    {
        if (WornSlot(who, itemNum) is not { } bagSlot) return false;

        _items.UnequipSlot(who.PlayerIndex, bagSlot);
        _items.SendInventoryUpdate(who.PlayerIndex, bagSlot);
        return true;
    }

    public bool IsRunning(EntityHandle who) =>
        who.IsPlayer && IsInWorld(who) && _pm[who.PlayerIndex].Char.Moving == MovementType.Running;

    // ── Asking what a creature was authored as ────────────────────────────────

    public string BehaviorOf(EntityHandle npc) => Template(npc) switch
    {
        { Behavior: NpcBehavior.Stationary } => "stationary",
        { Behavior: NpcBehavior.Pursue } => "pursue",
        { Behavior: NpcBehavior.Flee } => "flee",
        { Behavior: NpcBehavior.Scavenge } => "scavenge",
        not null => "wander",
        _ => string.Empty,
    };

    public int GroupOf(EntityHandle npc) => Template(npc)?.Group ?? 0;

    public int RangeOf(EntityHandle npc) => Template(npc)?.Range ?? 0;

    public bool IsChasing(EntityHandle npc) =>
        Npc(npc) is { } body && (body.Target > 0 || body.NpcTargetSpawnSlot > 0);

    /// <summary>The RECORD a body is a copy of — what an author wrote, rather than what this one body is
    /// doing. Null for a handle naming nothing in the world.</summary>
    private Shared.Records.NpcRecord? Template(EntityHandle npc)
        => Npc(npc) is { Num: > 0 } body ? _world.Npcs[body.Num] : null;

    /// <summary>Where the NPC a handle names currently is, or null when nothing answers to that
    /// identity any more. A handle outlives the body it was made for, so this is asked rather than
    /// assumed everywhere above — and it resolves rather than indexing, because a chaser away from
    /// home has vacated the slot it is named after.</summary>
    private WorldQueries.NpcLocation? Locate(EntityHandle who)
        => who.IsNpc ? _queries.ResolveNpc(who.SpawnMap, who.SpawnSlot) : null;

    /// <summary>The NPC record a handle names, wherever that body currently stands.</summary>
    private Shared.Records.MapNpcRecord? Npc(EntityHandle who) => Locate(who)?.Record;
}
