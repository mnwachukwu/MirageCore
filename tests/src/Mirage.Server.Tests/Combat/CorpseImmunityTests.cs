using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Combat;

// Corpse (timed dead-state) combat immunity + logout-while-dead. A corpse == Char.Dead && Hp == 0.
//
// Regression lock for the reported bug: NPCs kept hitting a corpse (Hp == 0), so every hit re-entered the
// lethal branch `damage >= Hp` and re-ran EnterDeadState — the escalating respawn penalty (RespawnPenaltySteps)
// and the countdown (RespawnReadyUtc) reset on EVERY hit, so the timer "maxed out" and never counted down. Each
// hit also re-stamped combat, which blocked logout and, on disconnect, made an uncleanable dead-ghost. The fix
// gates every incoming-damage path and NPC target-acquisition on Char.Dead, freezes a corpse's own actions, and
// makes a dead player always take the normal-leave path on logout (never a ghost). Player-vs-player already
// refused a dead (Hp <= 0) target; that is covered here too so it can't regress.
[TestFixture]
public class CorpseImmunityTests
{
    const int Map = 1, NpcNum = 1, CorpseIdx = 5, AttackerIdx = 6, VictimIdx = 7;

    // Fixed sentinels so the "unchanged" assertions don't depend on DeathFormulas (covered by DeathFormulasTests).
    const long RespawnSentinel = 9_999_999_999L;
    const int PenaltySentinel = 3;

    // ── 1. The reported bug, end-to-end through the real brain ─────────────────
    // A native AoS mob pre-locked and adjacent to a corpse (the exact shape GuestNativeScenarioParityTests uses to
    // land melee hits on a LIVE player) must land NOTHING on a corpse: no damage, and — crucially — no reset of the
    // respawn penalty/countdown and no combat re-stamp.
    [Test]
    public void PreLockedNpc_CannotReKillCorpse_TimerNeverResets()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 0;
        npc.Spd = 10;

        // Level < 10 keeps a REVERTED run failing cleanly: the sub-10 death path is spared drops, so it re-runs
        // EnterDeadState (caught by the assertions) instead of NRE-ing on the null _items drop path.
        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 7, level: 5);

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Dir = Direction.Down;  // adjacent + facing the corpse at (8,7)
        mn.Hp = 9999;
        mn.Mp = 50;
        mn.Sp = 20;
        mn.Target = CorpseIdx;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = CorpseIdx;  // stale lock on the corpse

        long ready = pm[CorpseIdx].Char.RespawnReadyUtc;
        int steps = pm[CorpseIdx].Char.RespawnPenaltySteps;

        long tick = 1_000_000;
        for (int k = 0; k < 8; k++)
        {
            mn.AttackTimer = 0;                       // clear the melee cooldown so it would swing this tick
            mn.Sp = 20;
            mn.CombatExpiresAt = tick + 10_000_000;   // stay engaged as the AI clock advances
            ai.RunForAllMaps(tick);
            tick += 1_000;
        }

        Assert.Multiple(() =>
        {
            Assert.That(pm[CorpseIdx].Char.Hp, Is.EqualTo(0), "a corpse takes no damage");
            Assert.That(pm[CorpseIdx].Char.RespawnReadyUtc, Is.EqualTo(ready), "respawn countdown must not reset");
            Assert.That(pm[CorpseIdx].Char.RespawnPenaltySteps, Is.EqualTo(steps), "respawn penalty must not escalate");
            Assert.That(pm[CorpseIdx].CombatExpiresAt, Is.EqualTo(0), "no combat re-stamp on a corpse");
        });
    }

    // ── 2. NPC melee gate ──────────────────────────────────────────────────────
    [Test]
    public void CanNpcAttackPlayer_False_ForCorpse()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombat(world, pm);
        world.Npcs[NpcNum].Behavior = NpcBehavior.AttackOnSight;

        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 7, level: 5);
        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Hp = 100;  // NPC valid + adjacent, so the Dead guard is what fails it

        Assert.That(combat.CanNpcAttackPlayer(Map, mn, CorpseIdx, Environment.TickCount64), Is.False);
    }

    // ── 3. Shared NPC→player damage body no-ops on a corpse ───────────────────
    [Test]
    public void ApplyNpcDamageToPlayer_NoOp_ForCorpse()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombat(world, pm);
        world.Npcs[NpcNum].Str = 50;
        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 7, level: 5);

        long ready = pm[CorpseIdx].Char.RespawnReadyUtc;
        int steps = pm[CorpseIdx].Char.RespawnPenaltySteps;

        // Private (no InternalsVisibleTo): (int mapNum, NpcRecord npcRec, int victimIndex, int damage, bool wasCrit, bool isSpell)
        typeof(CombatSystem).GetMethod("ApplyNpcDamageToPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(combat, new object[] { Map, world.Npcs[NpcNum], CorpseIdx, 50, false, false });

        Assert.Multiple(() =>
        {
            Assert.That(pm[CorpseIdx].Char.Hp, Is.EqualTo(0));
            Assert.That(pm[CorpseIdx].Char.RespawnReadyUtc, Is.EqualTo(ready));
            Assert.That(pm[CorpseIdx].Char.RespawnPenaltySteps, Is.EqualTo(steps));
        });
    }

    // ── 4. NPC spell no-ops on a corpse ────────────────────────────────────────
    [Test]
    public void NpcCastSpellOnPlayer_NoOp_ForCorpse()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombat(world, pm);
        var npc = world.Npcs[NpcNum];
        npc.Int = 30;
        npc.Behavior = NpcBehavior.AttackOnSight;

        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 7, level: 5);
        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Hp = 100;
        mn.Mp = 9999;  // caster valid + funded, so the Dead guard is the seam

        long ready = pm[CorpseIdx].Char.RespawnReadyUtc;
        int steps = pm[CorpseIdx].Char.RespawnPenaltySteps;

        combat.NpcCastSpellOnPlayer(Map, 1, mn, CorpseIdx, Environment.TickCount64);

        Assert.Multiple(() =>
        {
            Assert.That(pm[CorpseIdx].Char.Hp, Is.EqualTo(0));
            Assert.That(pm[CorpseIdx].Char.RespawnReadyUtc, Is.EqualTo(ready));
            Assert.That(pm[CorpseIdx].Char.RespawnPenaltySteps, Is.EqualTo(steps));
            Assert.That(pm[CorpseIdx].CombatExpiresAt, Is.EqualTo(0));
        });
    }

    // ── 5. NPC target acquisition excludes corpses ─────────────────────────────
    [Test]
    public void FindLowestLevelPlayer_SkipsCorpse()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var mob = PlaceNpc(world, 1, num: NpcNum, x: 8, y: 6);
        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 8, level: 5);   // the only candidate is dead

        int winner = (int)InvokePrivate(NewAi(world, pm), "FindLowestLevelPlayer", Map, mob, 15);

        Assert.That(winner, Is.EqualTo(0), "an AoS mob must not acquire a corpse (else it re-locks and re-kills every beat)");
    }

    [Test]
    public void FindGuardTarget_SkipsDeadPkPlayer()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var guard = PlaceNpc(world, 1, num: NpcNum, x: 8, y: 6);
        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 8, level: 5);
        pm[CorpseIdx].Char.PkExpiryUtc = long.MaxValue;   // dead AND PK: death doesn't clear PK, so only the Dead filter excludes it

        int winner = (int)InvokePrivate(NewAi(world, pm), "FindGuardTarget", Map, guard, 0L);

        Assert.That(winner, Is.EqualTo(0), "a guard must not target a corpse even though it is still flagged PK");
    }

    // ── 6. PvP parity: a player cannot attack a corpse either (locks the existing Hp <= 0 gate) ──
    [Test]
    public void CanAttackPlayer_False_ForCorpse()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombat(world, pm);
        RegisterLivePlayer(world, pm, AttackerIdx, x: 8, y: 6, level: 10).Dir = Direction.Down;
        PlaceCorpse(world, pm, CorpseIdx, x: 8, y: 7, level: 10);

        Assert.That(combat.CanAttackPlayer(AttackerIdx, CorpseIdx), Is.False);
    }

    // ── 7. Action-freeze: a corpse cannot attack ───────────────────────────────
    // Asserted via a deterministic side effect that fires the instant an attack is accepted (MarkPlayerCombat,
    // before any damage/weapon math): a living attacker facing an adjacent victim stamps CombatExpiresAt; the same
    // attacker, once a corpse, returns from HandleAttack before stamping anything.
    [Test]
    public void DeadAttacker_CannotMelee()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombat(world, pm);
        var atk = RegisterLivePlayer(world, pm, AttackerIdx, x: 8, y: 6, level: 10);
        atk.Dir = Direction.Down;                                   // faces the victim at (8,7)
        RegisterLivePlayer(world, pm, VictimIdx, x: 8, y: 7, level: 10);   // live, adjacent, attackable

        // Control: a living attacker is accepted and enters combat.
        combat.HandleAttack(AttackerIdx);
        Assert.That(pm[AttackerIdx].CombatExpiresAt, Is.GreaterThan(0), "control: a living attack must be accepted (marks combat)");

        // Freeze: the same attacker, now a corpse, is refused before any combat stamp.
        pm[AttackerIdx].CombatExpiresAt = 0;
        pm[AttackerIdx].Char.Dead = true;
        pm[AttackerIdx].Char.Hp = 0;
        combat.HandleAttack(AttackerIdx);
        Assert.That(pm[AttackerIdx].CombatExpiresAt, Is.EqualTo(0), "a corpse cannot attack");
    }

    // ── 8. Logout while dead: a corpse always takes the normal-leave path, never a ghost ──
    [Test]
    public void DeadPlayer_LeftGame_DoesNotBecomeGhost()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var jl = BuildJoinLeave(world, pm);

        var sp = pm[CorpseIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "corpse";
        sp.Char.Map = Map;
        sp.Char.Dead = true;
        sp.Char.Hp = 0;
        sp.CombatExpiresAt = long.MaxValue;   // in combat AND dead: the Dead term must win so it never ghosts

        jl.LeftGame(CorpseIdx);

        Assert.Multiple(() =>
        {
            Assert.That(sp.IsGhost, Is.False, "a corpse must never become a combat ghost");
            Assert.That(sp.InGame, Is.False, "the normal-leave path must complete");
        });
    }

    // Contrast: a non-dead in-combat player DOES ghost — guards against accidentally disabling ghosting entirely.
    [Test]
    public void LivePlayer_InCombat_LeftGame_BecomesGhost()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var jl = BuildJoinLeave(world, pm);

        var sp = pm[AttackerIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "fighter";
        sp.Char.Map = Map;
        sp.Char.Hp = 100;
        sp.CombatExpiresAt = long.MaxValue;   // in combat, alive

        jl.LeftGame(AttackerIdx);

        Assert.That(sp.IsGhost, Is.True, "an in-combat disconnect still leaves a ghost when the player is alive");
    }

    // ── 9. Dying in combat sends the combat-exit notice (consistency with a natural expiry) ──
    [Test]
    public void DeathInCombat_SendsCombatEndedNotice()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new CapturingDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 0;
        npc.Spd = 10;
        var pc = RegisterLivePlayer(world, pm, CorpseIdx, x: 8, y: 7, level: 5);   // alive + Level<10, so the death penalty is spared (no null _items touch)
        pm[CorpseIdx].WasInCombat = true;
        pm[CorpseIdx].CombatExpiresAt = long.MaxValue;  // in combat when the killing blow lands

        // A lethal NPC hit (damage >= Hp) kills the in-combat victim.
        typeof(CombatSystem).GetMethod("ApplyNpcDamageToPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(combat, new object[] { Map, world.Npcs[NpcNum], CorpseIdx, 999, false, false });

        Assert.Multiple(() =>
        {
            Assert.That(pc.Dead, Is.True, "the victim died");
            Assert.That(dispatcher.Chats.Exists(c => c.Index == CorpseIdx && c.Key == ServerStrings.RegenerationSystem_CombatEnded), Is.True,
                "dying in combat must send the combat-exit notice, matching a natural combat expiry");
        });
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    // Full brain: real Combat/Movement/Blood/Spawn, no-op dispatcher, null for the sub-systems the melee path only
    // touches on a KILL (items/joinLeave) — a corpse is never killed, so they stay null.
    static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        var spawn = new SpawnSystem(world, pm, dispatcher);
        return new NpcAiSystem(world, pm, dispatcher, combat, movement, spawn, items: null!, blood);
    }

    // A CombatSystem with a no-op dispatcher + real blood/movement; the Dead guards all return before touching the
    // null items/joinLeave, so those stay null.
    static CombatSystem BuildCombat(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    // A real JoinLeaveSystem. LeftGame's normal-leave path only touches _guilds/_party/_saver/_logger/_dispatcher/
    // _world/_pm; a guildless + partyless player early-returns StampMemberLastSeen/DisbandParty, and PlayerSaver's
    // Chain swallows the null-persistence load, so the remaining sub-systems pass as null.
    static JoinLeaveSystem BuildJoinLeave(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var party = new PartySystem(pm, dispatcher);
        // Positional (persistence, bg, items, mail, objectives null): guildless players never acquire a quest, so
        // the objective-kernel + persistence deps stay untouched.
        var guilds = new GuildSystem(world, pm, dispatcher, null!, null!, saver, null!, null!, objectives: null!, NullLogger<GuildSystem>.Instance);
        // A real TradeSystem — LeftGame now calls _trade.OnPlayerGone (a no-op for a non-trading player).
        var trade = new TradeSystem(world, pm, dispatcher, items: null!, mail: null!, persistence: null!, saver: null!);
        // A real QuestSystem — LeftGame now calls _quests.OnPlayerGone, which only touches its own (empty)
        // tracking dict for a non-questing player, so all its deps can be null.
        var quests = new QuestSystem(world, pm, dispatcher, items: null!, mail: null!, objectives: null!,
            combat: new Lazy<CombatSystem>(() => null!), guildSchedule: null!);
        // Positional; nulls are movement, mail, social, items, shop, conversations, tod, weather, blood — none touched on the dead normal-leave path.
        return new JoinLeaveSystem(world, pm, dispatcher, saver, null!, party, guilds, null!, null!, trade, quests, null!, null!, null!, null!,
            NullLogger<JoinLeaveSystem>.Instance);
    }

    // Find* scanners dereference only _world and _pm, so the other six constructor deps are null (mirrors
    // NpcTargetAcquisitionTests).
    static NpcAiSystem NewAi(GameWorld world, PlayerManager pm) => new(world, pm, null!, null!, null!, null!, null!, null!);

    static MapNpcRecord PlaceNpc(GameWorld world, int slot, int num, int x, int y)
    {
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 100;
        return mn;
    }

    static PlayerRecord RegisterLivePlayer(GameWorld world, PlayerManager pm, int index, int x, int y, int level)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = level;
        pc.MaxHp = 100;
        pc.Hp = 100;
        pc.Access = AdminLevel.Player;   // ordinary player so PvP is allowed (GetPvpBlock == None)
        world.MapObservers[Map].Add(index);   // acquisition + targeting scan MapObservers; unobserved players are invisible
        return pc;
    }

    static PlayerRecord PlaceCorpse(GameWorld world, PlayerManager pm, int index, int x, int y, int level)
    {
        var pc = RegisterLivePlayer(world, pm, index, x, y, level);
        pc.Dead = true;
        pc.Hp = 0;
        pc.RespawnReadyUtc = RespawnSentinel;
        pc.RespawnPenaltySteps = PenaltySentinel;
        return pc;
    }

    static object InvokePrivate(NpcAiSystem ai, string method, params object[] args)
        => typeof(NpcAiSystem).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ai, args)!;

    // No-op packet dispatcher (copied from GuestNativeScenarioParityTests — the per-file convention).
    // SendLocalizedChatTo is virtual so CapturingDispatcher can record the combat-exit notice.
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
        public virtual void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
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

    // Records the localized chat lines sent to each player so a test can assert the combat-exit notice fires.
    sealed class CapturingDispatcher : NoOpDispatcher
    {
        public readonly List<(int Index, string Key)> Chats = new();
        public override void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args)
            => Chats.Add((index, key));
    }
}
