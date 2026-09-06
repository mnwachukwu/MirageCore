using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>
/// The floor of the whole game: a player standing next to a mob, facing it, off cooldown, in clear weather,
/// swings and the mob loses HP. Every gate between the keypress and the damage has a reason to refuse, and
/// each one is a way for melee to go silently dead while everything still builds and every other test passes.
///
/// <para>Asserted on the RECORD rather than on a packet: what matters is that the mob's HP went down, not
/// how the news travelled.</para>
/// </summary>
[TestFixture]
public class MeleeLandsOnNpcTests
{
    const int Map = 1, PlayerIdx = 1, NpcNum = 1, NpcSlot = 1;
    const int Attempts = 60;

    private static CombatSystem Build(out PlayerManager pm, out GameWorld world)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        var sp = pm[PlayerIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var c = sp.Char;
        c.Map = Map;
        c.Level = 10;
        c.Access = AdminLevel.Player;
        c.MaxHp = 10_000; c.Hp = 10_000;
        c.MaxSp = 100;    c.Sp = 100;
        c.Str = 50;                      // a real weapon-arm, so the swing has something to resolve
        c.X = 8; c.Y = 6;
        c.Dir = Direction.Down;          // facing the mob below
        world.MapObservers[Map].Add(PlayerIdx);

        var npc = world.Npcs[NpcNum];
        npc.Name = "training dummy";
        npc.Behavior = NpcBehavior.AttackOnSight;   // Friendly and Stationary rebuff the swing by design
        npc.Str = 1;
        npc.Def = 1;                                // low, so mitigation cannot eat the hit
        npc.Int = 1;
        npc.Spd = 1;

        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.Num = NpcNum;
        mapNpc.X = 8; mapNpc.Y = 7;                 // adjacent, on the faced tile
        mapNpc.Hp = 100_000;                        // never dies mid-run
        mapNpc.Mp = 100;
        mapNpc.Sp = 0;                              // no stamina: it cannot block or dodge the swing away

        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    [Test]
    public void APlayerSwingingAtAnAdjacentNpc_TakesItsHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = world.MapNpcs[Map, NpcSlot];

        int before = mapNpc.Hp;
        pm[PlayerIdx].AttackTimer = 0;               // off cooldown
        combat.HandleAttack(PlayerIdx);

        Assert.That(mapNpc.Hp, Is.LessThan(before),
            "a facing, adjacent, off-cooldown swing in clear weather took no HP off the mob");
    }

    /// <summary>The swing also has to keep working. A gate that stamps the cooldown without landing would
    /// pass the test above once and then wedge every swing after it.</summary>
    [Test]
    public void SwingingRepeatedly_KeepsTakingHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = world.MapNpcs[Map, NpcSlot];

        int landed = 0;
        for (int i = 0; i < Attempts; i++)
        {
            int before = mapNpc.Hp;
            pm[PlayerIdx].AttackTimer = 0;
            pm[PlayerIdx].Char.Sp = pm[PlayerIdx].Char.MaxSp;
            combat.HandleAttack(PlayerIdx);
            if (mapNpc.Hp < before) landed++;
        }

        Assert.That(landed, Is.EqualTo(Attempts),
            $"only {landed} of {Attempts} swings landed; a swing off cooldown against a stamina-less mob has "
            + "no other outcome in clear weather");
    }

    /// <summary>The cooldown is the one legitimate refusal, and it has to actually refuse.</summary>
    [Test]
    public void SwingingAgainOnCooldown_TakesNothing()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = world.MapNpcs[Map, NpcSlot];

        pm[PlayerIdx].AttackTimer = 0;
        combat.HandleAttack(PlayerIdx);              // lands, and stamps the beat

        int after = mapNpc.Hp;
        combat.HandleAttack(PlayerIdx);              // immediately again, still inside the cooldown

        Assert.That(mapNpc.Hp, Is.EqualTo(after), "a swing inside the cooldown still took HP");
    }

    // ── Dispatcher (per-file convention: copied from FriendlyFireTests) ────────
    class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
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
