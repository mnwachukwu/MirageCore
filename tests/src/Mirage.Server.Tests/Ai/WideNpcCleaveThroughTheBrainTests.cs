using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// A wide NPC's melee cleave, driven through the REAL brain — <see cref="NpcAiSystem.RunForAllMaps"/> — the
/// way a live server drives it.
///
/// <para>🔴 Calling <c>CombatSystem.NpcAttackPlayer</c> directly proves only that the damage code cleaves
/// once something decides to swing. Everything between the brain and that call — acquisition, facing,
/// reach, the turn-then-swing beat — is where a cleave actually goes missing, and a test that starts below
/// it will pass on a build where nothing cleaves in game.</para>
///
/// <para>The shape under test: a size-3 body faces its target and swings ONCE, and everything standing on
/// the three tiles past its leading edge is struck. Two players pressed against the same face is the case
/// a player sees and expects.</para>
///
/// <para>🔴 EVERY NPC HERE IS PINNED TO A DETERMINISTIC MODALITY, because the brain's melee-vs-magic weave
/// is a coin flip and a test may not ride one. <see cref="NpcAiSystem"/> gives two bands with no roll in
/// them: <b>Int 0 always melees</b> (TryNpcMagicActionCore returns before any cast logic, whatever Str is),
/// and <b>Str 0 with Int above 0 always casts</b> (RollCastModality's pCast is Int/(Int+Str), so 1.0). A mob
/// with BOTH above 0 rolls, and no test in this file may sit there — a "flaky" cleave test is really a test
/// that watched the mob cast. <see cref="SeatWideMob"/> pins the melee band and <see cref="MakeCaster"/>
/// the magic one; both set the stat EXPLICITLY rather than leaning on the record default, so a change to
/// <see cref="NpcRecord"/> cannot quietly move a fixture into the rolling band. If a future test genuinely
/// needs the weave, inject an <see cref="IRandomSource"/> — GameSystem takes one — rather than sampling it.
/// </para>
/// </summary>
[TestFixture]
public class WideNpcCleaveThroughTheBrainTests
{
    const int Map = 1, NpcNum = 1, PlayerA = 3, PlayerB = 4;

    static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
                                      objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        var spawn = new SpawnSystem(world, pm, dispatcher);
        return new NpcAiSystem(world, pm, dispatcher, combat, movement, spawn, items: null!, blood);
    }

    /// <summary>A player who cannot dodge or block (SP 0) and cannot die during the run, so any drop in HP
    /// is the swing landing and nothing else.</summary>
    static PlayerRecord Seat(GameWorld world, PlayerManager pm, int index, int x, int y)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = 10;
        pc.MaxHp = 1_000_000;
        pc.Hp = 1_000_000;
        pc.Sp = 0;
        world.MapObservers[Map].Add(index);
        return pc;
    }

    /// <summary>A size-3 mob anchored at (5,5) — body x5..7, y5..7 — facing Right, so its leading edge is
    /// the column x=8 across rows 5..7. Targets <paramref name="target"/> and is ready to swing.</summary>
    static MapNpcRecord SeatWideMob(GameWorld world, int target, int size = 3)
    {
        var def = world.Npcs[NpcNum];
        def.Name = "big";
        def.Behavior = NpcBehavior.AttackOnSight;
        def.Size = size;
        def.Str = 200;
        def.Int = 0;              // 🔴 the melee half of the determinism rule — see the class remarks
        def.Def = 10;
        def.Spd = 10;

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 5;
        mn.Y = 5;
        mn.Dir = Direction.Right;
        mn.Hp = 999_999;
        mn.Sp = 20;
        mn.Target = target;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = target;
        return mn;
    }

    /// <summary>Run the brain until the mob has landed at least one swing, or give up. Returns the damage
    /// each player took. Vitals are topped back up each tick so a long run cannot kill anyone.</summary>
    static (int A, int B) DriveUntilItSwings(NpcAiSystem ai, MapNpcRecord mn, PlayerRecord a, PlayerRecord b)
    {
        long tick = 1_000_000;
        for (int i = 0; i < 40; i++)
        {
            mn.AttackTimer = 0;                       // clear the melee cooldown so it may swing this tick
            mn.Sp = 20;
            mn.CombatExpiresAt = tick + 10_000_000;   // stay engaged as the AI clock advances
            ai.RunForAllMaps(tick);
            int da = 1_000_000 - a.Hp, db = 1_000_000 - b.Hp;
            if (da > 0 || db > 0) return (da, db);
            tick += 1_000;
        }
        return (0, 0);
    }

    [Test]
    public void TwoPlayersOnTheSameFace_AreBothStruckByOneSwing()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var a = Seat(world, pm, PlayerA, 8, 5);   // both on the leading edge column
        var b = Seat(world, pm, PlayerB, 8, 6);
        var mn = SeatWideMob(world, target: PlayerA);

        var (da, db) = DriveUntilItSwings(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.GreaterThan(0),
                "the second player is on the same face, one swing away — the cleave did not reach them");
        });
    }

    [Test]
    public void APlayerBehindTheFace_IsNotStruck()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var a = Seat(world, pm, PlayerA, 8, 5);   // on the leading edge
        var b = Seat(world, pm, PlayerB, 4, 5);   // behind the mob, off the faced edge entirely
        var mn = SeatWideMob(world, target: PlayerA);

        var (da, db) = DriveUntilItSwings(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.Zero, "a cleave is the FACED edge, not a circle around the body");
        });
    }

    [Test]
    public void ASingleTileMob_StrikesOnlyItsTarget()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        // Size 1 anchored at (5,5) faces Right onto (6,5) only; the second player stands beside that tile.
        var a = Seat(world, pm, PlayerA, 6, 5);
        var b = Seat(world, pm, PlayerB, 6, 6);
        var mn = SeatWideMob(world, target: PlayerA, size: 1);

        var (da, db) = DriveUntilItSwings(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.Zero, "a one-tile body has a one-tile face");
        });
    }

    // ── The same width, cast ─────────────────────────────────────────────────────
    // A body that cleaves three tiles wide in melee must not become a single-tile threat by raising a hand.
    // The splash runs perpendicular to the spell's flight and reaches size-1 each way, so there is no side
    // of the impact a player can choose to stand on.

    /// <summary>Turns the wide mob into a PURE caster: Str 0, Int &gt; 0. See the class remarks — that is the
    /// magic half of the determinism rule, and the brain reaches for a spell every beat.</summary>
    static void MakeCaster(GameWorld world, MapNpcRecord mn)
    {
        var def = world.Npcs[NpcNum];
        def.Str = 0;
        def.Int = 200;
        mn.Mp = 10_000;
    }

    /// <summary>Cast the spell directly, the way the brain's cast branch does once it has decided to.
    /// ⚠️ This starts BELOW the decision, so it proves the splash's shape and nothing about whether a
    /// caster reaches for a spell in the first place.</summary>
    static (int A, int B) CastAt(GameWorld world, PlayerManager pm, MapNpcRecord mn, int target,
                                 PlayerRecord a, PlayerRecord b)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
                                      objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        combat.NpcCastSpellOnPlayer(Map, 1, mn, target, 1_000_000);
        return (1_000_000 - a.Hp, 1_000_000 - b.Hp);
    }

    [TestCase(3, TestName = "A 3x3 caster's spell reaches two tiles either side of the impact")]
    [TestCase(2, TestName = "A 2x2 caster's spell reaches one tile either side of the impact")]
    public void ACastersSplash_IsAsWideAsItsBody(int size)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        // The spell travels along x, so the splash runs along y. The bystander stands at the far edge of it.
        var a = Seat(world, pm, PlayerA, 10, 6);
        var b = Seat(world, pm, PlayerB, 10, 6 + (size - 1));
        var mn = SeatWideMob(world, target: PlayerA, size: size);
        MakeCaster(world, mn);

        var (da, db) = CastAt(world, pm, mn, PlayerA, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.GreaterThan(0),
                $"a size-{size} caster's splash should reach {size - 1} tile(s) out, "
                + "so a player cannot pick a safe side of the impact");
        });
    }

    [Test]
    public void ACastersSplash_StopsAtItsWidth()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = Seat(world, pm, PlayerA, 10, 5);
        var b = Seat(world, pm, PlayerB, 10, 8);   // 3 out from a caster that reaches 2
        var mn = SeatWideMob(world, target: PlayerA, size: 3);
        MakeCaster(world, mn);

        var (da, db) = CastAt(world, pm, mn, PlayerA, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.Zero, "the splash is the body's width, not a free extra tile");
        });
    }

    [Test]
    public void ASingleTileCaster_SplashesNobody()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = Seat(world, pm, PlayerA, 10, 5);
        var b = Seat(world, pm, PlayerB, 10, 6);
        var mn = SeatWideMob(world, target: PlayerA, size: 1);
        MakeCaster(world, mn);

        var (da, db) = CastAt(world, pm, mn, PlayerA, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.Zero, "a one-tile body throws a one-tile spell");
        });
    }

    // ── The same swing, against other NPCs ───────────────────────────────────────
    // A mob fighting mobs runs a different brain path (the NPC-target branch) and a different strike path
    // than a mob fighting players, so proving one says nothing about the other.

    /// <summary>An enemy mob of its own type, placed on the wide mob's leading edge.</summary>
    static MapNpcRecord SeatEnemy(GameWorld world, int slot, int num, int x, int y)
    {
        var def = world.Npcs[num];
        def.Name = "enemy" + num;
        def.Behavior = NpcBehavior.AttackOnSight;
        def.Def = 1;
        def.Spd = 0;              // no SP to block or dodge with
        def.Str = 20;
        def.Int = 0;              // these are ticked by the brain too — same determinism rule as the wide mob
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 1_000_000;
        mn.Sp = 0;
        return mn;
    }

    static (int A, int B) DriveUntilItSwingsAtNpcs(NpcAiSystem ai, MapNpcRecord mn, MapNpcRecord a, MapNpcRecord b)
    {
        long tick = 1_000_000;
        for (int i = 0; i < 40; i++)
        {
            mn.AttackTimer = 0;
            mn.Sp = 20;
            mn.CombatExpiresAt = tick + 10_000_000;
            ai.RunForAllMaps(tick);
            int da = 1_000_000 - a.Hp, db = 1_000_000 - b.Hp;
            if (da > 0 || db > 0) return (da, db);
            tick += 1_000;
        }
        return (0, 0);
    }

    [Test]
    public void TwoEnemyNpcsOnTheSameFace_AreBothStruckByOneSwing()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);   // somebody is watching, so the full brain runs

        var a = SeatEnemy(world, slot: 2, num: 2, x: 8, y: 5);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 8, y: 6);
        var mn = SeatWideMob(world, target: 0);
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;             // its quarry is the first enemy
        mn.NpcTargetSpawnSlot = 2;

        var (da, db) = DriveUntilItSwingsAtNpcs(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.GreaterThan(0),
                "the second mob is on the same face, one swing away — the cleave did not reach it");
        });
    }

    /// <summary>Matt's case, watched live: a 3x3 facing DOWN with two mobs south of it and an empty tile
    /// between them — the two outer columns of its three-tile face. Both are on the swing.</summary>
    [Test]
    public void TwoEnemyNpcsWithAGapBetweenThem_AreBothStruck()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);

        // Body x5..7, y5..7 facing Down → face is row 8, columns 5,6,7.
        var a = SeatEnemy(world, slot: 2, num: 2, x: 5, y: 8);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 7, y: 8);   // gap at (6,8)
        var mn = SeatWideMob(world, target: 0);
        mn.Dir = Direction.Down;
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;

        var (da, db) = DriveUntilItSwingsAtNpcs(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.GreaterThan(0),
                "the second mob is on the same three-tile face, with only an empty tile between them");
        });
    }

    /// <summary>The same, but the two south mobs are the SAME TYPE as each other — the ordinary case when a
    /// pack fights something. Neither is allied to the attacker, so both are on the swing.</summary>
    [Test]
    public void TwoEnemyNpcsOfOneType_AreBothStruck()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);

        var a = SeatEnemy(world, slot: 2, num: 2, x: 5, y: 8);
        var b = SeatEnemy(world, slot: 3, num: 2, x: 7, y: 8);   // same NPC num as the first
        var mn = SeatWideMob(world, target: 0);
        mn.Dir = Direction.Down;
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;

        var (da, db) = DriveUntilItSwingsAtNpcs(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(db, Is.GreaterThan(0), "two of a kind are not the ATTACKER's kin");
        });
    }

    /// <summary>🔴 A VISITING npc on the face. A guest is a body standing on the map like any other, and the
    /// targeting code has always seen them — but a sweep that reads only the native slot array walks past
    /// one, so a wide mob mid-fight simply fails to hit whatever chased something in across the seam.</summary>
    [Test]
    public void AVisitingNpcOnTheFace_IsStruckToo()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);

        var native = SeatEnemy(world, slot: 2, num: 2, x: 5, y: 8);
        world.Npcs[3].Name = "guest";
        world.Npcs[3].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[3].Spd = 0;
        var guest = new TraversalNpcRecord
        {
            Num = 3, SpawnMapNum = Map, SpawnSlot = 9, CurrentMapNum = Map,
            X = 7, Y = 8, Hp = 1_000_000, Sp = 0,
        };
        world.MapTraversalNpcs[Map].Add(guest);

        var mn = SeatWideMob(world, target: 0);
        mn.Dir = Direction.Down;
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;

        var (dn, dg) = DriveUntilItSwingsAtNpcs(ai, mn, native, guest);

        Assert.Multiple(() =>
        {
            Assert.That(dn, Is.GreaterThan(0), "the mob never struck its own target");
            Assert.That(dg, Is.GreaterThan(0), "the visitor was standing on the same face and was walked past");
        });
    }

    // ── The same splash, against other NPCs ──────────────────────────────────────
    // NpcCastSpellOnNpc is a separate path from NpcCastSpellOnPlayer and had NO splash at all, so the
    // player-side coverage above proved nothing about a mob caught in a mob's spell.

    /// <summary>Cast at another NPC directly, the way the brain's cast branch does once it has decided to.
    /// ⚠️ This starts BELOW the decision, so it proves the splash's shape and nothing about whether a
    /// caster reaches for a spell in the first place.</summary>
    static (int A, int B) CastAtNpc(GameWorld world, PlayerManager pm, MapNpcRecord mn, int victimSlot,
                                    MapNpcRecord a, MapNpcRecord b)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
                                      objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        combat.NpcCastSpellOnNpc(Map, 1, mn, Map, victimSlot, a);
        return (1_000_000 - a.Hp, 1_000_000 - b.Hp);
    }

    [TestCase(3, TestName = "A 3x3 caster's spell reaches two tiles either side of an NPC it lands on")]
    [TestCase(2, TestName = "A 2x2 caster's spell reaches one tile either side of an NPC it lands on")]
    public void AnNpcCastersSplash_IsAsWideAsItsBody(int size)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        // Facing Right, so the spell travels along x and the splash runs along y.
        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 6);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 6 + (size - 1));
        var mn = SeatWideMob(world, target: 0, size: size);
        MakeCaster(world, mn);

        var (da, db) = CastAtNpc(world, pm, mn, victimSlot: 2, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.GreaterThan(0),
                $"a size-{size} caster's splash should reach {size - 1} tile(s) out, "
                + "so a mob cannot pick a safe side of the impact");
        });
    }

    /// <summary>The splash is a span of tiles, not a spear down a line: an empty tile between two mobs
    /// costs the far one nothing.</summary>
    [Test]
    public void AnNpcCastersSplash_CrossesAGap()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 5);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 7);   // nothing standing at (10,6)
        var mn = SeatWideMob(world, target: 0, size: 3);
        MakeCaster(world, mn);

        var (da, db) = CastAtNpc(world, pm, mn, victimSlot: 2, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.GreaterThan(0), "the empty tile between them stopped the splash");
        });
    }

    [Test]
    public void AnNpcCastersSplash_StopsAtItsWidth()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 5);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 8);   // 3 out from a caster that reaches 2
        var mn = SeatWideMob(world, target: 0, size: 3);
        MakeCaster(world, mn);

        var (da, db) = CastAtNpc(world, pm, mn, victimSlot: 2, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.Zero, "the splash is the body's width, not a free extra tile");
        });
    }

    [Test]
    public void ASingleTileNpcCaster_SplashesNobody()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 5);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 6);
        var mn = SeatWideMob(world, target: 0, size: 1);
        MakeCaster(world, mn);

        var (da, db) = CastAtNpc(world, pm, mn, victimSlot: 2, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.Zero, "a one-tile body throws a one-tile spell");
        });
    }

    /// <summary>A warband does not mince itself: the splash spares an ally standing in it, the same rule the
    /// melee cleave acquires by.</summary>
    [Test]
    public void AnNpcCastersSplash_SparesAnAlly()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 5);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 6);
        var mn = SeatWideMob(world, target: 0, size: 3);
        MakeCaster(world, mn);
        world.Npcs[NpcNum].Group = 7;
        world.Npcs[3].Group = 7;              // the bystander now shares the caster's warband

        var (da, db) = CastAtNpc(world, pm, mn, victimSlot: 2, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the caster never hit its own target");
            Assert.That(db, Is.Zero, "an ally standing in the splash was burned by its own side");
        });
    }

    // ── The splash, decided by the brain ─────────────────────────────────────────
    // Everything above starts BELOW the cast decision. These start above it: a PURE caster (Str 0, Int > 0)
    // makes RollCastModality's pCast = Int/(Int+Str) exactly 1.0, so the weave picks magic every beat and
    // the brain reaches for a spell on its own. Matt's observation, and the thing that makes an end-to-end
    // cast test possible at all.

    [Test]
    public void TwoPlayersInTheSplash_AreBothStruck_ThroughTheBrain()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        // Body x5..7; targets east at a 2-tile gap — inside the r=5 spell circle, outside melee, so the
        // caster holds at range and throws rather than closing. Splash runs along y: 2 out from the impact.
        var a = Seat(world, pm, PlayerA, 10, 6);
        var b = Seat(world, pm, PlayerB, 10, 8);
        var mn = SeatWideMob(world, target: PlayerA, size: 3);
        MakeCaster(world, mn);

        var (da, db) = DriveUntilItSwings(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the brain never cast at its own target");
            Assert.That(db, Is.GreaterThan(0),
                "the bystander was two tiles from the impact — inside a 3x3's splash — and was missed");
        });
    }

    [Test]
    public void TwoEnemyNpcsInTheSplash_AreBothStruck_ThroughTheBrain()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);   // somebody is watching, so the full brain runs

        var a = SeatEnemy(world, slot: 2, num: 2, x: 10, y: 6);
        var b = SeatEnemy(world, slot: 3, num: 3, x: 10, y: 8);
        var mn = SeatWideMob(world, target: 0, size: 3);
        MakeCaster(world, mn);
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;

        var (da, db) = DriveUntilItSwingsAtNpcs(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "the brain never cast at its own target");
            Assert.That(db, Is.GreaterThan(0),
                "the second mob was two tiles from the impact — inside a 3x3's splash — and was missed");
        });
    }

    // ── The target leaves, the face does not ─────────────────────────────────────
    // Matt, watching live: a 3x3 stopped attacking three mobs along its edge because ONE of the three
    // walked away. That one was its target, and the swing gate asks whether the TARGET is in reach — so
    // the whole swing was refused while two enemies stayed pressed against the face.

    [Test]
    public void WhenTheTargetLeavesTheFace_TheOthersStillOnItAreStruck()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        world.MapObservers[Map].Add(PlayerA);

        // Body x5..7 facing Right; the face is column 8, rows 5..7.
        var runner = SeatEnemy(world, slot: 2, num: 2, x: 8, y: 5);
        var a = SeatEnemy(world, slot: 3, num: 3, x: 8, y: 6);
        var b = SeatEnemy(world, slot: 4, num: 4, x: 8, y: 7);
        var mn = SeatWideMob(world, target: 0);
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;          // its quarry is the one that will leave

        runner.X = 13;                      // ...and it walks off the face, well out of reach
        runner.Y = 5;

        var (da, db) = DriveUntilItSwingsAtNpcs(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0),
                "the target left, so the mob refused to swing at the enemy still on its face");
            Assert.That(db, Is.GreaterThan(0), "the second enemy on the face was missed by the same swing");
            Assert.That(runner.Hp, Is.EqualTo(1_000_000), "the one that walked away is out of reach and untouched");
        });
    }

    [Test]
    public void WhenTheTargetLeavesTheFace_PlayersStillOnItAreStruck()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var a = Seat(world, pm, PlayerA, 8, 6);
        var b = Seat(world, pm, PlayerB, 8, 7);
        var mn = SeatWideMob(world, target: PlayerA);

        // The mob's own target is an NPC that then leaves; the players never move off the face.
        var runner = SeatEnemy(world, slot: 2, num: 2, x: 8, y: 5);
        mn.Target = 0;
        mn.NpcTargetSpawnMap = Map;
        mn.NpcTargetSpawnSlot = 2;
        runner.X = 13;

        var (da, db) = DriveUntilItSwings(ai, mn, a, b);

        Assert.Multiple(() =>
        {
            Assert.That(da, Is.GreaterThan(0), "a player standing on the face was not struck once the target left");
            Assert.That(db, Is.GreaterThan(0), "the second player on the face was missed by the same swing");
        });
    }

    /// <summary>The fallback swings at what is THERE, and offers nothing when the face is empty.
    ///
    /// <para>⚠️ Asked of <see cref="CombatSystem.FirstVictimOnFace"/> directly rather than driven through the
    /// brain. Driving it cannot answer this question: with its target gone the mob CHASES, and over enough
    /// ticks it reaches the runner and hits it legitimately — a pass or fail that turns on how far the legs
    /// got, not on whether the fallback fired.</para></summary>
    [Test]
    public void AnEmptyFace_OffersNobody()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = NewCombatFor(world, pm);

        var mn = SeatWideMob(world, target: 0);          // body x5..7, facing Right; face is column 8
        SeatEnemy(world, slot: 2, num: 2, x: 13, y: 5);  // the one that walked away, well past the face
        SeatEnemy(world, slot: 3, num: 3, x: 2, y: 5);   // behind the body, off the faced edge entirely
        mn.AttackTimer = 0;

        Assert.That(combat.FirstVictimOnFace(Map, mn, 1_000_000), Is.Null,
            "the face is empty — a mob past it or behind the body is not on it");
    }

    /// <summary>The same question with someone actually standing there, so the null above is a real answer
    /// and not a method that never finds anyone.</summary>
    [Test]
    public void AFaceWithSomeoneOnIt_OffersThem()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = NewCombatFor(world, pm);

        var mn = SeatWideMob(world, target: 0);
        var onFace = SeatEnemy(world, slot: 2, num: 2, x: 8, y: 6);   // column 8, inside the faced strip
        mn.AttackTimer = 0;

        var found = combat.FirstVictimOnFace(Map, mn, 1_000_000);
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Value.Npc, Is.SameAs(onFace), "the body standing on the face is the one offered");
    }

    static CombatSystem NewCombatFor(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
                                objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    sealed class NoOpDispatcher : IPacketDispatcher
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
