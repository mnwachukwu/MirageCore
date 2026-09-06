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
/// A swing the wind tears away is one of the no-damage outcomes, and it resolves like the others. Block,
/// dodge and miss all mean "the swing reached its target and nothing landed", so all three show the swoosh,
/// float their word over the TARGET, count as reach, and cost the attacker the beat.
///
/// <para>Two separate claims, and they need two different comparisons:</para>
///
/// <para>The ACT of swinging — swoosh, attack pose, cooldown, reach — belongs to swinging, not to how the
/// swing turned out, so it must be identical across a landed swing, a dodged one and a torn one. Comparing
/// only the two no-damage outcomes to each other would not show this: a swoosh that fires solely on the
/// damage path is missing from both of them equally, and they would still match. The landed swing is what
/// makes the three-way agreement meaningful.</para>
///
/// <para>The OUTCOME — the word that floats and who it floats over — is compared between the dodge and the
/// miss, which must differ in the word alone.</para>
///
/// <para>Reach is the load-bearing field. An attack-on-sight NPC gives up on a target it has not reached in
/// <c>NpcAiSystem.NpcAosUnreachableGiveUpMs</c>, so a mob whose swings keep being turned aside must still count as having reached
/// what it is standing next to and hitting — or it walks away mid-fight.</para>
///
/// <para>Block, dodge and miss are all rolls, so each is swung for until it occurs. Heavy Wind supplies the
/// miss and also disables every stamina proc, so a wind swing can only miss or land and a clear one can only
/// be defended or land; neither run can sample the other's outcome.</para>
/// </summary>
[TestFixture]
public class MeleeMissParityTests
{
    const int Map = 1, VictimIdx = 1, NpcNum = 1, NpcSlot = 1, VictimNpcSlot = 2;
    const int Attempts = 600;

    // Def high enough that the defender turns swings aside constantly; 0 so they never do.
    const int Defensive = 250, Helpless = 0;

    /// <summary>Everything one resolved swing produced, so two resolutions can be compared field for field.</summary>
    private readonly record struct Resolution(
        CombatTextKind Float,
        bool FloatOverNpc,
        int FloatIndex,
        SwingAct Act);

    /// <summary>The part of a swing that belongs to the ACT rather than to the outcome. Identical for every
    /// swing that reaches a target, whatever it then resolves to.</summary>
    private readonly record struct SwingAct(
        bool SwooshFlew,
        bool TargetInCombat,
        bool AttackerPosed,
        bool AttackerOnCooldown,
        bool AttackerCountedReach);

    [Test]
    public void TheActOfSwingingIsTheSameWhateverItResolvesTo()
    {
        var landed = ResolveOnPlayer(WeatherType.Clear, Helpless, want: null);
        var dodged = ResolveOnPlayer(WeatherType.Clear, Defensive, CombatTextKind.Dodge);
        var torn = ResolveOnPlayer(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(landed.Act.SwooshFlew, Is.True, "no swing showed a swoosh, so this proves nothing");
            Assert.That(dodged.Act, Is.EqualTo(landed.Act), "a dodged swing is not the same act as a landed one");
            Assert.That(torn.Act, Is.EqualTo(landed.Act), "a torn swing is not the same act as a landed one");
        });
    }

    [Test]
    public void NpcAgainstAPlayer_AMissResolvesLikeADodge()
    {
        var dodged = ResolveOnPlayer(WeatherType.Clear, Defensive, CombatTextKind.Dodge);
        var torn = ResolveOnPlayer(WeatherType.HeavyWind, Defensive, CombatTextKind.Miss);

        Assert.That(torn with { Float = dodged.Float }, Is.EqualTo(dodged),
            "a torn swing resolved differently from a dodged one");
    }

    /// <summary>The mob-on-mob mirror: whichever way the swing fails, the victim turns on whoever swung.</summary>
    [Test]
    public void NpcAgainstAnNpc_AMissFlipsAggroLikeADefence()
    {
        int defended = ResolveOnNpc(WeatherType.Clear, CombatTextKind.Block, CombatTextKind.Dodge);
        int torn = ResolveOnNpc(WeatherType.HeavyWind, CombatTextKind.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(defended, Is.EqualTo(NpcSlot), "a defended swing left the victim unaware, so this proves nothing");
            Assert.That(torn, Is.EqualTo(NpcSlot), "a torn swing left the victim unaware of who swung at it");
        });
    }

    /// <summary>The one place the outcomes deliberately part company: a dodge is the defender spending stamina
    /// to get out of the way, a miss is the weather and costs them nothing.</summary>
    [Test]
    public void AMissCostsTheDefenderNoStamina()
    {
        var combat = BuildNpcVsPlayer(out var pm, out var world, out var dispatcher, Defensive);

        world.Weather = WeatherType.Clear;
        SwingUntil(combat, pm, world, dispatcher, CombatTextKind.Dodge);
        int spentDodging = pm[VictimIdx].Char.MaxSp - pm[VictimIdx].Char.Sp;

        world.Weather = WeatherType.HeavyWind;
        SwingUntil(combat, pm, world, dispatcher, CombatTextKind.Miss);
        int spentMissed = pm[VictimIdx].Char.MaxSp - pm[VictimIdx].Char.Sp;

        Assert.Multiple(() =>
        {
            Assert.That(spentDodging, Is.GreaterThan(0), "a dodge drained no stamina, so this proves nothing");
            Assert.That(spentMissed, Is.Zero, "a miss drained the defender's stamina; only their own dodge should");
        });
    }

    // ── NPC swinging at a player ──────────────────────────────────────────────

    // A null `want` takes the first swing whatever it resolved to, which is how the landed swing is sampled.
    private Resolution ResolveOnPlayer(WeatherType weather, int defenderDef, CombatTextKind? want)
    {
        var combat = BuildNpcVsPlayer(out var pm, out var world, out var dispatcher, defenderDef);
        world.Weather = weather;

        var text = want is null
            ? SwingOnce(combat, pm, world, dispatcher)
            : SwingUntil(combat, pm, world, dispatcher, want.Value);
        var mapNpc = world.MapNpcs[Map, NpcSlot];
        long now = Environment.TickCount64;
        return new Resolution(
            Float: text?.Kind ?? CombatTextKind.None,
            FloatOverNpc: text?.IsNpc ?? false,
            FloatIndex: text?.Index ?? 0,
            Act: new SwingAct(
                SwooshFlew: dispatcher.Packets.OfType<NpcAttackPacket>().Any(),
                TargetInCombat: pm[VictimIdx].IsInCombat(now),
                AttackerPosed: mapNpc.Attacking,
                AttackerOnCooldown: mapNpc.AttackTimer != 0,
                AttackerCountedReach: mapNpc.LastReachedTargetMs != 0));
    }

    private CombatSystem BuildNpcVsPlayer(out PlayerManager pm, out GameWorld world,
                                          out CapturingDispatcher dispatcher, int defenderDef)
    {
        var combat = BuildWorld(out pm, out world, out dispatcher);

        var c = pm[VictimIdx].Char;
        c.X = 8; c.Y = 7;
        c.Def = defenderDef;          // dodge chance scales off Def; no shield, so negation lands as a dodge

        var npc = world.Npcs[NpcNum];
        npc.Name = "gale hound";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 1;                  // a landed swing must not kill the victim out from under the loop
        npc.Def = 1;

        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.Num = NpcNum;
        mapNpc.X = 8; mapNpc.Y = 6;
        mapNpc.Dir = Direction.Down;  // adjacent and facing the victim at (8,7)
        return combat;
    }

    // ── NPC swinging at another NPC ───────────────────────────────────────────

    // Returns the victim's acquired NPC-attacker slot: who it turned on after the swing failed.
    private int ResolveOnNpc(WeatherType weather, params CombatTextKind[] want)
    {
        var combat = BuildWorld(out _, out var world, out var dispatcher);
        world.Weather = weather;

        var npc = world.Npcs[NpcNum];
        npc.Name = "gale hound";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 1;
        npc.Def = Defensive;         // the VICTIM rolls block/dodge off this

        var attacker = world.MapNpcs[Map, NpcSlot];
        attacker.Num = NpcNum;
        attacker.X = 8; attacker.Y = 6;
        var victim = world.MapNpcs[Map, VictimNpcSlot];
        victim.Num = NpcNum;
        victim.X = 8; victim.Y = 7;  // adjacent

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            attacker.AttackTimer = 0;
            RestockNpc(attacker);
            RestockNpc(victim);
            victim.Target = 0;
            victim.NpcTargetSpawnSlot = 0;   // AlertNpcFromNpc only acquires an unclaimed victim
            dispatcher.Clear();

            combat.NpcAttackNpc(Map, NpcSlot, attacker, Map, VictimNpcSlot, victim, Environment.TickCount64);

            if (dispatcher.Packets.OfType<CombatTextPacket>().Any(p => want.Contains(p.Kind)))
                return victim.NpcTargetSpawnSlot;
        }
        Assert.Fail($"no {string.Join(" or ", want)} across {Attempts} swings, so there is nothing to compare");
        return 0;
    }

    // ── Shared world + swing loop ─────────────────────────────────────────────

    private CombatSystem BuildWorld(out PlayerManager pm, out GameWorld world, out CapturingDispatcher dispatcher)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        dispatcher = new CapturingDispatcher();

        var sp = pm[VictimIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var c = sp.Char;
        c.Map = Map;
        c.Level = 10;
        c.Access = AdminLevel.Player;
        c.MaxHp = 100_000; c.Hp = 100_000;
        c.MaxMp = 10_000;  c.Mp = 10_000;
        c.MaxSp = 100;     c.Sp = 100;
        world.MapObservers[Map].Add(VictimIdx);

        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    /// <summary>Swings until one of <paramref name="want"/> floats, and returns that float. The world and the
    /// dispatcher are left holding exactly that swing, so the caller can read what it produced.</summary>
    private static CombatTextPacket SwingUntil(CombatSystem combat, PlayerManager pm, GameWorld world,
                                               CapturingDispatcher dispatcher, params CombatTextKind[] want)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var text = SwingOnce(combat, pm, world, dispatcher);
            if (text is not null && want.Contains(text.Kind)) return text;
        }
        Assert.Fail($"no {string.Join(" or ", want)} across {Attempts} swings, so there is nothing to compare");
        return null!;
    }

    /// <summary>One swing from a clean slate, returning whatever floated (null if nothing did).</summary>
    private static CombatTextPacket? SwingOnce(CombatSystem combat, PlayerManager pm, GameWorld world,
                                               CapturingDispatcher dispatcher)
    {
        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.AttackTimer = 0;                               // ready to swing
        mapNpc.Attacking = false;
        mapNpc.LastReachedTargetMs = 0;
        RestockNpc(mapNpc);
        pm[VictimIdx].Char.Hp = pm[VictimIdx].Char.MaxHp;     // never let the victim die out from under the loop
        // Block and dodge are both gated on stamina, so a pool drained by an earlier attempt would quietly
        // stop the outcome the caller is waiting for.
        pm[VictimIdx].Char.Sp = pm[VictimIdx].Char.MaxSp;
        dispatcher.Clear();

        combat.NpcAttackPlayer(Map, mapNpc, NpcSlot, VictimIdx, Environment.TickCount64);

        return dispatcher.Packets.OfType<CombatTextPacket>().FirstOrDefault();
    }

    private static void RestockNpc(MapNpcRecord mapNpc)
    {
        mapNpc.Hp = 10_000;
        mapNpc.Mp = 10_000;
        mapNpc.Sp = 100;
    }

    // ── Dispatcher (per-file convention: copied from FriendlyFireTests) ────────
    // Records every packet put on the wire, so a test can read the swoosh and the float one swing produced.
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
