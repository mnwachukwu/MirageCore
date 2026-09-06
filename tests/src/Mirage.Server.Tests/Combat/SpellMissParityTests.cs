using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>
/// A spell the wind tears away is one of the no-damage outcomes, and it resolves like the others. Block,
/// dodge and miss all mean "the cast reached its target and nothing landed", so all three send the bolt,
/// float their word over the TARGET, leave both sides in the fight, and charge the caster the same.
///
/// <para>Two separate claims, and they need two different comparisons:</para>
///
/// <para>The ACT of casting — the bolt, the combat marks, the cooldown, the mana — belongs to casting, not to
/// how the cast turned out, so it must be identical across a landed cast, a defended one and a torn one.
/// Comparing only the two no-damage outcomes to each other would not show this: a bolt that fires solely on
/// the damage path is missing from both of them equally, and they would still match. The landed cast is what
/// makes the three-way agreement meaningful.</para>
///
/// <para>The OUTCOME — the word that floats and who it floats over — is compared between the defence and the
/// miss, which must differ in the word alone.</para>
///
/// <para>Every outcome is a roll, so each is cast for until it occurs. Heavy Wind supplies the miss and also
/// disables every stamina proc, so a wind cast can only miss or land and a clear one can only be defended or
/// land; neither run can sample the other's outcome.</para>
/// </summary>
[TestFixture]
public class SpellMissParityTests
{
    const int Map = 1, CasterIdx = 1, VictimIdx = 2, SpellNum = 1, SpellSlot = 1, NpcNum = 1, NpcSlot = 1;
    const int Attempts = 600;

    // Def high enough that the defender turns casts aside constantly; 0 so they never do.
    const int Defensive = 250, Helpless = 0;

    /// <summary>Everything one resolved cast produced, so two resolutions can be compared field for field.</summary>
    private readonly record struct Resolution(
        CombatTextKind Float,
        bool FloatOverNpc,
        int FloatIndex,
        int FloatX,
        int FloatY,
        CastAct Act);

    /// <summary>The part of a cast that belongs to the ACT rather than to the outcome. Identical for every
    /// cast that reaches a target, whatever it then resolves to.</summary>
    private readonly record struct CastAct(
        bool BoltFlew,
        bool CasterInCombat,
        bool TargetEngaged,
        bool CasterOnCooldown,
        int ManaSpent);

    // ── The act is the same however the cast resolves ─────────────────────────

    [Test]
    public void AgainstAPlayer_TheActOfCastingIsTheSameWhateverItResolvesTo()
    {
        var landed = ResolveOnPlayer(WeatherType.Clear, Helpless, null);
        var dodged = ResolveOnPlayer(WeatherType.Clear, Defensive, CombatTextKind.Dodge);
        var torn = ResolveOnPlayer(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(landed.Act.BoltFlew, Is.True, "no cast sent a bolt, so this proves nothing");
            Assert.That(dodged.Act, Is.EqualTo(landed.Act), "a dodged cast is not the same act as a landed one");
            Assert.That(torn.Act, Is.EqualTo(landed.Act), "a torn cast is not the same act as a landed one");
        });
    }

    [Test]
    public void AgainstAnNpc_TheActOfCastingIsTheSameWhateverItResolvesTo()
    {
        var landed = ResolveOnNpc(WeatherType.Clear, Helpless).Resolution;
        var defended = ResolveOnNpc(WeatherType.Clear, Defensive, CombatTextKind.Block, CombatTextKind.Dodge).Resolution;
        var torn = ResolveOnNpc(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss).Resolution;

        Assert.Multiple(() =>
        {
            Assert.That(landed.Act.BoltFlew, Is.True, "no cast sent a bolt, so this proves nothing");
            Assert.That(defended.Act, Is.EqualTo(landed.Act), "a turned-aside cast is not the same act as a landed one");
            Assert.That(torn.Act, Is.EqualTo(landed.Act), "a torn cast is not the same act as a landed one");
        });
    }

    // ── The outcomes differ only in the word ──────────────────────────────────

    [Test]
    public void AgainstAPlayer_AMissResolvesLikeADodge()
    {
        var dodged = ResolveOnPlayer(WeatherType.Clear, Defensive, CombatTextKind.Dodge);
        var torn = ResolveOnPlayer(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss);

        Assert.That(torn with { Float = dodged.Float }, Is.EqualTo(dodged),
            "a torn cast resolved differently from a dodged one");
    }

    [Test]
    public void AgainstAnNpc_AMissResolvesLikeADefence()
    {
        var (defended, _) = ResolveOnNpc(WeatherType.Clear, Defensive, CombatTextKind.Block, CombatTextKind.Dodge);
        var (torn, npcTarget) = ResolveOnNpc(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(torn with { Float = defended.Float }, Is.EqualTo(defended),
                "a torn cast resolved differently from one the NPC turned aside");
            Assert.That(npcTarget, Is.EqualTo(CasterIdx),
                "a torn cast left the NPC unaware of the caster; a turned-aside one aggros");
        });
    }

    /// <summary>The one place the outcomes deliberately part company. Block and dodge are the defender
    /// spending stamina to turn the cast aside; a miss is the weather, and costs them nothing.</summary>
    [Test]
    public void AMissCostsTheDefenderNoStamina()
    {
        var spells = BuildPlayerFight(out var pm, out var world, out var dispatcher, Defensive);

        world.Weather = WeatherType.Clear;
        CastUntil(spells, pm, world, dispatcher, CombatTextKind.Dodge);
        int spentDodging = pm[VictimIdx].Char.MaxSp - pm[VictimIdx].Char.Sp;

        world.Weather = WeatherType.HeavyWind;
        CastUntil(spells, pm, world, dispatcher, CombatTextKind.Miss);
        int spentMissed = pm[VictimIdx].Char.MaxSp - pm[VictimIdx].Char.Sp;

        Assert.Multiple(() =>
        {
            Assert.That(spentDodging, Is.GreaterThan(0), "a dodge drained no stamina, so this proves nothing");
            Assert.That(spentMissed, Is.Zero, "a miss drained the defender's stamina; only their own dodge should");
        });
    }

    // ── Player target ─────────────────────────────────────────────────────────

    // A null `want` takes the first cast whatever it resolved to, which is how the landed cast is sampled.
    private Resolution ResolveOnPlayer(WeatherType weather, int defenderDef, CombatTextKind? want)
    {
        var spells = BuildPlayerFight(out var pm, out var world, out var dispatcher, defenderDef);
        world.Weather = weather;

        var text = want is null
            ? CastOnce(spells, pm, world, dispatcher)
            : CastUntil(spells, pm, world, dispatcher, want.Value);
        var caster = pm[CasterIdx];
        long now = Environment.TickCount64;
        return new Resolution(
            Float: text?.Kind ?? CombatTextKind.None,
            FloatOverNpc: text?.IsNpc ?? false,
            FloatIndex: text?.Index ?? 0,
            FloatX: text?.X ?? 0,
            FloatY: text?.Y ?? 0,
            Act: new CastAct(
                BoltFlew: dispatcher.Packets.OfType<PlayerCastPacket>().Any(),
                CasterInCombat: caster.IsInCombat(now),
                TargetEngaged: pm[VictimIdx].IsInCombat(now),
                CasterOnCooldown: caster.AttackTimer != 0,
                ManaSpent: caster.Char.MaxMp - caster.Char.Mp));
    }

    private SpellSystem BuildPlayerFight(out PlayerManager pm, out GameWorld world,
                                         out CapturingDispatcher dispatcher, int defenderDef)
    {
        var spells = BuildWorld(out pm, out world, out dispatcher);

        pm[CasterIdx].Char.X = 8; pm[CasterIdx].Char.Y = 6;
        pm[VictimIdx].Char.X = 8; pm[VictimIdx].Char.Y = 7;   // adjacent, well inside the R=5 spell circle
        // No shield, so negation resolves as a dodge and never reaches the shield-wear path.
        pm[VictimIdx].Char.Def = defenderDef;                  // dodge chance scales off Def
        // Arena so the PvP gate lets an offensive cast through without PK bookkeeping.
        world.Maps[Map].Moral = MapMoral.Arena;

        pm[CasterIdx].TargetType = 0;
        pm[CasterIdx].Target = VictimIdx;
        pm[CasterIdx].TargetMap = Map;
        return spells;
    }

    // ── NPC target ────────────────────────────────────────────────────────────

    // An empty `want` takes the first cast whatever it resolved to, which is how the landed cast is sampled.
    private (Resolution Resolution, int NpcTarget) ResolveOnNpc(WeatherType weather, int defenderDef,
                                                                params CombatTextKind[] want)
    {
        var spells = BuildNpcFight(out var pm, out var world, out var dispatcher, defenderDef);
        world.Weather = weather;

        var text = want.Length == 0
            ? CastOnce(spells, pm, world, dispatcher)
            : CastUntil(spells, pm, world, dispatcher, want);
        var mapNpc = world.MapNpcs[Map, NpcSlot];
        var caster = pm[CasterIdx];
        long now = Environment.TickCount64;
        return (new Resolution(
            Float: text?.Kind ?? CombatTextKind.None,
            FloatOverNpc: text?.IsNpc ?? false,
            FloatIndex: text?.Index ?? 0,
            FloatX: text?.X ?? 0,
            FloatY: text?.Y ?? 0,
            Act: new CastAct(
                BoltFlew: dispatcher.Packets.OfType<PlayerCastPacket>().Any(),
                CasterInCombat: caster.IsInCombat(now),
                TargetEngaged: mapNpc.IsInCombat(now),
                CasterOnCooldown: caster.AttackTimer != 0,
                ManaSpent: caster.Char.MaxMp - caster.Char.Mp)), mapNpc.Target);
    }

    private SpellSystem BuildNpcFight(out PlayerManager pm, out GameWorld world,
                                      out CapturingDispatcher dispatcher, int defenderDef)
    {
        var spells = BuildWorld(out pm, out world, out dispatcher);

        pm[CasterIdx].Char.X = 8; pm[CasterIdx].Char.Y = 6;

        var npc = world.Npcs[NpcNum];
        npc.Name = "gale wisp";
        npc.Behavior = NpcBehavior.AttackOnSight;   // Friendly and Stationary refuse the cast outright
        npc.Def = defenderDef;                      // block/dodge chance scales off Def
        npc.Int = 1;
        npc.Str = 1;

        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.Num = NpcNum;
        mapNpc.X = 8; mapNpc.Y = 7;                 // adjacent to the caster
        RestockNpc(mapNpc);

        pm[CasterIdx].TargetType = 1;
        pm[CasterIdx].Target = NpcSlot;
        pm[CasterIdx].TargetMap = Map;
        return spells;
    }

    // ── Shared world + cast loop ──────────────────────────────────────────────

    private SpellSystem BuildWorld(out PlayerManager pm, out GameWorld world, out CapturingDispatcher dispatcher)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        dispatcher = new CapturingDispatcher();

        for (int i = CasterIdx; i <= VictimIdx; i++)
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            var c = sp.Char;
            c.Map = Map;
            c.Level = 10;            // the PvP gate opens at 10
            c.Access = AdminLevel.Player;
            c.MaxHp = 10_000; c.Hp = 10_000;
            c.MaxMp = 10_000; c.Mp = 10_000;
            c.MaxSp = 100;    c.Sp = 100;
            c.Int = 30;
        }
        world.MapObservers[Map].Add(CasterIdx);
        world.MapObservers[Map].Add(VictimIdx);

        // SubMp, not SubHp: a Sub spell (so it marks combat and needs the PvP gate) that pays no reagent and
        // routes no damage, which keeps the ItemSystem and the death path out of the cast entirely.
        var spell = world.Spells[SpellNum];
        spell.Name = "gale bolt";
        spell.Type = SpellType.SubMp;
        spell.VitalAmount = 10;
        spell.LevelReq = 1;
        pm[CasterIdx].Char.Spell[SpellSlot] = SpellNum;

        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return new SpellSystem(world, pm, dispatcher, combat, items: null!);
    }

    /// <summary>Casts until one of <paramref name="want"/> floats, and returns that float. The world and the
    /// dispatcher are left holding exactly that cast, so the caller can read what it produced.</summary>
    private static CombatTextPacket CastUntil(SpellSystem spells, PlayerManager pm, GameWorld world,
                                              CapturingDispatcher dispatcher, params CombatTextKind[] want)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var text = CastOnce(spells, pm, world, dispatcher);
            if (text is not null && want.Contains(text.Kind)) return text;
        }
        Assert.Fail($"no {string.Join(" or ", want)} across {Attempts} casts, so there is nothing to compare");
        return null!;
    }

    /// <summary>One cast from a clean slate, returning whatever floated (null if nothing did).</summary>
    private static CombatTextPacket? CastOnce(SpellSystem spells, PlayerManager pm, GameWorld world,
                                              CapturingDispatcher dispatcher)
    {
        pm[CasterIdx].AttackTimer = 0;                           // ready to cast
        pm[CasterIdx].Char.Mp = pm[CasterIdx].Char.MaxMp;
        pm[VictimIdx].Char.Mp = pm[VictimIdx].Char.MaxMp;
        // Every defender's pools back to full: block and dodge are both gated on stamina, so a pool drained
        // by an earlier attempt would quietly stop the outcome the caller is waiting for.
        pm[VictimIdx].Char.Sp = pm[VictimIdx].Char.MaxSp;
        RestockNpc(world.MapNpcs[Map, NpcSlot]);
        dispatcher.Clear();

        spells.CastSpell(CasterIdx, SpellSlot);

        return dispatcher.Packets.OfType<CombatTextPacket>().FirstOrDefault();
    }

    // A slot NPC with no record in it takes the top-up harmlessly, which keeps the cast loop free of a
    // per-scenario branch.
    private static void RestockNpc(MapNpcRecord mapNpc)
    {
        mapNpc.Hp = 10_000;
        mapNpc.Mp = 10_000;
        mapNpc.Sp = 100;
    }

    // ── Dispatcher (per-file convention: copied from FriendlyFireTests) ────────
    // Records every packet put on the wire, so a test can read the bolt and the float one cast produced.
    class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> Packets = new();

        public void Clear() => Packets.Clear();

        public void SendTo(int index, IPacket packet) => Packets.Add(packet);
        public void SendToAll(IPacket packet) => Packets.Add(packet);
        public void SendToAllBut(int exclude, IPacket packet) => Packets.Add(packet);
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) => Packets.Add(packet);
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) => Packets.Add(packet);
        public void SendToViewport(int speakerIndex, IPacket packet) => Packets.Add(packet);
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) => Packets.Add(packet);
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
