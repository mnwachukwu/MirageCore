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
/// The other half of the floor: a cast that passes every gate actually reaches its target. Harmful at an NPC,
/// harmful at a player, and a heal at a player — each has its own branch through CastSpell, and each is a
/// separate way for casting to go quiet while the build stays green.
///
/// <para>Asserted on the RECORD, like the melee equivalent: the target's vital moved, however the news
/// travelled. The cast cooldown is stamped on success, so each case swings once from a clean slate.</para>
/// </summary>
[TestFixture]
public class SpellLandsOnTargetTests
{
    const int Map = 1, CasterIdx = 1, VictimIdx = 2, SpellNum = 1, SpellSlot = 1, NpcNum = 1, NpcSlot = 1;

    private static SpellSystem Build(out PlayerManager pm, out GameWorld world, SpellType type, int amount = 40)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        for (int i = CasterIdx; i <= VictimIdx; i++)
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            var c = sp.Char;
            c.Map = Map;
            c.Level = 10;                 // the PvP gate opens at 10
            c.Access = AdminLevel.Player; // an admin is exempt from PvP, which would refuse the harmful cases
            c.MaxHp = 10_000; c.Hp = 10_000;
            c.MaxMp = 10_000; c.Mp = 10_000;
            c.MaxSp = 100;    c.Sp = 100;
            c.Int = 60;
        }
        world.MapObservers[Map].Add(CasterIdx);
        world.MapObservers[Map].Add(VictimIdx);
        world.Maps[Map].Moral = MapMoral.Arena;   // consequence-free PvP, so the harmful gate opens cleanly

        pm[CasterIdx].Char.X = 8; pm[CasterIdx].Char.Y = 6;
        pm[VictimIdx].Char.X = 8; pm[VictimIdx].Char.Y = 7;   // adjacent, inside the R=5 circle
        pm[VictimIdx].Char.Def = 0;                            // nothing left to mitigate the drain
        // Stamina is the only gate that shuts dodge off outright. Def=0 does NOT: the chance is
        // (Def + Level) / 18 rounded, and Level has to stay at 10 for the PvP gate, so it floors at 1 —
        // against a roll in [0..99] that is 1 cast in 100 dodged. Matches the NPC victim below.
        pm[VictimIdx].Char.Sp = 0;

        var spell = world.Spells[SpellNum];
        spell.Name = "test spell";
        spell.Type = type;
        spell.VitalAmount = (short)amount;
        spell.LevelReq = 1;
        pm[CasterIdx].Char.Spell[SpellSlot] = SpellNum;

        var npc = world.Npcs[NpcNum];
        npc.Name = "training dummy";
        npc.Behavior = NpcBehavior.AttackOnSight;   // Friendly and Stationary refuse a cast by design
        npc.Str = 1; npc.Def = 0; npc.Int = 1; npc.Spd = 1;

        var mapNpc = world.MapNpcs[Map, NpcSlot];
        mapNpc.Num = NpcNum;
        mapNpc.X = 8; mapNpc.Y = 7;
        mapNpc.Hp = 100_000;
        mapNpc.Mp = 100_000;
        mapNpc.Sp = 0;                              // no stamina: it cannot turn the cast aside

        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return new SpellSystem(world, pm, dispatcher, combat, items: null!);
    }

    [Test]
    public void AHarmfulSpellAtAnNpc_TakesItsMp()
    {
        var spells = Build(out var pm, out var world, SpellType.SubMp);
        world.Weather = WeatherType.Clear;
        var mapNpc = world.MapNpcs[Map, NpcSlot];
        pm[CasterIdx].TargetType = 1;
        pm[CasterIdx].Target = NpcSlot;
        pm[CasterIdx].TargetMap = Map;

        int before = mapNpc.Mp;
        pm[CasterIdx].AttackTimer = 0;
        spells.CastSpell(CasterIdx, SpellSlot);

        Assert.That(mapNpc.Mp, Is.LessThan(before), "a cast at an adjacent, defenceless NPC drained nothing");
    }

    [Test]
    public void AHarmfulSpellAtAPlayer_TakesTheirMp()
    {
        var spells = Build(out var pm, out var world, SpellType.SubMp);
        world.Weather = WeatherType.Clear;
        pm[CasterIdx].TargetType = 0;
        pm[CasterIdx].Target = VictimIdx;
        pm[CasterIdx].TargetMap = Map;

        int before = pm[VictimIdx].Char.Mp;
        pm[CasterIdx].AttackTimer = 0;
        spells.CastSpell(CasterIdx, SpellSlot);

        Assert.That(pm[VictimIdx].Char.Mp, Is.LessThan(before), "a cast at an adjacent player drained nothing");
    }

    [Test]
    public void AHealAtAPlayer_RestoresTheirHp()
    {
        var spells = Build(out var pm, out var world, SpellType.AddHp);
        world.Weather = WeatherType.Clear;
        pm[VictimIdx].Char.Hp = 100;                  // room to heal into
        pm[CasterIdx].TargetType = 0;
        pm[CasterIdx].Target = VictimIdx;
        pm[CasterIdx].TargetMap = Map;

        int before = pm[VictimIdx].Char.Hp;
        pm[CasterIdx].AttackTimer = 0;
        spells.CastSpell(CasterIdx, SpellSlot);

        Assert.That(pm[VictimIdx].Char.Hp, Is.GreaterThan(before), "a heal on an adjacent player restored nothing");
    }

    /// <summary>A cast costs mana whether or not the caster is watching, and the beat has to be stamped or the
    /// cooldown means nothing.</summary>
    [Test]
    public void ALandedCast_ChargesManaAndTheBeat()
    {
        var spells = Build(out var pm, out var world, SpellType.SubMp);
        world.Weather = WeatherType.Clear;
        pm[CasterIdx].TargetType = 1;
        pm[CasterIdx].Target = NpcSlot;
        pm[CasterIdx].TargetMap = Map;

        int beforeMp = pm[CasterIdx].Char.Mp;
        pm[CasterIdx].AttackTimer = 0;
        spells.CastSpell(CasterIdx, SpellSlot);

        Assert.Multiple(() =>
        {
            Assert.That(pm[CasterIdx].Char.Mp, Is.LessThan(beforeMp), "a landed cast cost no mana");
            Assert.That(pm[CasterIdx].AttackTimer, Is.Not.Zero, "a landed cast left the caster off cooldown");
        });
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
