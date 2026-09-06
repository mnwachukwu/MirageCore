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
/// The two remaining melee directions: a player hitting a player, and a mob hitting a player. Each runs
/// through its own entry point with its own gates, so a change that quietly stops one says nothing about
/// the others.
///
/// <para>Asserted on the RECORD — the victim's HP went down — for the reason the player-versus-NPC fixture
/// gives: a swing that lands nothing still broadcasts, still stamps its cooldown and still floats text, so
/// every signal a test could read stays truthful while the damage goes missing.</para>
/// </summary>
[TestFixture]
public class MeleeLandsOnPlayerTests
{
    const int Map = 1, AttackerIdx = 1, VictimIdx = 2, NpcNum = 1, NpcSlot = 1;
    const int Attempts = 60;

    private static CombatSystem Build(out PlayerManager pm, out GameWorld world)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        for (int i = AttackerIdx; i <= VictimIdx; i++)
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            var c = sp.Char;
            c.Map = Map;
            c.Level = 10;                  // open PvP starts here
            c.Access = AdminLevel.Player;  // an admin is exempt from PvP entirely
            c.MaxHp = 100_000; c.Hp = 100_000;   // deep enough that no run kills anyone
            c.MaxSp = 100;     c.Sp = 100;
        }
        // Arena so a kill carries no stakes and the PvP gate opens without PK bookkeeping.
        world.Maps[Map].Moral = MapMoral.Arena;

        var ap = pm[AttackerIdx].Char;
        ap.X = 8; ap.Y = 6;
        ap.Dir = Direction.Down;           // facing the victim below
        ap.Str = 60;                       // a real arm behind the swing

        var vp = pm[VictimIdx].Char;
        vp.X = 8; vp.Y = 7;
        vp.Def = 0;                        // no mitigation, and no block or dodge to turn it aside
        vp.Sp = 0;

        world.MapObservers[Map].Add(AttackerIdx);
        world.MapObservers[Map].Add(VictimIdx);

        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    // Arms the map NPC and returns it, positioned adjacent to the victim and facing them.
    private static MapNpcRecord ArmNpc(GameWorld world)
    {
        var npc = world.Npcs[NpcNum];
        npc.Name = "training dummy";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 60;
        npc.Def = 1;
        npc.Int = 60;
        npc.Spd = 1;

        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.Num = NpcNum;
        mapNpc.X = 8; mapNpc.Y = 6;
        mapNpc.Dir = Direction.Down;   // adjacent to the victim at (8,7) and facing them
        mapNpc.Hp = 100_000;
        mapNpc.Mp = 100_000;
        mapNpc.Sp = 100;
        mapNpc.AttackTimer = 0;
        return mapNpc;
    }

    // ── Player versus player ──────────────────────────────────────────────────

    [Test]
    public void APlayerSwingingAtAnAdjacentPlayer_TakesTheirHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;

        int before = pm[VictimIdx].Char.Hp;
        pm[AttackerIdx].AttackTimer = 0;
        combat.HandleAttack(AttackerIdx);

        Assert.That(pm[VictimIdx].Char.Hp, Is.LessThan(before),
            "a facing, adjacent, off-cooldown swing in an arena took no HP off the other player");
    }

    [Test]
    public void SwingingRepeatedlyAtAPlayer_KeepsTakingHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;

        int landed = 0;
        for (int i = 0; i < Attempts; i++)
        {
            int before = pm[VictimIdx].Char.Hp;
            pm[AttackerIdx].AttackTimer = 0;
            pm[AttackerIdx].Char.Sp = pm[AttackerIdx].Char.MaxSp;
            pm[VictimIdx].Char.Sp = 0;   // held at zero so no swing is dodged away
            combat.HandleAttack(AttackerIdx);
            if (pm[VictimIdx].Char.Hp < before) landed++;
        }

        Assert.That(landed, Is.EqualTo(Attempts),
            $"only {landed} of {Attempts} swings landed on a stamina-less player in clear weather");
    }

    // ── NPC versus player ─────────────────────────────────────────────────────

    [Test]
    public void AnNpcSwingingAtAnAdjacentPlayer_TakesTheirHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = ArmNpc(world);

        int before = pm[VictimIdx].Char.Hp;
        combat.NpcAttackPlayer(Map, mapNpc, NpcSlot, VictimIdx, Environment.TickCount64);

        Assert.That(pm[VictimIdx].Char.Hp, Is.LessThan(before),
            "a facing, adjacent, off-cooldown mob took no HP off the player");
    }

    [Test]
    public void AnNpcSwingingRepeatedly_KeepsTakingHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = ArmNpc(world);

        int landed = 0;
        for (int i = 0; i < Attempts; i++)
        {
            int before = pm[VictimIdx].Char.Hp;
            mapNpc.AttackTimer = 0;
            mapNpc.Sp = 100;
            pm[VictimIdx].Char.Sp = 0;   // no stamina to block or dodge with
            combat.NpcAttackPlayer(Map, mapNpc, NpcSlot, VictimIdx, Environment.TickCount64);
            if (pm[VictimIdx].Char.Hp < before) landed++;
        }

        Assert.That(landed, Is.EqualTo(Attempts),
            $"only {landed} of {Attempts} mob swings landed on a stamina-less player in clear weather");
    }

    [Test]
    public void AnNpcCastingAtAnAdjacentPlayer_TakesTheirHp()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        var mapNpc = ArmNpc(world);

        int before = pm[VictimIdx].Char.Hp;
        combat.NpcCastSpellOnPlayer(Map, NpcSlot, mapNpc, VictimIdx, Environment.TickCount64);

        Assert.That(pm[VictimIdx].Char.Hp, Is.LessThan(before), "a mob's cast took no HP off the player");
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
