using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// Mirage Source Remastered's own arithmetic, running in Compass.
///
/// <para>🔴 <b>These numbers are not a design, they are a MEASUREMENT.</b> Every expectation below was
/// computed from the original's <c>StatFormulas</c> and <c>ExpFormulas</c> as they stand in
/// <c>D:\Repos\MirageSourceRemastered</c>, and the port is held to them exactly. A character built on
/// this engine has the pools it would have had on that one, or this fails.</para>
///
/// <para>⚠ <b>The risk this exists for is arithmetic that is nearly right.</b> A port that drops the
/// shift, rounds the wrong way, or divides before it multiplies produces numbers that look plausible
/// at level 1 and are wrong by hundreds at level 255 — which nobody notices until somebody plays that
/// far. So the sample spans the whole curve rather than its comfortable middle.</para>
/// </summary>
[TestFixture]
public class MsrStatsTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("mirage-msr-").FullName;

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>The game's own rules, loaded the way a server loads them, with one line added that
    /// asks the question. The scripts are the SHIPPED ones — a copy written for a test would be a
    /// second game that nothing plays.</summary>
    private (ScriptedWorldModule Module, ScriptedWorldTests.RecordingWorld World) Asking(
        string body, ScriptedWorldTests.RecordingWorld? standing = null)
    {
        // A world of its own per call: a test that asks several questions loads several times, and
        // a second copy into one folder is a file that already exists.
        string worldDir = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        string scripts = Path.Combine(worldDir, ScriptedWorldModule.ScriptsFolder);
        Directory.CreateDirectory(scripts);

        string from = Path.Combine(Repository(), "modules", "msr", "world", "scripts");
        foreach (string file in Directory.EnumerateFiles(from, "*.cm", SearchOption.AllDirectories))
        {
            string landing = Path.Combine(scripts, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(landing)!);
            File.Copy(file, landing);
        }

        File.WriteAllText(Path.Combine(scripts, "asking.cm"), $$"""
            shared model Asking
                public function OnPlayerTick(Player who)
            {{body}}
                end function
            end model
            """);

        // ⚠ HOOKED onto the game's own tick rather than added beside it. The engine looks only at
        // Rules and matches a handler by name and arity, so a second OnPlayerTick is not a second
        // handler - it is a file that does not compile.
        string rules = Path.Combine(scripts, "rules.cm");
        string door = File.ReadAllText(rules);

        Assert.That(door, Does.Contain("Vitals.Rest(who);"), "the tick this hooks onto has moved");

        File.WriteAllText(rules, door.Replace(
            "Vitals.Rest(who);",
            "Vitals.Rest(who);" + Environment.NewLine + "        Asking.OnPlayerTick(who);"));

        var module = new ScriptedWorldModule(worldDir);
        _declared = CoreRegistry.Build(module);

        var world = standing ?? new ScriptedWorldTests.RecordingWorld();
        module.Start(world);

        Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error).Select(p => p.Message),
            Is.Empty, "the shipped scripts have to load");

        return (module, world);
    }

    /// <summary>What the last load DECLARED — families, attributes, verbs.
    ///
    /// <para>⚠ Loading clean is not the same as declaring anything. A model whose <c>Describe</c> faults
    /// leaves a world that compiles, loads, reports no problem, and has no records in it.</para></summary>
    private CoreRegistry? _declared;

    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx"))) here = here.Parent;
        return here?.FullName ?? throw new InvalidOperationException("The repository root is not above here.");
    }

    private string Answered(string expression)
    {
        var (module, world) = Asking($"        who.Message(\"\" + {expression});");
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    /// <summary>🔴 The health pool, across the whole curve.</summary>
    [TestCase(1, 0, ExpectedResult = "26")]
    [TestCase(1, 20, ExpectedResult = "42")]
    [TestCase(10, 5, ExpectedResult = "68")]
    [TestCase(50, 20, ExpectedResult = "482")]
    [TestCase(120, 60, ExpectedResult = "2196")]
    [TestCase(255, 0, ExpectedResult = "7290")]
    [TestCase(255, 200, ExpectedResult = "9860")]
    public string TheHealthPoolMatchesTheOriginal(int level, int def) =>
        Answered($"Stats.MaxHealth({level}, {def}, 0)");

    /// <summary>⚠ Stamina is LINEAR where health is quadratic, and a port that gave them one shape
    /// would be wrong in a way that only shows at the top of the curve.</summary>
    [TestCase(1, 0, ExpectedResult = "2")]
    [TestCase(10, 20, ExpectedResult = "40")]
    [TestCase(255, 200, ExpectedResult = "710")]
    public string TheStaminaPoolIsLinear(int level, int spd) =>
        Answered($"Stats.MaxStamina({level}, {spd}, 0)");

    /// <summary>Regen has the shape of the pool it fills, and a floor under it.</summary>
    [TestCase(0, ExpectedResult = "3")]
    [TestCase(20, ExpectedResult = "18")]
    [TestCase(200, ExpectedResult = "693")]
    public string HealthRegenMatchesTheOriginal(int def) => Answered($"Stats.HealthRegen({def})");

    /// <summary>⚠ The floor is the rate a body with nothing in the stat regenerates at, higher for
    /// stamina than for the other two because stamina is the only one anybody ever sits on.</summary>
    [TestCase(0, ExpectedResult = "4")]
    [TestCase(5, ExpectedResult = "4")]
    [TestCase(20, ExpectedResult = "10")]
    [TestCase(200, ExpectedResult = "100")]
    public string StaminaRegenHasItsOwnFloor(int spd) => Answered($"Stats.StaminaRegen({spd})");

    /// <summary>What the next level costs, and what a sheet may hold at this one.</summary>
    [TestCase(1, ExpectedResult = "500")]
    [TestCase(10, ExpectedResult = "50000")]
    [TestCase(255, ExpectedResult = "32512500")]
    public string TheExperienceCurveMatchesTheOriginal(int level) =>
        Answered($"Stats.ToNextLevel({level})");

    [TestCase(1, ExpectedResult = "20")]
    [TestCase(10, ExpectedResult = "47")]
    [TestCase(255, ExpectedResult = "782")]
    public string ThePointBudgetMatchesTheOriginal(int level) => Answered($"Stats.PointBudget({level})");

    // ── Vitals ──────────────────────────────────────────────────────────

    /// <summary>A new character's pools are sized to the sheet that was just written, and full.</summary>
    [Test]
    public void ANewCharactersPoolsAreSizedAndFull()
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        ((IWorldObserver)scripts).OnPlayerJoined(EntityHandle.ForPlayer(1));

        // Level 1 with five in each stat: the original's own numbers for that sheet.
        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "maxhp"), Is.EqualTo(29L));
            Assert.That(Held(world, "maxmp"), Is.EqualTo(29L));
            Assert.That(Held(world, "maxsp"), Is.EqualTo(7L));

            Assert.That(Held(world, "hp"), Is.EqualTo(29L), "and full");
            Assert.That(Held(world, "mp"), Is.EqualTo(29L));
            Assert.That(Held(world, "sp"), Is.EqualTo(7L));
        });
    }

    /// <summary>What the original paid, and how often it paid it.
    ///
    /// <para>The original ran five clocks: health every 2.5 seconds and every 1.25 on protected ground,
    /// mana and stamina every 2.5 and every 1, and mana every 5 while fighting. Neither health nor
    /// stamina comes back in a fight at all — stamina is the one pool paid out continuously, so a rate
    /// that kept running would refund a sprint about as fast as it was spent.</para>
    ///
    /// <para>The port pays on a one-second tick instead, so what is checked is THROUGHPUT over time
    /// rather than one tick's worth. A tick pays the share of an interval it is worth and carries the
    /// fraction, so a single tick rounds and ten of them do not.</para>
    /// </summary>
    [TestCase(false, 2.5, 2.5, 2.5, Description = "ordinary ground")]
    [TestCase(true, 2.5, 2.5, 2.5, Description = "in a fight, where only mana comes back")]
    public void RegenPaysWhatTheOriginalPaid(bool fighting, double _, double __, double ___)
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        // Empty pools with ceilings well clear of ten seconds' worth, so nothing is clamped.
        foreach (string key in (string[])["hp", "mp", "sp"])
            world.SetAttribute(who, key, AttributeValue.From(0L));
        foreach (string key in (string[])["maxhp", "maxmp", "maxsp"])
            world.SetAttribute(who, key, AttributeValue.From(10_000L));

        if (fighting) world.In.Add((who, "engaged"));

        for (int tick = 1; tick <= Seconds; tick++) ((ITickWork)scripts).Tick(tick);

        // Five in each stat, which enrollment leaves: regen of 6, 6 and 4 per interval.
        long health = fighting ? 0 : Paid(6, 2.5);
        long mana = Paid(6, fighting ? 5.0 : 2.5);
        long stamina = fighting ? 0 : Paid(4, 2.5);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "hp"), Is.EqualTo(health), "health");
            Assert.That(Held(world, "mp"), Is.EqualTo(mana), "mana");
            Assert.That(Held(world, "sp"), Is.EqualTo(stamina), "stamina");
        });
    }

    /// <summary>Protected ground pays health twice as often and the other two two and a half times as
    /// often, which is the original's own pair of ratios.</summary>
    [Test]
    public void ProtectedGroundPaysFaster()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        world.MapFields[1] = Row(("moral", 1L));

        var (module, _) = Asking("        yield;", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        foreach (string key in (string[])["hp", "mp", "sp"])
            world.SetAttribute(who, key, AttributeValue.From(0L));
        foreach (string key in (string[])["maxhp", "maxmp", "maxsp"])
            world.SetAttribute(who, key, AttributeValue.From(10_000L));

        for (int tick = 1; tick <= Seconds; tick++) ((ITickWork)scripts).Tick(tick);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "hp"), Is.EqualTo(Paid(6, 1.25)), "health, every 1.25s");
            Assert.That(Held(world, "mp"), Is.EqualTo(Paid(6, 1.0)), "mana, every second");
            Assert.That(Held(world, "sp"), Is.EqualTo(Paid(4, 1.0)), "stamina, every second");
        });
    }

    /// <summary>Long enough that a carried fraction has to have been carried rather than rounded
    /// away.</summary>
    private const int Seconds = 10;

    /// <summary>What the original pays over <see cref="Seconds"/>, given an amount and the interval it
    /// was paid on.</summary>
    private static long Paid(int amount, double everySeconds) =>
        (long)(amount * Seconds / everySeconds);


    /// <summary>⚠ A pool is never written past its ceiling, and a full one is not written at all —
    /// every write ships to everybody who can see the body.</summary>
    [Test]
    public void AFullPoolIsNotWrittenAgain()
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        world.SetAttribute(who, "hp", AttributeValue.From(28L));
        ((ITickWork)scripts).Tick(1);

        Assert.That(Held(world, "hp"), Is.EqualTo(29L), "topped up, not overfilled");
    }

    /// <summary>🔴 The ceiling is DERIVED, and a levelled character has the pool their level earns.
    ///
    /// <para>A maximum stored once at enrollment is wrong from the first level up, and the bar would
    /// read full at a value the character has outgrown.</para></summary>
    [Test]
    public void TheCeilingFollowsTheSheet()
    {
        var (module, world) = Asking("        Vitals.Refresh(who);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        world.SetAttribute(who, "level", AttributeValue.From(50L));
        world.SetAttribute(who, "def", AttributeValue.From(20L));

        ((ITickWork)scripts).Tick(1);

        Assert.That(Held(world, "maxhp"), Is.EqualTo(482L), "level 50, twenty defense");
    }

    // ── Combat ─────────────────────────────────────────────────────────

    /// <summary>🔴 What a swing is worth, and what a body turns aside.
    ///
    /// <para>⚠ Damage and mitigation share the exponent on purpose — they grow in step, so a fight at
    /// level 200 takes about as many swings as one at level 20. A port that got one of the two
    /// divisors wrong reads fine at low level and either plateaus or collapses at the top.</para>
    /// </summary>
    [TestCase(0, ExpectedResult = "9")]
    [TestCase(5, ExpectedResult = "13")]
    [TestCase(20, ExpectedResult = "25")]
    [TestCase(60, ExpectedResult = "72")]
    [TestCase(200, ExpectedResult = "326")]
    public string ASwingMatchesTheOriginal(int stat) => Answered($"Combat.Swing({stat})");

    [TestCase(1, 0, ExpectedResult = "9")]
    [TestCase(10, 20, ExpectedResult = "21")]
    [TestCase(50, 200, ExpectedResult = "255")]
    [TestCase(255, 0, ExpectedResult = "415")]
    [TestCase(255, 200, ExpectedResult = "617")]
    public string ProtectionMatchesTheOriginal(int level, int def) =>
        Answered($"Combat.Protection({level}, {def})");

    /// <summary>🔴 A hit that lands always does something.
    ///
    /// <para>Without the floor a defensive build becomes immune rather than durable and the fight
    /// stops being a fight — and a player hitting a creature floors HIGHER, so a low-offense build
    /// against a tanky one is slow rather than stuck.</para></summary>
    [TestCase(100, 30, "Combat.MinDamageFloor", ExpectedResult = "70")]
    [TestCase(100, 95, "Combat.MinDamageFloor", ExpectedResult = "12")]
    [TestCase(100, 95, "Combat.PveDamageFloor", ExpectedResult = "35")]
    [TestCase(40, 500, "Combat.PveDamageFloor", ExpectedResult = "14")]
    public string DamageIsFlooredLikeTheOriginal(int swing, int protection, string floor) =>
        Answered($"Combat.Resolve({swing}, {protection}, {floor})");

    /// <summary>A player's chance counts their level as well as the stat, so the caps want real
    /// investment; a creature's counts the stat alone and caps lower, because its defense already
    /// buys it health and experience.</summary>
    [TestCase(0, ExpectedResult = "6")]
    [TestCase(20, ExpectedResult = "8")]
    [TestCase(200, ExpectedResult = "28")]
    public string APlayersBlockChanceMatchesTheOriginal(int stat) =>
        Answered($"Combat.PlayerChance({stat}, 50, Combat.PlayerBlockDivisor, Combat.PlayerBlockCap)");

    [TestCase(0, ExpectedResult = "0")]
    [TestCase(20, ExpectedResult = "1")]
    [TestCase(200, ExpectedResult = "10")]
    public string ACreaturesDodgeChanceMatchesTheOriginal(int stat) =>
        Answered($"Combat.NpcChance({stat}, Combat.NpcDodgeDivisor, Combat.NpcDodgeCap)");

    /// <summary>⚠ The caps bite. A stat that would run past one is held at it rather than climbing,
    /// which is the whole reason they are there.</summary>
    [Test]
    public void TheChanceCapsHold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Combat.PlayerChance(9000, 9000, Combat.PlayerBlockDivisor, Combat.PlayerBlockCap)"),
                Is.EqualTo("35"));
            Assert.That(Answered("Combat.PlayerChance(9000, 9000, Combat.PlayerDodgeDivisor, Combat.PlayerDodgeCap)"),
                Is.EqualTo("15"));
            Assert.That(Answered("Combat.NpcChance(9000, Combat.NpcBlockDivisor, Combat.NpcBlockCap)"),
                Is.EqualTo("25"));
        });
    }

    /// <summary>🔴 A kill pays experience, and a kill worth more than a level carries through every
    /// level it pays for.
    ///
    /// <para>⚠ A loop rather than an if. One that granted a single level would silently eat the rest
    /// of a big kill, which is the kind of thing nobody notices until somebody does the arithmetic on
    /// a boss.</para></summary>
    [Test]
    public void AKillWorthSeveralLevelsGrantsAllOfThem()
    {
        var (module, world) = Asking("        Levels.Earn(who, 3000);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        ((ITickWork)scripts).Tick(1);

        // 500 to leave level 1, 2000 to leave level 2, and 500 left over against level 3's 4500.
        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "level"), Is.EqualTo(3L));
            Assert.That(Held(world, "exp"), Is.EqualTo(500L), "the remainder is kept, not eaten");
            Assert.That(Held(world, "points"), Is.EqualTo(6L), "three a level, twice");

            // Level 3 with five defense, straight out of the original's own curve.
            Assert.That(Held(world, "maxhp"), Is.EqualTo(36L), "and the pool grew with the level");
        });
    }

    /// <summary>Points go where they are asked, all at once.</summary>
    [Test]
    public void AnAllocationIsAppliedWhereItWasAsked()
    {
        var (module, world) = Asking("""
                    Levels.Earn(who, 600);
                    Levels.Train(who, 2, 1, 0, 0);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "str"), Is.EqualTo(7L), "five to start, and two spent");
            Assert.That(Held(world, "def"), Is.EqualTo(6L), "and one here");
            Assert.That(Held(world, "spd"), Is.EqualTo(5L), "nothing asked, nothing given");
            Assert.That(Held(world, "points"), Is.Zero, "three earned, three spent");

            Assert.That(world.Said, Has.Some.Contains("stronger"), "one line per stat raised");
            Assert.That(world.Said, Has.Some.Contains("tougher"));
            Assert.That(world.Said, Has.None.Contains("quicker"), "and none for a stat left alone");
        });
    }

    /// <summary>🔴 An allocation bigger than the points behind it buys NOTHING.
    ///
    /// <para>The original committed the whole staged allocation in one message, so the only two
    /// outcomes are all of it or none of it. Spending what it can afford and dropping the rest is the
    /// tempting third answer, and it is the one that quietly puts points somewhere the player did not
    /// choose: they staged three into speed and one into strength, could afford three, and find out
    /// afterwards which of the four the engine decided to skip.</para></summary>
    [Test]
    public void AnAllocationBeyondThePointsBuysNothingAtAll()
    {
        var (module, world) = Asking("""
                    Levels.Earn(who, 600);
                    Levels.Train(who, 3, 3, 3, 3);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "str"), Is.EqualTo(5L), "not a point of it landed");
            Assert.That(Held(world, "points"), Is.EqualTo(3L), "and they still have all three");
            Assert.That(world.Said, Has.Some.Contains("that many points"), "and are told why");
        });
    }

    /// <summary>One number off the body the double is keeping.</summary>
    private static long Held(ScriptedWorldTests.RecordingWorld world, string key) =>
        world.Bag.TryGet(key, out AttributeValue value) ? value.AsLong() : -1L;

    /// <summary>A first arrival is enrolled; somebody who has been here before is left alone.
    ///
    /// <para>⚠ The guard is the whole of it. Without it every returning character is reset to level
    /// one on every join, which is the kind of bug that is only found by somebody losing a character
    /// they cared about.</para></summary>
    [Test]
    public void AFirstArrivalIsEnrolled_AndAReturningOneIsLeftAlone()
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        var observer = (IWorldObserver)scripts;
        var who = EntityHandle.ForPlayer(1);

        observer.OnPlayerJoined(who);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "level"), Is.EqualTo(1L));
            Assert.That(Held(world, "str"), Is.EqualTo(5L), "twenty points across four stats");
            Assert.That(Held(world, "points"), Is.EqualTo(0L));
            Assert.That(world.Said, Has.Count.EqualTo(1));
        });

        // Somebody who has levelled, joining again.
        world.SetAttribute(who, "level", AttributeValue.From(40L));
        observer.OnPlayerJoined(who);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "level"), Is.EqualTo(40L), "their own level survives");
            Assert.That(world.Said, Has.Count.EqualTo(1), "and they are not welcomed twice");
        });
    }

    // ── Creature against creature ─────────────────────────────────────────────

    /// <summary>🔴 <b>A fight the engine sets up and a game decides.</b> Core notices the pairing on its
    /// own — a body closes on anything within its range that is not its own kind and not in its group —
    /// and says so when it arrives. Everything after that is the game's, and this is the whole of it
    /// reaching the shipped scripts.
    ///
    /// <para>⚠ It arrives at its own handler rather than at the one a player target reaches. Routed into
    /// that one it would hand an Npc to a parameter that says Player, and a rule that runs on the wrong
    /// kind of body is worse than one that is not called.</para></summary>
    [Test]
    public void ACreatureReachingACreatureFightsIt()
    {
        var (module, world) = Asking("        who.Message(\"\");");
        using ScriptedWorldModule scripts = module;

        var wolf = EntityHandle.ForNpc(1, 1);
        var deer = EntityHandle.ForNpc(1, 2);
        world.Here.Add(wolf);
        world.Here.Add(deer);

        // The double keeps one bag, so both bodies read these, as two matched creatures would
        // anyway.
        //
        // ⚠ NO DEFENSE, deliberately. Any defense at all buys a dodge and a block roll, and a test that
        // is about the handler reaching the arithmetic would then fail a few runs in a hundred on a
        // roll it never meant to make.
        world.SetAttribute(deer, "str", AttributeValue.From(30L));
        world.SetAttribute(deer, "def", AttributeValue.From(0L));
        world.SetAttribute(deer, "hp", AttributeValue.From(400L));

        ((IWorldObserver)scripts).OnContact(wolf, deer);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "hp"), Is.LessThan(400L), "the swing landed on something");
            Assert.That(world.Chasing.ContainsKey(deer), Is.True,
                "and the bitten body turned on what bit it");
        });
    }

    /// <summary>A creature that only fights back — the original's second behavior, and the one Core's
    /// locomotion vocabulary has no word for.
    ///
    /// <para>It is written as a body authored to amble plus this: whoever lands a hit becomes what it is
    /// after. Without it that body is a punching bag, which is half the original's bestiary.</para></summary>
    [Test]
    public void AWoundedCreatureChasesWhoeverHitIt()
    {
        var (module, world) = Asking("""
                    Npc? target = World.NpcAt(1, 4, 4);

                    if target.HasValue()
                        Fight.Strike(who, 1, 4, 4);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var beast = EntityHandle.ForNpc(1, 1);
        world.Here.Add(who);
        world.Here.Add(beast);
        world.Standing[new WorldPlace(1, 4, 4)] = beast;

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        world.SetAttribute(beast, "hp", AttributeValue.From(9000L));
        world.SetAttribute(beast, "def", AttributeValue.From(0L));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Chasing.TryGetValue(beast, out var after) ? after : EntityHandle.None,
            Is.EqualTo(who), "a hit it survived points it at the body that landed it");
    }

    /// <summary>⚠ A kill pays nobody when nobody was there to earn it. The original credited a kill to
    /// whoever landed the blow, and a creature has no sheet to credit — so a beast farming another
    /// species on an empty map is an event no ledger records.</summary>
    [Test]
    public void ACreatureKilledByACreatureEarnsNobodyAnything()
    {
        var (module, world) = Asking("        who.Message(\"\");");
        using ScriptedWorldModule scripts = module;

        var wolf = EntityHandle.ForNpc(1, 1);
        var deer = EntityHandle.ForNpc(1, 2);
        world.Here.Add(wolf);
        world.Here.Add(deer);

        world.SetAttribute(deer, "str", AttributeValue.From(200L));
        world.SetAttribute(deer, "def", AttributeValue.From(0L));
        world.SetAttribute(deer, "hp", AttributeValue.From(1L));

        ((IWorldObserver)scripts).OnContact(wolf, deer);

        Assert.Multiple(() =>
        {
            Assert.That(world.Killed, Is.Not.Empty, "the body went down");
            Assert.That(Held(world, "exp"), Is.EqualTo(-1L), "and no sheet anywhere was paid");
        });
    }

    // ── Classes ───────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>A class is AUTHORED, and the model is the whole declaration.</b> How many classes
    /// there are, what they are called, and what each opens with are filled in through the editor, in a
    /// section it built from a model it was never compiled against.
    ///
    /// <para>⚠ Loading clean does not prove this. A <c>Describe</c> that faults leaves a world that
    /// compiles, reports no problem, and has no records in it — so what is asserted here is the
    /// declaration itself rather than the absence of an error.</para></summary>
    [Test]
    public void TheClassesAreDeclaredAsRecordsTheEditorCanAuthor()
    {
        _ = Asking("        who.Message(\"\");");

        var family = _declared!.Schema.Families.SingleOrDefault(f => f.Id == "Class");

        Assert.That(family, Is.Not.Null, "the editor is handed a Classes section");
        Assert.Multiple(() =>
        {
            Assert.That(family!.Fields.Select(f => f.Key),
                Is.EquivalentTo(new[] { "name", "description", "str", "def", "spd", "int" }));
            Assert.That(family.Fields.Single(f => f.Key == "str").Max, Is.EqualTo(100d),
                "a stat outside this is a character the arithmetic was never meant to hold");
        });
    }

    /// <summary>The kit points AT a class, so an author picks the Knight by name rather than
    /// remembering that the Knight is number four.</summary>
    [Test]
    public void AKitLinePointsAtTheClassItOutfits()
    {
        _ = Asking("        who.Message(\"\");");

        var kit = _declared!.Schema.Families.SingleOrDefault(f => f.Id == "ClassKit");

        Assert.That(kit, Is.Not.Null);

        var pointer = kit!.Fields.Single(f => f.Key == "forClass");

        Assert.Multiple(() =>
        {
            Assert.That(pointer.Kind, Is.EqualTo(FieldKind.RecordRef));
            Assert.That(pointer.RecordFamilyId, Is.EqualTo("Class"));
        });
    }

    /// <summary>🔴 <b>A class counts TWICE, and the second time is the one that matters.</b> Its spread
    /// becomes the character's four stats at enlistment, and it is ALSO added to every pool ceiling for
    /// as long as they play — so two characters at one level with identical stats still have different
    /// pools if they enlisted differently.
    ///
    /// <para>⚠ A port that applied only the first half reads correctly on the character sheet and is
    /// wrong on every bar, which is the hardest kind of wrong to see.</para></summary>
    [Test]
    public void EnlistingSetsTheSpreadAndRaisesThePoolsTwice()
    {
        var (module, world) = Asking("        Classes.Take(who, 1);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        world.Records["Class"] =
        [
            Row(("name", "Knight"), ("description", "Fights for honor."),
                ("str", 8L), ("def", 8L), ("spd", 2L), ("int", 2L)),
        ];

        ((IWorldObserver)scripts).OnPlayerJoined(who);

        // Level 1 with an even five across four stats, and no class on them yet.
        long plainHealth = Held(world, "maxhp");

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "class"), Is.EqualTo(1L));
            Assert.That(Held(world, "str"), Is.EqualTo(8L), "the spread replaces the even opening");
            Assert.That(Held(world, "def"), Is.EqualTo(8L));

            // def 5 → 8 alone would raise it; the class's own 8 is added on top of that.
            Assert.That(Held(world, "maxhp"),
                Is.EqualTo(Quadratic(level: 1, stat: 8, fromClass: 8)));
            Assert.That(Held(world, "maxhp"), Is.GreaterThan(plainHealth));

            Assert.That(Held(world, "hp"), Is.EqualTo(Held(world, "maxhp")),
                "and they start the game rested rather than having to go and sleep");
        });
    }

    /// <summary>The kit arrives in the bag. Every line naming this class, and no line naming another.
    ///
    /// <para>⚠ Carried rather than worn. Equipping is the engine's and nothing reaches it, so the
    /// original's already-worn opening is the only part of a kit the port cannot carry.</para></summary>
    [Test]
    public void AKitGivesOnlyTheLinesThatNameThisClass()
    {
        var (module, world) = Asking("        Classes.Take(who, 2);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        world.Records["Class"] =
        [
            Row(("name", "Knight")),
            Row(("name", "Mage")),
        ];
        world.Records["ClassKit"] =
        [
            Row(("forClass", 1L), ("item", 14L), ("many", 1L)),
            Row(("forClass", 2L), ("item", 31L), ("many", 1L)),
            Row(("forClass", 2L), ("item", 2L), ("many", 5L)),
            Row(("forClass", 2L), ("item", 0L), ("many", 3L)),
        ];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Given, Is.EqualTo(new[] { (31, 1), (2, 5) }),
            "the Knight's line is not theirs, and a line naming no item is not a grant");
    }

    // ── Spells ────────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>A spell is a swing delivered at range.</b> Mind and strength are the same offense
    /// stat running the same curve, so a caster and a warrior of equal investment deal identical damage —
    /// range is paid for in mana and in the wait after a cast, never in the numbers.
    ///
    /// <para>⚠ A port that gave magic its own damage curve would look fine in a fight and be wrong every
    /// time anybody compared two builds.</para></summary>
    [TestCase(0, 0, ExpectedResult = "9")]
    [TestCase(5, 10, ExpectedResult = "20")]
    [TestCase(20, 20, ExpectedResult = "45")]
    [TestCase(60, 50, ExpectedResult = "127")]
    [TestCase(200, 250, ExpectedResult = "548")]
    public string ASpellsRawPowerMatchesTheOriginal(int intellect, int amount) =>
        Answered($"Spells.Power({intellect}, {amount})");

    /// <summary>The authored magnitude contributes through a diminishing-returns curve that asymptotes at
    /// the caster's own intelligence, so a large number typed into the editor cannot blow past what
    /// mitigation can answer.</summary>
    [TestCase(0, 10, ExpectedResult = "0")]
    [TestCase(10, 10, ExpectedResult = "10")]
    [TestCase(50, 20, ExpectedResult = "29")]
    [TestCase(250, 200, ExpectedResult = "222")]
    public string TheMagnitudeContributionMatchesTheOriginal(int rating, int stat) =>
        Answered($"Spells.Contribution({rating}, {stat})");

    /// <summary>A class's head start moves the THRESHOLD to learn a spell and never the damage, so two
    /// casters with equal intelligence cast identically whatever they enlisted as.</summary>
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(2, ExpectedResult = "1")]
    [TestCase(8, ExpectedResult = "2")]
    [TestCase(10, ExpectedResult = "3")]
    [TestCase(40, ExpectedResult = "10")]
    public string TheClassHeadStartMatchesTheOriginal(int classInt) => Answered($"Spells.Affinity({classInt})");

    /// <summary>🔴 <b>The authored magnitude does three jobs</b> — how much the spell does, the
    /// intelligence to learn it, and the mana it costs. One number, so nobody can author a spell that is
    /// powerful, cheap, and free to pick up.
    ///
    /// <para>⚠ Mana is the one type whose output is its own input, so it prices off what it HANDS OVER
    /// rather than off the authored amount. Priced the other way any sufficiently clever caster nets
    /// positive, and no constant outruns a curve.</para></summary>
    [TestCase("SubMp", 1, ExpectedResult = "11")]
    [TestCase("SubMp", 30, ExpectedResult = "50")]
    [TestCase("SubMp", 250, ExpectedResult = "719")]
    [TestCase("AddHp", 100, ExpectedResult = "206")]
    [TestCase("AddSp", 100, ExpectedResult = "226")]
    [TestCase("AddSp", 250, ExpectedResult = "791")]
    public string ASpellsManaCostMatchesTheOriginal(string type, int amount) =>
        CostOf(type, amount, casterInt: 0, maxMana: 0);

    [TestCase(10, 5, ExpectedResult = "26")]
    [TestCase(50, 60, ExpectedResult = "166")]
    [TestCase(100, 200, ExpectedResult = "597")]
    public string RestoringManaIsPricedOffWhatItHandsOver(int amount, int casterInt) =>
        CostOf("AddMp", amount, casterInt, maxMana: 0);

    /// <summary>⚠ Damage does not pay the utility price at all — it is the caster's sustainable weapon,
    /// so it costs a flat share of the pool. Mana is a distant ceiling on a marathon rather than a gate
    /// on every cast.</summary>
    [TestCase(100, ExpectedResult = "4")]
    [TestCase(1000, ExpectedResult = "40")]
    [TestCase(9860, ExpectedResult = "394")]
    public string DamageIsPricedOffThePoolRatherThanTheSpell(int maxMana) =>
        CostOf("SubHp", amount: 40, casterInt: 0, maxMana);

    /// <summary>The gate is floored at one rather than at zero. A real spell always carries a magnitude,
    /// so unlike free gear it keeps a token requirement even for the class it was written for.</summary>
    [TestCase(30, 0, ExpectedResult = "30")]
    [TestCase(30, 8, ExpectedResult = "28")]
    [TestCase(30, 200, ExpectedResult = "1")]
    [TestCase(1, 0, ExpectedResult = "1")]
    public string TheIntelligenceGateMatchesTheOriginal(int amount, int classInt)
    {
        var (module, world) = Asking(
            $"        who.Message(\"\" + Spells.IntNeeded(1, {classInt}));");

        using ScriptedWorldModule scripts = module;

        world.Records["Spell"] = [Row(("name", "Bolt"), ("type", "SubHp"), ("amount", (long)amount))];
        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    /// <summary>🔴 <b>No row for a spell means every class may learn it.</b> That is the common case, so
    /// it is the one that costs nothing to author — a world where the gate were the default would need a
    /// row per class per spell before anybody could cast anything.</summary>
    [Test]
    public void AnUngatedSpellIsOpenToEverybody()
    {
        var (module, world) = Asking("""
                    who.Message("" + Spells.OpenTo(1, 2));
                    who.Message("" + Spells.OpenTo(2, 2));
                    who.Message("" + Spells.OpenTo(2, 3));
            """);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));
        world.Records["Spell"] =
        [
            Row(("name", "Spark"), ("type", "SubHp"), ("amount", 10L)),
            Row(("name", "Ward"), ("type", "AddHp"), ("amount", 10L),
                ("mayLearn", AttributeValue.From(new long[] { 2 }))),
        ];

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said, Is.EqualTo(new[] { "true", "true", "false" }),
            "the ungated spell is open, and the gated one only to the class its row names");
    }

    /// <summary>A book fills in order, refuses what it already holds, and names the reason.</summary>
    /// <summary>🔴 <b>What a swing costs a weapon, on average, over a whole hundred-to-nothing cycle.</b>
    /// A caster's reagent bill is matched against a warrior's repair bill, and this ratio is the bridge
    /// between them — so it is READ OFF the wear bands rather than written down beside them.
    ///
    /// <para>Four bands twenty-five points wide chipping at a quarter, a half, three quarters, and
    /// certainty. Crossing them takes 100 + 50 + 33⅓ + 25 hits, so a hundred points of durability go in
    /// 208⅓ hits, which is 0.48 a hit.</para>
    ///
    /// <para>⚠ Move a band and this moves. A figure copied here would go stale the moment one did, and it would fail silently — nothing throws, casters simply stop paying their
    /// share.</para></summary>
    [Test]
    public void AverageWearPerHit_IsReadOffTheWearBands() =>
        Assert.That(Answered("Math.Round(Gear.AverageChip() * 100)"), Is.EqualTo("48"));

    /// <summary>🔴 <b>A cast takes a whole number of reagents or none.</b> It mirrors the swing it
    /// is priced off: a swing removes one point of durability or none, never a point and sometimes
    /// two. So the bill rounds UP to what a charge would be, and how often a charge happens carries the
    /// fraction.</summary>
    [TestCase("0.0", ExpectedResult = "0")]
    [TestCase("0.02", ExpectedResult = "1")]
    [TestCase("0.48", ExpectedResult = "1")]
    [TestCase("1.0", ExpectedResult = "1")]
    [TestCase("1.2", ExpectedResult = "2")]
    [TestCase("9.6", ExpectedResult = "10")]
    public string OneCastTakesAWholeNumberOfReagents(string exact) =>
        Answered($"Spells.PerCast({exact})");

    /// <summary>🔴 <b>The bill is the engine's repair rate, not a number of this game's own.</b> A
    /// warrior at this tier burns that much gold a durability point; a caster burns the same, at a
    /// reagent to the gold, times what a swing costs a weapon.</summary>
    [Test]
    public void TheReagentBillIsPricedOffTheEnginesRepairRate()
    {
        var (module, world) = Asking("""
                    who.Message("" + Math.Round(Spells.BaseReagentCost(20) * 100));
                    who.Message("" + Spells.PerCast(Spells.BaseReagentCost(20)));
            """);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        // Ten gold a durability point at tier twenty.
        world.RepairRatePerTier = 0.5;

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said, Is.EqualTo(new[] { "480", "5" }),
            "ten gold a point at 0.48 points a swing is 4.8 reagents a cast, so a cast that charges charges five");
    }

    /// <summary>🔴 <b>A bill that is already whole is charged every time.</b> The roll carries the
    /// fraction and nothing else, so odds of one are certainty rather than nearly certainty — otherwise
    /// the mirror leaks in the caster's favor at every tier where the arithmetic happens to come out
    /// even.</summary>
    [Test]
    public void AWholeBillIsChargedEveryTime()
    {
        var (module, world) = Asking("""
                    loop for _ = 1 to 40
                        who.Message("" + Spells.RollReagents(2.0));
                    end loop
            """);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said, Is.All.EqualTo("2"), "a cost of exactly two is two, every time");
    }

    /// <summary>⚠ And a fractional one charges the WHOLE amount or nothing — never the fraction, which
    /// is not a quantity of items anybody can hold.</summary>
    [Test]
    public void AFractionalBillChargesTheWholeAmountOrNothing()
    {
        var (module, world) = Asking("""
                    loop for _ = 1 to 200
                        who.Message("" + Spells.RollReagents(1.5));
                    end loop
            """);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Is.All.AnyOf("0", "2"));
            Assert.That(world.Said, Has.Some.EqualTo("2"), "three quarters of the time it charges two");
            Assert.That(world.Said, Has.Some.EqualTo("0"), "and the rest of the time it charges nothing");
        });
    }

    [Test]
    public void LearningWritesTheSpellIntoTheFirstFreePage()
    {
        var (module, world) = Asking("""
                    Book.Learn(who, 1);
                    Book.Learn(who, 1);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Spell"] = [Row(("name", "Spark"), ("type", "SubHp"), ("amount", 1L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "book1"), Is.EqualTo(1L));
            Assert.That(Held(world, "prepared"), Is.EqualTo(1L),
                "anybody learning their first spell wants it ready");
            Assert.That(world.Said, Has.Some.Contains("already know"));
        });
    }

    /// <summary>⚠ The refusals come in the order they are worth reporting. A character under the level
    /// gate is told about the level, not about their intelligence — both may be short, and the first one
    /// they can do something about is the one to name.</summary>
    [Test]
    public void TheLevelGateIsNamedBeforeTheIntelligenceGate()
    {
        var (module, world) = Asking("        Book.Learn(who, 1);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Spell"] =
            [Row(("name", "Nova"), ("type", "SubHp"), ("amount", 200L), ("levelReq", 40L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("level 40"));
            Assert.That(world.Said, Has.None.Contains("intelligence"));
            Assert.That(Held(world, "book1"), Is.EqualTo(-1L), "and nothing was written");
        });
    }

    /// <summary>🔴 <b>Restoring mana has to cost more than it returns, at every level of investment.</b>
    /// It is the one spell type whose output is its own input, so a cast that nets positive is a loop
    /// rather than a good trade.
    ///
    /// <para>⚠ Priced off the authored magnitude alone it netted positive for almost everybody: the
    /// restore carries a term growing without bound in the caster's own intelligence, and an
    /// amount-only cost is a constant. No constant outruns a curve, so a bigger multiplier only moves
    /// the leak — the cost has to price off what the spell HANDS OVER. This is that property, asked
    /// across the whole range of casters rather than at one comfortable point.</para></summary>
    [TestCase(10, 5)]
    [TestCase(10, 200)]
    [TestCase(50, 60)]
    [TestCase(250, 255)]
    public void RestoringManaNeverPaysForItself(int amount, int casterInt)
    {
        var (module, world) = Asking($"""
                    who.Message("" + Spells.Cost(1, {casterInt}, 0));
                    who.Message("" + Spells.Power({casterInt}, {amount}));
            """);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));
        world.Records["Spell"] = [Row(("name", "Draw"), ("type", "AddMp"), ("amount", (long)amount))];

        ((ITickWork)scripts).Tick(1);

        long cost = long.Parse(world.Said[^2]);
        long returned = long.Parse(world.Said[^1]);

        Assert.That(cost, Is.GreaterThan(returned),
            "a cast that nets positive is a loop, whatever the caster's intelligence");
    }

    /// <summary>And the write order behind it: the cost comes out before the restore goes in.</summary>
    [Test]
    public void ACastSpendsBeforeItRestores()
    {
        var (module, world) = Asking("""
                    Book.Learn(who, 1);
                    Book.Inward(who);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Spell"] = [Row(("name", "Mend"), ("type", "AddHp"), ("amount", 5L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);

        long ceiling = Held(world, "maxmp");
        world.SetAttribute(who, "hp", AttributeValue.From(1L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "mp"), Is.LessThan(ceiling), "the mana was spent");
            Assert.That(Held(world, "hp"), Is.GreaterThan(1L), "and the health came back");
        });
    }

    /// <summary>⚠ A refusal pays nothing, because nothing was attempted. That is only true of refusals:
    /// a cast that finds no target is a whiff and pays the full wait, exactly as a swing into air
    /// does.</summary>
    [Test]
    public void ACastNobodyCanAffordCostsNothing()
    {
        var (module, world) = Asking("""
                    Book.Learn(who, 1);
                    who.SetNumber(Vitals.Mana, 0);
                    Book.Inward(who);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Spell"] = [Row(("name", "Mend"), ("type", "AddHp"), ("amount", 5L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("do not have the mana"));
            Assert.That(world.Timed.Where(t => t.State == "cooldown"), Is.Empty, "and no cooldown started");
        });
    }

    /// <summary>A spell aimed at a creature, end to end: the bolt, the damage, and the creature turning
    /// on whoever cast it.</summary>
    [Test]
    public void ASpellLandsOnACreatureAndItAnswers()
    {
        var (module, world) = Asking("""
                    Book.Learn(who, 1);
                    Book.Hurl(who, 1, 5, 6);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var beast = EntityHandle.ForNpc(1, 3);
        world.Here.Add(who);
        world.Here.Add(beast);
        world.Standing[new WorldPlace(1, 5, 5)] = who;
        world.Standing[new WorldPlace(1, 5, 6)] = beast;
        world.Records["Spell"] = [Row(("name", "Spark"), ("type", "SubHp"), ("amount", 5L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        world.SetAttribute(beast, "hp", AttributeValue.From(9000L));
        world.SetAttribute(beast, "def", AttributeValue.From(0L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Shown.Where(s => s.What.StartsWith("throw")), Is.Not.Empty,
                "something crossed the gap");
            Assert.That(Held(world, "hp"), Is.LessThan(9000L));
            Assert.That(world.Chasing.TryGetValue(beast, out var after) ? after : EntityHandle.None,
                Is.EqualTo(who), "a spell is a hit, so it answers like one");
        });
    }

    /// <summary>⚠ A heal aimed at a creature is a heal aimed at the wrong body, and a drain aimed at
    /// yourself is never what anybody meant. Both are refused rather than cast.</summary>
    [Test]
    public void ASpellAimedTheWrongWayIsRefused()
    {
        var (module, world) = Asking("""
                    Book.Learn(who, 1);
                    Book.Hurl(who, 1, 5, 6);
                    Book.Inward(who);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var beast = EntityHandle.ForNpc(1, 3);
        world.Here.Add(who);
        world.Here.Add(beast);
        world.Standing[new WorldPlace(1, 5, 5)] = who;
        world.Standing[new WorldPlace(1, 5, 6)] = beast;
        world.Records["Spell"] = [Row(("name", "Mend"), ("type", "AddHp"), ("amount", 5L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("kindness wasted"), "a heal is not thrown at a beast");
            Assert.That(world.Shown.Where(s => s.What.StartsWith("throw")), Is.Empty);
            Assert.That(Held(world, "hp"), Is.EqualTo(Held(world, "maxhp")), "and the heal landed inward");
        });
    }

    private string CostOf(string type, int amount, int casterInt, int maxMana)
    {
        var (module, world) = Asking(
            $"        who.Message(\"\" + Spells.Cost(1, {casterInt}, {maxMana}));");

        using ScriptedWorldModule scripts = module;

        world.Records["Spell"] =
            [Row(("name", "Test"), ("type", type), ("amount", (long)amount), ("intReq", (long)amount))];

        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    // ── A game's own fields, on the engine's own records ──────────────────────

    /// <summary>🔴 <b>An item's own properties are a closed set, because Core cannot act on one it has
    /// never heard of.</b> A game's are open, and they belong on the same record rather than in a table
    /// beside it: a level requirement is a fact about the sword.
    ///
    /// <para>⚠ What is asserted is that the fields reach the ENGINE'S <c>Items</c> family, not that a
    /// family called <c>ItemRules</c> exists — an extension is the same family with more rows on its
    /// form, and a second family would be the fact stored where it can be forgotten.</para></summary>
    [Test]
    public void AGameAddsItsOwnFieldsToTheEnginesItems()
    {
        _ = Asking("        who.Message(\"\");");

        var items = _declared!.Schema.Families.Single(f => f.Id == "Items");

        Assert.Multiple(() =>
        {
            Assert.That(items.Fields.Select(f => f.Key),
                Is.EquivalentTo(new[]
                {
                    "levelReq", "teaches", "valor", "coin", "reagent",
                    "restoresHealth", "restoresMana", "restoresStamina",
                }));
            Assert.That(_declared.Schema.Families.Any(f => f.Id == "ItemRules"), Is.False,
                "the fields joined Items rather than becoming a family of their own");

            // And one of them points at this game's own records, so the form draws a picker.
            var teaches = items.Fields.Single(f => f.Key == "teaches");
            Assert.That(teaches.Kind, Is.EqualTo(FieldKind.RecordRef));
            Assert.That(teaches.RecordFamilyId, Is.EqualTo("Spell"));
        });
    }

    /// <summary>🔴 <b>A field may point at the ENGINE'S own records.</b> Items and creatures have no
    /// model a script could type a field as, and authoring a kit line by typing 214 when the answer is
    /// "Iron Sword" is the mistake a picker exists to stop.</summary>
    [Test]
    public void AFieldPointsAtTheEnginesOwnRecords()
    {
        _ = Asking("        who.Message(\"\");");

        var kit = _declared!.Schema.Families.Single(f => f.Id == "ClassKit").Fields.Single(f => f.Key == "item");

        Assert.Multiple(() =>
        {
            Assert.That(kit.Kind, Is.EqualTo(FieldKind.RecordRef));
            Assert.That(kit.RecordFamilyId, Is.EqualTo("Items"));
        });
    }

    /// <summary>And a rule reads those fields back off the item at run time, which is the half that
    /// makes authoring them worth anything.</summary>
    [Test]
    public void AScrollTeachesTheSpellWrittenOnIt()
    {
        var (module, world) = Asking("        Book.Read(who, 8);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        world.Records["Spell"] = [Row(("name", "Spark"), ("type", "SubHp"), ("amount", 1L))];

        // Item 8 is a scroll: a field this game added to the engine's own Items says which spell.
        world.Records["Items"] = [Row(), Row(), Row(), Row(), Row(), Row(), Row(), Row(("teaches", 1L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "book1"), Is.EqualTo(1L), "the spell went into the book");
            Assert.That(world.Taken, Is.EqualTo(new[] { (8, 1) }), "and the scroll was spent");
        });
    }

    /// <summary>⚠ A scroll that teaches nothing is not a scroll. Using an ordinary item has to reach the
    /// handler and do nothing, or every bite of bread reads as a failed cast.</summary>
    [Test]
    public void AnOrdinaryItemTeachesNothingAndIsNotSpent()
    {
        var (module, world) = Asking("        Book.Read(who, 3);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Items"] = [Row(), Row(), Row(("levelReq", 4L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Taken, Is.Empty);
            Assert.That(world.Said, Has.None.Contains("learn"));
        });
    }

    /// <summary>The gate on gear, which is a FAMILY rather than a field because the original allowed a
    /// list of classes per item — and no row for a piece means everybody may wield it.</summary>
    [Test]
    public void GearIsGatedByClassAndByLevel()
    {
        var (module, world) = Asking("""
                    who.Message("" + Gear.MayWield(who, 1));
                    who.Message("" + Gear.MayWield(who, 2));
                    who.Message("" + Gear.MayWield(who, 3));
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        world.Records["Items"] =
        [
            Row(),
            Row(("mayWield", AttributeValue.From(new long[] { 7 }))),
            Row(("levelReq", 40L)),
        ];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^3..], Is.EqualTo(new[] { "true", "false", "false" }),
            "ungated, gated to another class, and gated by a level nobody has yet");
    }

    /// <summary>🔴 <b>A refusal means the piece never went on.</b> Asked before the engine acts, so a
    /// rule about who may hold what is a rule rather than a correction.
    ///
    /// <para>⚠ The blank answer is the one that matters most: a policy that refused everything it was
    /// not asked about would stop every potion, key, and scroll in the world.</para></summary>
    [Test]
    public void GearThisCharacterMayNotHoldIsRefusedBeforeItGoesOn()
    {
        var (module, world) = Asking("""
                    who.Message("[" + Rules.OnMayUse(who, 1, 1) + "]");
                    who.Message("[" + Rules.OnMayUse(who, 2, 1) + "]");
                    who.Message("[" + Rules.OnMayUse(who, 3, 1) + "]");
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        world.Records["Items"] =
        [
            Row(),
            Row(("mayWield", AttributeValue.From(new long[] { 7 }))),
            Row(("levelReq", 40L)),
        ];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said[^3], Is.EqualTo("[]"),
                "anybody may use what nobody gated");
            Assert.That(world.Said[^2], Does.Contain("never trained"));
            Assert.That(world.Said[^1], Does.Contain("level 40"));
        });
    }

    /// <summary>🔴 <b>A class is picked before the character exists, as the original picked it.</b> What
    /// somebody IS has to be settled before there is somebody: a spread applied afterwards is a
    /// correction rather than a beginning.
    ///
    /// <para>⚠ What is asserted is the DECLARATION — that the engine is asked to put this question on
    /// the creation screen, over this game's own classes. The answer arriving is the engine's half, and
    /// a script that declared nothing would be handed no answer to read.</para></summary>
    [Test]
    public void TheClassIsAskedForBeforeACharacterExists()
    {
        _ = Asking("        who.Message(\"\");");

        var asked = _declared!.CreationChoices.Choices;

        Assert.That(asked, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(asked[0].Key, Is.EqualTo("class"));
            Assert.That(asked[0].FamilyId, Is.EqualTo("Class"), "listed from this game's own classes");
            Assert.That(_declared.Attributes.TryGet("class", out _), Is.True,
                "and the key is synced, or the answer would be written and invisible");
        });
    }

    /// <summary>And enrolling applies what they already picked, rather than asking again.</summary>
    [Test]
    public void EnrollingAppliesTheClassTheyChoseAtCreation()
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Class"] =
        [
            Row(("name", "Knight"), ("str", 8L), ("def", 8L), ("spd", 2L), ("int", 2L)),
        ];

        // What the creation screen wrote onto the new character, before anything was told they joined.
        world.SetAttribute(who, "class", AttributeValue.From(1L));

        ((IWorldObserver)scripts).OnPlayerJoined(who);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "str"), Is.EqualTo(8L), "the spread, not the even opening five");
            Assert.That(Held(world, "maxhp"), Is.EqualTo(Quadratic(level: 1, stat: 8, fromClass: 8)),
                "and the class counts twice, once in the stat and once on top");
        });
    }

    // ── Quests ────────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>The NPC roles live on the QUEST, not on the creature.</b> A quest names who offers
    /// it and who takes it back, so adding one is a row and touches nothing else — and a creature can
    /// give as many quests as an author likes without ever being edited.</summary>
    [Test]
    public void AQuestNamesItsOwnGiverAndTaker()
    {
        _ = Asking("        who.Message(\"\");");

        var quests = _declared!.Schema.Families.Single(f => f.Id == "Quest");

        Assert.Multiple(() =>
        {
            Assert.That(quests.Fields.Single(f => f.Key == "giver").RecordFamilyId, Is.EqualTo("NPCs"));
            Assert.That(quests.Fields.Single(f => f.Key == "turnIn").RecordFamilyId, Is.EqualTo("NPCs"));
            Assert.That(quests.Fields.Single(f => f.Key == "prereq").RecordFamilyId, Is.EqualTo("Quest"),
                "a chain is a quest pointing at the one before it");
            Assert.That(quests.Fields.Single(f => f.Key == "rewardItem").RecordFamilyId, Is.EqualTo("Items"));
        });
    }

    /// <summary>Taking one, killing what it asks for, and handing it back — the whole loop, through the
    /// verbs a player actually picks.</summary>
    [Test]
    public void AQuestIsTakenCountedAndSettled()
    {
        var (module, world) = Asking("""
                    Quests.Ask(who, 1, 4, 4);
                    Quests.Accept(who, "1");
                    Quests.Killed(who, 7);
                    Quests.Killed(who, 7);
                    Quests.Give(who, 1, 4, 4);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var giver = EntityHandle.ForNpc(1, 2);
        world.Here.Add(who);
        world.Here.Add(giver);
        world.Standing[new WorldPlace(1, 4, 4)] = giver;
        world.Kinds[giver] = 3;

        world.Records["Quest"] =
        [
            Row(("name", "Wolves"), ("giver", 3L), ("rewardExp", 100L),
                ("rewardItem", 14L), ("rewardMany", 2L)),
        ];
        world.Records["QuestGoal"] = [Row(("forQuest", 1L), ("quarry", 7L), ("many", 2L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "quest1"), Is.EqualTo(2L), "taken, then finished");
            Assert.That(Held(world, "qcount1"), Is.EqualTo(2L), "both kills counted");
            Assert.That(world.Given, Is.EqualTo(new[] { (14, 2) }), "and the reward was paid");
            Assert.That(Held(world, "exp"), Is.EqualTo(100L));
        });
    }

    /// <summary>⚠ A kill toward a quest nobody has taken counts toward nothing. Otherwise a character
    /// arrives at a giver with the work already done, which is a quest that was never a quest.</summary>
    [Test]
    public void AKillBeforeTheQuestIsTakenCountsForNothing()
    {
        var (module, world) = Asking("""
                    Quests.Killed(who, 7);
                    Quests.Ask(who, 1, 4, 4);
                    Quests.Accept(who, "1");
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var giver = EntityHandle.ForNpc(1, 2);
        world.Here.Add(who);
        world.Here.Add(giver);
        world.Standing[new WorldPlace(1, 4, 4)] = giver;
        world.Kinds[giver] = 3;

        world.Records["Quest"] = [Row(("name", "Wolves"), ("giver", 3L))];
        world.Records["QuestGoal"] = [Row(("forQuest", 1L), ("quarry", 7L), ("many", 2L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(Held(world, "qcount1"), Is.EqualTo(0L), "taking it starts the count at nothing");
    }

    /// <summary>🔴 <b>A quest that is not finished is not settled</b>, and a giver with nothing to settle
    /// says so rather than paying out.</summary>
    [Test]
    public void AnUnfinishedQuestIsNotPaid()
    {
        var (module, world) = Asking("""
                    Quests.Ask(who, 1, 4, 4);
                    Quests.Accept(who, "1");
                    Quests.Killed(who, 7);
                    Quests.Give(who, 1, 4, 4);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var giver = EntityHandle.ForNpc(1, 2);
        world.Here.Add(who);
        world.Here.Add(giver);
        world.Standing[new WorldPlace(1, 4, 4)] = giver;
        world.Kinds[giver] = 3;

        world.Records["Quest"] = [Row(("name", "Wolves"), ("giver", 3L), ("rewardExp", 100L))];
        world.Records["QuestGoal"] = [Row(("forQuest", 1L), ("quarry", 7L), ("many", 2L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "quest1"), Is.EqualTo(1L), "still underway");
            Assert.That(Held(world, "exp"), Is.EqualTo(0L), "and nothing was paid");
            Assert.That(world.Said, Has.Some.Contains("nothing to settle"));
        });
    }

    /// <summary>A chain: the second is invisible until the first is finished.</summary>
    [Test]
    public void AQuestThatComesAfterAnotherWaitsForIt()
    {
        var (module, world) = Asking("""
                    Quests.Ask(who, 1, 4, 4);
                    Quests.Accept(who, "1");
            """);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var giver = EntityHandle.ForNpc(1, 2);
        world.Here.Add(who);
        world.Here.Add(giver);
        world.Standing[new WorldPlace(1, 4, 4)] = giver;
        world.Kinds[giver] = 3;

        world.Records["Quest"] =
        [
            Row(("name", "Wolves"), ("giver", 3L)),
            Row(("name", "The pack leader"), ("giver", 3L), ("prereq", 1L)),
        ];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "quest1"), Is.EqualTo(1L), "the first is offered");
            Assert.That(Held(world, "quest2"), Is.EqualTo(-1L), "the second is not, yet");
        });
    }

    /// <summary>The journal is a window over what is underway, rewritten whenever it changes — so a game
    /// with no journal code still draws one, off keys the panel reads live.</summary>
    [Test]
    public void TheJournalSaysHowFarAlongEachOneIs()
    {
        var (module, world) = Asking("""
                    Quests.Ask(who, 1, 4, 4);
                    Quests.Accept(who, "1");
                    Quests.Killed(who, 7);
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var giver = EntityHandle.ForNpc(1, 2);
        world.Here.Add(who);
        world.Here.Add(giver);
        world.Standing[new WorldPlace(1, 4, 4)] = giver;
        world.Kinds[giver] = 3;

        world.Records["Quest"] = [Row(("name", "Wolves"), ("giver", 3L))];
        world.Records["QuestGoal"] = [Row(("forQuest", 1L), ("quarry", 7L), ("many", 3L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(Text(world, "qlog1"), Is.EqualTo("Wolves - 1 of 3"));
    }

    // ── Guilds ────────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>A guild earns ONE point for a kill, whatever the kill was worth to the player.</b>
    /// The two curves have nothing to do with each other: a character's runs to a few thousand a level
    /// and a guild's to hundreds of millions, so paying a guild what its members earn would level it in
    /// an afternoon.</summary>
    [Test]
    public void AGuildEarnsOnePointForAKill()
    {
        var (module, world) = Asking("        Guilds.Earn(who, Guilds.ExpPerKill);");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 4;
        world.GuildNames[4] = "The Gathering";

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(Guild(world, 4, "gexp"), Is.EqualTo(1L));
    }

    /// <summary>⚠ <b>A guild's experience is CUMULATIVE and is never spent.</b> A level is a threshold
    /// the running total has passed, not a cost taken out of it — so level five is holding 256 million
    /// rather than having earned 341 million across five rungs.</summary>
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(999_999, ExpectedResult = "0")]
    [TestCase(1_000_000, ExpectedResult = "1")]
    [TestCase(3_999_999, ExpectedResult = "1")]
    [TestCase(4_000_000, ExpectedResult = "2")]
    [TestCase(16_000_000, ExpectedResult = "3")]
    [TestCase(64_000_000, ExpectedResult = "4")]
    [TestCase(256_000_000, ExpectedResult = "5")]
    [TestCase(999_000_000, ExpectedResult = "5")]
    public string AGuildsLevelIsAThresholdItsTotalHasPassed(int exp) =>
        Answered($"Guilds.LevelForExp({exp})");

    /// <summary>And the thresholds themselves, against the original's own table.</summary>
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(1, ExpectedResult = "1000000")]
    [TestCase(2, ExpectedResult = "4000000")]
    [TestCase(3, ExpectedResult = "16000000")]
    [TestCase(4, ExpectedResult = "64000000")]
    [TestCase(5, ExpectedResult = "256000000")]
    public string TheGuildLevelTableMatchesTheOriginal(int level) =>
        Answered($"Guilds.ExpForLevel({level})");

    /// <summary>🔴 <b>The GAP decides the price, not either level on its own.</b> Declaring upward
    /// subtracts and declaring downward adds, so a strong guild picking on a weak one pays the most.
    ///
    /// <para>⚠ And declaring on a guild that has earned NOTHING doubles the whole thing — such a war can
    /// never be answered, so its cost is paid indefinitely for a payout that never comes. That is
    /// what protects a guild nobody has built yet, and it is applied BEFORE the floor: a floor doubled
    /// afterwards is not the floor.</para>
    ///
    /// <para>The second case is the original's own worked example — a level-5 guild declaring on a
    /// level-1 one pays 49,000.</para></summary>
    [TestCase(3, 3, ExpectedResult = "35000")]
    [TestCase(5, 1, ExpectedResult = "49000")]
    [TestCase(1, 5, ExpectedResult = "21000")]
    [TestCase(5, 0, ExpectedResult = "105000")]
    [TestCase(0, 0, ExpectedResult = "70000")]
    public string DeclaringIsPricedOffTheGap(int ours, int theirs)
    {
        var (module, world) = Asking("        who.Message(\"\" + Guilds.DeclareCost(1, 2));");
        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));
        world.SetGuildValue(1, "glevel", AttributeValue.From((long)ours));
        world.SetGuildValue(2, "glevel", AttributeValue.From((long)theirs));

        ((ITickWork)scripts).Tick(1);

        return world.Said[^1];
    }

    /// <summary>🔴 <b>A war the other side answered is live at once; a grievance nobody answered waits
    /// out the warmup.</b> So a declaration is never an ambush — and the two cases are the same
    /// call, told apart by whether the opponent had already declared.</summary>
    [Test]
    public void AnAnsweredWarIsLiveAtOnceAndAGrievanceWaits()
    {
        var (module, world) = Asking("""
                    Guilds.War(who, "Theirs");
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 200_000L;
        world.SetGuildValue(1, "glevel", AttributeValue.From(3L));
        world.SetGuildValue(2, "glevel", AttributeValue.From(3L));

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1op"), Is.EqualTo(2L), "the opponent is named");
            Assert.That(Guild(world, 1, "gw1we"), Is.EqualTo(1L), "by us");
            Assert.That(Guild(world, 1, "gw1live") - world.Clock, Is.EqualTo(600L),
                "and nobody may be hit for ten minutes");
            Assert.That(Guild(world, 2, "gw1op"), Is.EqualTo(1L),
                "the other side carries the same war, from its own side");
            Assert.That(Guild(world, 2, "gw1they"), Is.EqualTo(1L), "as one that was declared upon");
            Assert.That(world.Spent, Is.EqualTo(new[] { (1, 35_000L) }), "the vault paid for it");
        });
    }

    /// <summary>An officer cannot declare. Asking queues the request for the leader, who accepts it —
    /// which runs the declaration as the leader, re-checking every gate — or denies it.</summary>
    [Test]
    public void AnOfficersDeclarationIsQueuedForTheLeader()
    {
        var (module, world) = Asking("        Guilds.War(who, \"Theirs\");");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "officer";

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("gone to your guild leader"));
            Assert.That(Guild(world, 1, "gw1op"), Is.Zero, "and nothing was declared yet");
            Assert.That(Guild(world, 1, "gr1kind"), Is.EqualTo(1L), "the request is waiting");
            Assert.That(Guild(world, 1, "gr1at"), Is.EqualTo(2L), "against them");
        });
    }

    /// <summary>🔴 <b>War legalizes player against player, and it is one-sided on purpose.</b> A
    /// guild that declared may be struck back before its OWN war goes live, because being declared upon
    /// is not something the other side agreed to.</summary>
    [Test]
    public void BeingDeclaredUponIsAnsweredWithoutWaiting()
    {
        var (module, world) = Asking("""
                    Player? them = who.Find("Rival");

                    # ⚠ Only the one asking. The tick walks every body in the world, and the rival
                    # asking about themselves is a different question with a different answer.
                    if World.GuildOf(who) == 1
                        who.Message("" + Guilds.AtWar(who, them.Value()));
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;

        // They declared, and their warmup has run out. We declared nothing at all.
        // They declared on us and the warmup has run out. Our own side of the war says so too, which
        // is how both guilds' entries are kept.
        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1they", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1live", AttributeValue.From(world.Clock - 1));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^1], Is.EqualTo("true"));
    }

    /// <summary>And a warmup that has not run out means nobody may be hit yet, from either side.</summary>
    [Test]
    public void NobodyIsFairGameDuringTheWarmup()
    {
        var (module, world) = Asking("""
                    Player? them = who.Find("Rival");

                    # ⚠ Only the one asking. The tick walks every body in the world, and the rival
                    # asking about themselves is a different question with a different answer.
                    if World.GuildOf(who) == 1
                        who.Message("" + Guilds.AtWar(who, them.Value()));
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;

        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1we", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1live", AttributeValue.From(world.Clock + 120));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^1], Is.EqualTo("false"));
    }

    /// <summary>⚠ A declaration cannot be taken back for fifteen minutes, so declaring is a decision
    /// rather than a feint.</summary>
    [Test]
    public void ADeclarationCannotBeTakenBackAtOnce()
    {
        var (module, world) = Asking("        Guilds.Peace(who, \"Theirs\");");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1we", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1at", AttributeValue.From(world.Clock));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("too soon"));
            Assert.That(Guild(world, 1, "gw1op"), Is.EqualTo(2L), "and the war stands");
        });
    }

    /// <summary>Five declarations out, and the sixth is refused. Incoming declarations do not count
    /// toward the five — only the ones this guild made.</summary>
    [Test]
    public void AGuildDeclaresOnFiveAtOnceAndNoMore()
    {
        var (module, world) = Asking("        who.Message(\"\" + Guilds.Outgoing(1));");
        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        for (int slot = 1; slot <= 5; slot++)
        {
            world.SetGuildValue(1, $"gw{slot}op", AttributeValue.From(slot + 10L));
            world.SetGuildValue(1, $"gw{slot}we", AttributeValue.From(slot <= 3 ? 1L : 0L));
        }

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^1], Is.EqualTo("3"), "two of the five were declared on us");
    }

    /// <summary>Returning a declaration makes the war mutual and live at once, and starts both meters.
    /// A war both sides chose needs no warmup.</summary>
    [Test]
    public void ReturningADeclarationMakesTheWarMutualAtOnce()
    {
        var (module, world) = Asking("        Guilds.War(who, \"Theirs\");");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.SetGuildValue(1, "glevel", AttributeValue.From(2L));

        // They declared on us; our entry says so, and we have not answered.
        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1they", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1live", AttributeValue.From(world.Clock + 300));
        world.SetGuildValue(2, "gw1op", AttributeValue.From(1L));
        world.SetGuildValue(2, "gw1we", AttributeValue.From(1L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1we"), Is.EqualTo(1L), "we have answered");
            Assert.That(Guild(world, 1, "gw1live"), Is.EqualTo(world.Clock), "and it is live now");
            Assert.That(Guild(world, 1, "gw1att"), Is.EqualTo(1000L), "our meter starts full");
            Assert.That(Guild(world, 2, "gw1att"), Is.EqualTo(1000L), "and so does theirs");
            Assert.That(world.Spent, Is.Empty, "answering costs nothing");
        });
    }

    /// <summary>A death in a mutual war swings the meter by the gold it cost the victim's vault plus a
    /// flat rate, zero-sum: the victim's side falls by what the killer's side gains.</summary>
    [Test]
    public void AWarDeathSwingsBothMetersByTheSameAmount()
    {
        var (module, world) = Asking("""
                    Player? them = who.Find("Rival");

                    if World.GuildOf(who) == 1
                        Guilds.Died(them.Value(), who, true, 30);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";

        Mutual(world);

        ((ITickWork)scripts).Tick(1);

        // 30 of treasury damage, plus the flat 20 at a fresh target's full weight.
        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 2, "gw1att"), Is.EqualTo(950L), "the victim's side fell by 50");
            Assert.That(Guild(world, 1, "gw1att"), Is.EqualTo(1000L), "and the killer's was already full");
            Assert.That(Guild(world, 2, "gw1low"), Is.EqualTo(950L), "a new low was reached");
        });
    }

    /// <summary>A target killed repeatedly is worth less: the flat rate counts at 100, 75, 50, then 25
    /// percent, and stays at 25 for every kill after that so a death always moves the meter.</summary>
    [Test]
    public void AFarmedTargetIsWorthLessButNeverNothing()
    {
        var (module, world) = Asking("""
                    who.Message("" + Guilds.Weight(1) + " " + Guilds.Weight(2) + " "
                                   + Guilds.Weight(3) + " " + Guilds.Weight(4) + " " + Guilds.Weight(9));
            """);

        using ScriptedWorldModule scripts = module;
        world.Here.Add(EntityHandle.ForPlayer(1));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^1], Is.EqualTo("100 75 50 25 25"));
    }

    /// <summary>Five vault-uncovered deaths in a row lose the war outright, without the meter running
    /// out. A guild that cannot pay for its own casualties has already lost.</summary>
    [Test]
    public void FiveUncoveredDeathsLoseTheWar()
    {
        var (module, world) = Asking("""
                    Player? them = who.Find("Rival");

                    if World.GuildOf(who) == 1
                        loop for _ = 1 to 5
                            Guilds.Died(them.Value(), who, false, 0);
                        end loop
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";

        Mutual(world);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1op"), Is.Zero, "the war is over");
            Assert.That(Guild(world, 2, "gw1op"), Is.Zero, "on both sides");
            Assert.That(world.Announced.Select(a => a.Item2), Has.Some.Contains("bankrupt"));
            Assert.That(Guild(world, 1, "gcd2"), Is.GreaterThan(world.Clock),
                "and neither may declare on the other again yet");
        });
    }

    /// <summary>Two hours with neither side pushing the other to a new low ends a mutual war as a
    /// draw.</summary>
    [Test]
    public void AWarNobodyIsFightingGoesCold()
    {
        var (module, world) = Asking("        yield;");
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";

        Mutual(world);
        world.SetGuildValue(1, "gw1prog", AttributeValue.From(world.Clock - 7200));
        world.SetGuildValue(2, "gw1prog", AttributeValue.From(world.Clock - 7200));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1op"), Is.Zero);
            Assert.That(world.Announced.Select(a => a.Item2), Has.Some.Contains("gone cold"));
        });
    }

    /// <summary>With no stake on the war, a plea for peace has to carry gold, and that gold is set aside
    /// out of the vault. Accepting it wins the war and takes the offering.</summary>
    [Test]
    public void SuingForPeaceEscrowsTheOfferingAndAcceptingTakesIt()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.SuePeace(who, 2, 400);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 1000L;

        Mutual(world);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1peace"), Is.EqualTo(1L), "the plea is out");
            Assert.That(Guild(world, 1, "gw1pesc"), Is.EqualTo(400L), "and the offering is set aside");
            Assert.That(world.GuildGold(1), Is.EqualTo(600L), "out of the vault");
        });
    }

    /// <summary>A plea may be at most half the vault, and withdrawing one gives the gold back.</summary>
    [Test]
    public void APleaIsCappedAtHalfTheVaultAndComesBackIfWithdrawn()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.SuePeace(who, 2, 900);
                        Guilds.SuePeace(who, 2, 500);
                        Guilds.WithdrawPeace(who, 2);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 1000L;

        Mutual(world);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("at most 500"), "900 is more than half of 1000");
            Assert.That(Guild(world, 1, "gw1peace"), Is.Zero, "the plea was withdrawn");
            Assert.That(world.GuildGold(1), Is.EqualTo(1000L), "and the offering came back");
        });
    }

    /// <summary>A matched ante takes the amount out of both vaults at once, and only inside the hour
    /// after the war became mutual.</summary>
    [Test]
    public void AnAcceptedWagerLocksBothVaults()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.AcceptWager(who, 2);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 1000L;
        world.Vaults[2] = 1000L;

        Mutual(world);
        world.SetGuildValue(2, "gw1prop", AttributeValue.From(300L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1ante"), Is.EqualTo(300L));
            Assert.That(Guild(world, 2, "gw1ante"), Is.EqualTo(300L), "matched on both sides");
            Assert.That(world.GuildGold(1), Is.EqualTo(700L));
            Assert.That(world.GuildGold(2), Is.EqualTo(700L));
            Assert.That(Guild(world, 2, "gw1prop"), Is.Zero, "the proposal is spent");
        });
    }

    /// <summary>The window closes an hour after the war became mutual. An ante cannot be agreed after
    /// that, though one already locked rides to the end.</summary>
    [Test]
    public void AWagerCannotBeAgreedOnceTheWindowHasClosed()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.AcceptWager(who, 2);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 1000L;
        world.Vaults[2] = 1000L;

        Mutual(world);
        world.SetGuildValue(1, "gw1mut", AttributeValue.From(world.Clock - 3601));
        world.SetGuildValue(2, "gw1prop", AttributeValue.From(300L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Has.Some.Contains("too late"));
            Assert.That(world.GuildGold(1), Is.EqualTo(1000L), "and nothing was taken");
        });
    }

    /// <summary>A war won pays the whole pot to the winner; one that goes cold returns each side its
    /// own stake.</summary>
    [Test]
    public void TheWinnerTakesThePotAndADrawReturnsIt()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.EndWar(1, 2, 1);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";

        Mutual(world);
        world.SetGuildValue(1, "gw1ante", AttributeValue.From(300L));
        world.SetGuildValue(2, "gw1ante", AttributeValue.From(300L));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.GuildGold(1), Is.EqualTo(600L), "winner takes both stakes");
    }

    [Test]
    public void AColdDrawReturnsEachSideItsOwnStake()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.EndWar(1, 2, 0);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";

        Mutual(world);
        world.SetGuildValue(1, "gw1ante", AttributeValue.From(300L));
        world.SetGuildValue(2, "gw1ante", AttributeValue.From(500L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.GuildGold(1), Is.EqualTo(300L));
            Assert.That(world.GuildGold(2), Is.EqualTo(500L));
        });
    }

    /// <summary>A leader accepting a queued request runs the action as the leader, which re-checks every
    /// gate — so an officer cannot get past one by asking.</summary>
    [Test]
    public void AcceptingARequestRunsItAsTheLeader()
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Guilds.Review(who, Guilds.KindDeclare, 2, true);
                    end if
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Ranks[who] = "leader";
        world.Vaults[1] = 200_000L;
        world.SetGuildValue(1, "glevel", AttributeValue.From(3L));
        world.SetGuildValue(2, "glevel", AttributeValue.From(3L));
        world.SetGuildValue(1, "gr1kind", AttributeValue.From(1L));
        world.SetGuildValue(1, "gr1at", AttributeValue.From(2L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1op"), Is.EqualTo(2L), "the war was declared");
            Assert.That(Guild(world, 1, "gr1kind"), Is.Zero, "and the request is gone");
        });
    }

    // ── The calendar ─────────────────────────────────────────────────────────

    /// <summary>🔴 <b>A month is not a fixed number of days, so a repeat that bucketed by thirty would
    /// drift off the calendar within a year.</b> The day number is turned into a real year and month,
    /// and these are checked against the dates .NET gives for the same day.</summary>
    [TestCase(0, ExpectedResult = "1970")]
    [TestCase(59, ExpectedResult = "1970")]
    [TestCase(365, ExpectedResult = "1971")]
    [TestCase(11_688, ExpectedResult = "2002")]
    [TestCase(20_454, ExpectedResult = "2026")]
    [TestCase(20_089, ExpectedResult = "2025")]
    public string TheYearIsReadOffTheDayNumber(int day) => Answered($"Calendar.YearOf({day})");

    [TestCase(0, ExpectedResult = "1")]
    [TestCase(31, ExpectedResult = "2")]
    [TestCase(59, ExpectedResult = "3")]
    [TestCase(364, ExpectedResult = "12")]
    [TestCase(20_454, ExpectedResult = "1")]
    [TestCase(20_255, ExpectedResult = "6")]
    public string TheMonthIsReadOffTheDayNumber(int day) => Answered($"Calendar.MonthOf({day})");

    /// <summary>⚠ <b>Every day of several windows, against what .NET says for the same day.</b> A
    /// calendar that is right in 1970 and wrong in 2030 is the failure this exists to catch, and a
    /// handful of hand-picked dates would not find it. Each window spans a leap year and the
    /// century-rule year 2000.
    ///
    /// <para>Windows rather than the whole range because one handler call has a budget, and sixty
    /// years of days is well past it — a run that overruns stops quietly part way, which reads as the
    /// calendar being wrong from that day on.</para></summary>
    [TestCase(0, 1500)]        // 1970-1974, leap 1972
    [TestCase(10_900, 1500)]   // 1999-2003, leap 2000 — a century year that IS a leap year
    [TestCase(19_700, 1500)]   // 2023-2027, leap 2024
    public void EveryDayInThisWindowAgreesWithTheRealCalendar(int from, int many)
    {
        var (module, world) = Asking($$"""
                    loop for day = {{from}} to {{from + many}}
                        who.Message("" + Calendar.YearOf(day) + "-" + Calendar.MonthOf(day));
                    end loop
            """);

        using ScriptedWorldModule scripts = module;
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said, Has.Count.AtLeast(many + 1),
            "the whole window has to have been walked, or the rest of this proves nothing");

        var start = new DateOnly(1970, 1, 1);
        var wrong = new List<string>();
        for (int i = 0; i <= many; i++)
        {
            var real = start.AddDays(from + i);
            string expected = $"{real.Year}-{real.Month}";
            if (world.Said[i] != expected) wrong.Add($"day {from + i}: said {world.Said[i]}, is {expected}");
        }

        Assert.That(wrong, Is.Empty, () => string.Join("; ", wrong.Take(5)));
    }

    /// <summary>A week is bucketed by the Sunday it began on, which is the boundary the guild
    /// settlement already uses.</summary>
    [Test]
    public void AWeekIsBucketedByTheSundayItBeganOn()
    {
        // 1 January 1970 was a Thursday, so the week holding it began on Sunday 28 December 1969 —
        // four days earlier, which is a day number before the epoch.
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Calendar.WeekOf(0)"), Is.EqualTo("-4"));
            Assert.That(Answered("Calendar.WeekOf(2)"), Is.EqualTo("-4"), "still that week on the Saturday");
            Assert.That(Answered("Calendar.WeekOf(3)"), Is.EqualTo("3"), "and the Sunday starts a new one");
            Assert.That(Answered("Calendar.WeekOf(9)"), Is.EqualTo("3"));
            Assert.That(Answered("Calendar.WeekOf(10)"), Is.EqualTo("10"));
        });
    }

    // ── How often a quest re-opens ───────────────────────────────────────────

    /// <summary>🔴 <b>A repeatable quest is not "take it again whenever".</b> It re-opens on its own
    /// cadence, and two runs inside one period give the same key, which holds it shut.</summary>
    [TestCase(1, 100, 101, ExpectedResult = false)]
    [TestCase(2, 3, 9, ExpectedResult = true)]
    [TestCase(2, 3, 10, ExpectedResult = false)]
    [TestCase(3, 20_454, 20_460, ExpectedResult = true)]
    [TestCase(3, 20_454, 20_500, ExpectedResult = false)]
    [TestCase(0, 0, 99_999, ExpectedResult = true)]
    public bool TwoDaysInOnePeriodShareAKey(int cadence, int first, int later) =>
        Answered($"Quests.PeriodOf({cadence}, {first})")
        == Answered($"Quests.PeriodOf({cadence}, {later})");

    // ── Land, and what holding it is worth ───────────────────────────────────

    /// <summary>🔴 Weeks held multiply what the land pays: fresh land pays once over, and a month of
    /// holding pays four times. Capped there, so land held since the server opened is not an economy of
    /// its own.</summary>
    [TestCase(0, ExpectedResult = "1")]
    [TestCase(1, ExpectedResult = "2")]
    [TestCase(2, ExpectedResult = "3")]
    [TestCase(3, ExpectedResult = "4")]
    [TestCase(9, ExpectedResult = "4")]
    public string HoldingLandPaysMoreTheLongerItIsHeld(int weeks) =>
        Answered($"Territory.HoldMultiplier({weeks})");

    /// <summary>A kill on held land pays its holder whoever did the killing, and a member of the holding
    /// guild is worth double, so held land is worth more to the people holding it than to anybody
    /// passing through.</summary>
    [TestCase("false", 0, ExpectedResult = "35")]
    [TestCase("true", 0, ExpectedResult = "70")]
    [TestCase("false", 3, ExpectedResult = "140")]
    [TestCase("true", 3, ExpectedResult = "280")]
    public string AKillOnHeldLandPaysItsHolder(string byOwner, int weeks) =>
        Answered($"Territory.KillWorth({byOwner}, {weeks})");

    // ── War night ────────────────────────────────────────────────────────────

    /// <summary>Saturday evening, and the week resets the Sunday after, so the new week is built on
    /// a war night's result. Day 3 was the epoch's first Sunday, so day 2 was its first Saturday.</summary>
    [TestCase(2, ExpectedResult = "true")]
    [TestCase(9, ExpectedResult = "true")]
    [TestCase(3, ExpectedResult = "false")]
    [TestCase(20_710, ExpectedResult = "false")]
    public string WarNightIsSaturday(int day) => Answered($"Contest.IsWarNight({day})");

    /// <summary>⚠ The meter DRIFTS toward nothing when a point is contested or empty, rather than
    /// staying put. A point two guilds are standing on in equal numbers is nobody's, and one both sides
    /// walked away from goes back to being up for grabs rather than freezing mid-swing.</summary>
    [TestCase(3, ExpectedResult = "2")]
    [TestCase(1, ExpectedResult = "0")]
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(-1, ExpectedResult = "0")]
    [TestCase(-3, ExpectedResult = "-2")]
    public string AnEmptyPointDriftsBackTowardNothing(int meter) =>
        Answered($"Contest.Drift({meter})");

    /// <summary>🔴 A full swing from securely held to taken is twice the meter's depth — six beats at
    /// five seconds, so about half a minute of standing on a flag. Short enough that a fight over one
    /// point is a fight, long enough that walking past does not take it.</summary>
    [Test]
    public void AFullSwingIsTwiceTheMetersDepth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Contest.Full * 2"), Is.EqualTo("6"));
            Assert.That(Answered("Contest.Full * 2 * Contest.BeatSeconds"), Is.EqualTo("30"));
        });
    }

    /// <summary>The three phases, as the original ran them: ten minutes of setup, twenty of fighting, ten
    /// of cooling off.</summary>
    [Test]
    public void TheEveningRunsFortyMinutes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Contest.SetupSeconds"), Is.EqualTo("600"));
            Assert.That(Answered("Contest.FightSeconds"), Is.EqualTo("1200"));
            Assert.That(Answered("Contest.CooldownSeconds"), Is.EqualTo("600"));
        });
    }

    // ── The leaderboard ──────────────────────────────────────────────────────

    /// <summary>🔴 What a week of holding is worth climbs the longer it has been held, compounding a
    /// quarter a week — so the guild that keeps land beats the guild that keeps taking it. It stops
    /// climbing after twelve weeks, which is a season's worth of streak.</summary>
    [TestCase(0, ExpectedResult = "100")]
    [TestCase(1, ExpectedResult = "125")]
    [TestCase(4, ExpectedResult = "200")]
    [TestCase(12, ExpectedResult = "400")]
    [TestCase(40, ExpectedResult = "400")]
    public string AWeekOfHoldingIsWorthMoreTheLongerItIsHeld(int weeks) =>
        Answered($"Season.HoldScore({weeks})");

    /// <summary>The placings, as the original paid them. Fourth and below take the flat scorer's share;
    /// a guild that scored nothing takes nothing at all.</summary>
    [TestCase(1, ExpectedResult = "700000")]
    [TestCase(2, ExpectedResult = "350000")]
    [TestCase(3, ExpectedResult = "175000")]
    [TestCase(4, ExpectedResult = "35000")]
    [TestCase(9, ExpectedResult = "35000")]
    [TestCase(0, ExpectedResult = "0")]
    public string APlacingPaysTheVault(int placing) => Answered($"Season.VaultPrize({placing})");

    [TestCase(1, ExpectedResult = "175000")]
    [TestCase(2, ExpectedResult = "87500")]
    [TestCase(3, ExpectedResult = "35000")]
    [TestCase(4, ExpectedResult = "17500")]
    [TestCase(0, ExpectedResult = "0")]
    public string APlacingPaysEachMember(int placing) => Answered($"Season.MemberPrize({placing})");

    /// <summary>⚠ Scoring SKIPS the season's first week, so control established before the season began
    /// carries in rather than paying from its first Sunday.</summary>
    [Test]
    public void TheSeasonsFirstWeekScoresNothing()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("""
                    who.Message("" + Season.WeeksIn(Calendar.Today()));
            """, world);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        // A season that began on the first Sunday the epoch saw, read three weeks later.
        world.WorldBag.Set("seasonbegan", 3L);
        world.Clock = (3L + 21L) * 86_400L + 12L * 3_600L;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said[^1], Is.EqualTo("3"));
            Assert.That(Answered("Season.ScoringFromWeek"), Is.EqualTo("1"),
                "and the first week of one pays nothing");
        });
    }

    /// <summary>⚠ A kill over LAND pays valor five times as often as a grudge war does, so held
    /// territory is the richer source of it.</summary>
    [Test]
    public void LandPaysValorFiveTimesAsOftenAsAGrudge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Valor.GrudgeChancePercent"), Is.EqualTo("10"));
            Assert.That(Answered("Valor.TerritoryChancePercent"), Is.EqualTo("50"));
        });
    }

    // ── Whose midnight ───────────────────────────────────────────────────────

    private string TodayAt(long moment, int offset)
    {
        var world = new ScriptedWorldTests.RecordingWorld { Clock = moment, Offset = offset };
        var (module, _) = Asking("""
                    who.Message("" + Calendar.Today());
            """, world);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));
        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    /// <summary>🔴 <b>Midnight means the OPERATOR's midnight.</b> A daily settlement, a weekly tax and a
    /// season all turn over on the server's own civil day, as they did in the original — so a world east
    /// of Greenwich is already on tomorrow while UTC is still on today, and one west of it is still on
    /// yesterday after UTC has moved on.</summary>
    [Test]
    public void TheDayTurnsOverAtTheServersOwnMidnight()
    {
        const long HalfPastEleven = 19_000L * 86_400L + 23L * 3_600L + 1_800L;
        const long HalfPastMidnight = 19_000L * 86_400L + 1_800L;

        Assert.Multiple(() =>
        {
            Assert.That(TodayAt(HalfPastEleven, offset: 0), Is.EqualTo("19000"),
                "at Greenwich the day is UTC's day");
            Assert.That(TodayAt(HalfPastEleven, offset: 2 * 3_600), Is.EqualTo("19001"),
                "two hours east it is already tomorrow");
            Assert.That(TodayAt(HalfPastMidnight, offset: -5 * 3_600), Is.EqualTo("18999"),
                "and five hours west it is still yesterday");
        });
    }

    // ── Seasons ──────────────────────────────────────────────────────────────
    //
    // 🔴 A season is not a slice of the calendar. It starts when the world starts one and runs thirteen
    // whole weeks from there, so there is nothing about a DATE that could name it. The seasonal
    // cadence above is left out of that table and asked for here instead.

    /// <summary>Days since 1970 for a day the world's week resets on. Day 3 was the first Sunday the
    /// epoch saw, so every Sunday after it is three more than a multiple of seven.</summary>
    private static long SecondsOnSunday(int weeksAfterTheFirst) =>
        (3L + weeksAfterTheFirst * 7L) * 86_400L + 12L * 3_600L;

    /// <summary>Runs the world's own beat once for each moment given, and answers with the season
    /// afterwards. The helper exists to move the clock by hand between beats.</summary>
    private string SeasonAfter(params long[] moments)
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("""
                    who.Message("" + Season.Which());
            """, world);

        using ScriptedWorldModule scripts = module;

        world.Here.Add(EntityHandle.ForPlayer(1));

        foreach (long moment in moments)
        {
            world.Clock = moment;
            ((ITickWork)scripts).Tick(1);
        }

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    /// <summary>⚠ A world nobody has run before is in NO season, and adopts the first week reset it
    /// sees as the start of season one. Counting from the epoch instead would put a world three days
    /// old in its two-hundredth season.</summary>
    [Test]
    public void AWorldThatHasNeverRun_IsInNoSeasonUntilItsFirstReset()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeasonAfter(SecondsOnSunday(0) + 86_400L), Is.EqualTo("0"),
                "a Monday, and nothing has started yet");
            Assert.That(SeasonAfter(SecondsOnSunday(0)), Is.EqualTo("1"),
                "and the reset day it does see is season one");
        });
    }

    /// <summary>Thirteen whole weeks, and the twelfth reset is still inside the first one.</summary>
    [Test]
    public void ASeasonRunsThirteenWeeks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeasonAfter(SecondsOnSunday(0), SecondsOnSunday(12)), Is.EqualTo("1"));
            Assert.That(SeasonAfter(SecondsOnSunday(0), SecondsOnSunday(13)), Is.EqualTo("2"));
            Assert.That(SeasonAfter(SecondsOnSunday(0), SecondsOnSunday(26)), Is.EqualTo("3"));
        });
    }

    /// <summary>🔴 <b>A server that was off over a reset still crosses it.</b> The days are walked
    /// rather than the answer read off today, so a world nobody logged into for a fortnight comes back
    /// having turned the season over rather than having missed it.</summary>
    [Test]
    public void AServerThatWasOff_StillCrossesTheResetItMissed()
    {
        Assert.That(SeasonAfter(SecondsOnSunday(0), SecondsOnSunday(14)), Is.EqualTo("2"),
            "one beat, fourteen weeks later, and the thirteen-week boundary was still crossed");
    }

    /// <summary>⚠ And nothing is settled retroactively. A world brought up after a year away begins a
    /// season rather than ending fifty of them.</summary>
    [Test]
    public void AWorldBroughtUpAfterAYear_BeginsOneSeason()
    {
        Assert.That(SeasonAfter(SecondsOnSunday(52)), Is.EqualTo("1"));
    }

    /// <summary>A repeat run pays the repeat set. ⚠ A repeat reward left at nought is "nobody said"
    /// rather than "pays nothing", so it falls back to the first run's — a quest an author gave a
    /// cadence and no repeat rewards still pays.</summary>
    [Test]
    public void ARepeatPaysItsOwnRewardsOrTheFirstRuns()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        world.Records["Quest"] =
        [
            Row(("name", "Wolves"), ("rewardExp", 500L), ("rewardItem", 4L), ("rewardMany", 3L),
                ("cadence", 1L), ("repeatExp", 120L), ("repeatItem", 9L), ("repeatMany", 1L)),
            Row(("name", "Rats"), ("rewardExp", 80L), ("rewardItem", 4L), ("rewardMany", 2L),
                ("cadence", 1L)),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Asks(world, "Quests.PaidExp(1, true)"), Is.EqualTo("120"));
            Assert.That(Asks(world, "Quests.PaidItem(1, true)"), Is.EqualTo("9"));
            Assert.That(Asks(world, "Quests.PaidMany(1, true)"), Is.EqualTo("1"));

            Assert.That(Asks(world, "Quests.PaidExp(1, false)"), Is.EqualTo("500"), "a first run pays its own");

            Assert.That(Asks(world, "Quests.PaidExp(2, true)"), Is.EqualTo("80"),
                "nothing was said about a repeat, so it pays what the first run did");
            Assert.That(Asks(world, "Quests.PaidMany(2, true)"), Is.EqualTo("2"));
        });
    }

    // ── What a reaction costs, and what it needs ─────────────────────────────
    //
    // From CombatSystem.Procs + CombatSystem.Costs: none of block, dodge or crit is a bare roll. Each
    // needs stamina left AND the right thing in hand, each spends stamina when it fires, and heavy wind
    // stops all three. A port that rolled them free would be a different game at every level.

    /// <summary>🔴 <b>A block needs a shield; a dodge needs the hand a shield would be in.</b> They are
    /// opposites, not alternatives, so carrying one is a decision rather than a strictly better
    /// choice.</summary>
    [Test]
    public void BlockNeedsAShieldAndDodgeNeedsNone()
    {
        var world = Armed(shield: 0, weapon: 0);
        Assert.Multiple(() =>
        {
            Assert.That(Asks(world, "Combat.CanBlock(who)"), Is.EqualTo("false"), "no shield, no block");
            Assert.That(Asks(world, "Combat.CanDodge(who)"), Is.EqualTo("true"), "and a free hand dodges");
        });

        var shielded = Armed(shield: 5, weapon: 0);
        Assert.Multiple(() =>
        {
            Assert.That(Asks(shielded, "Combat.CanBlock(who)"), Is.EqualTo("true"));
            Assert.That(Asks(shielded, "Combat.CanDodge(who)"), Is.EqualTo("false"),
                "you cannot sidestep from behind a shield");
        });
    }

    /// <summary>A critical needs a weapon. Bare hands never crit.</summary>
    [Test]
    public void ACriticalNeedsAWeapon()
    {
        Assert.That(Asks(Armed(shield: 0, weapon: 0), "Combat.CanCrit(who)"), Is.EqualTo("false"));
        Assert.That(Asks(Armed(shield: 0, weapon: 7), "Combat.CanCrit(who)"), Is.EqualTo("true"));
    }

    /// <summary>⚠ <b>All three gate on stamina.</b> A body with none left keeps swinging and keeps
    /// taking hits, which is the whole reason stamina is a pool.
    ///
    /// <para>The pool is emptied inside the same body that asks, because regen runs earlier in the same
    /// tick and would otherwise have filled it back up before the question was put.</para></summary>
    [Test]
    public void NoneOfThemHappensWithNoStaminaLeft()
    {
        var world = Armed(shield: 5, weapon: 7);

        var (module, _) = Asking("""
                    who.SetNumber(Vitals.Stamina, 0);
                    who.Message("" + Combat.CanBlock(who));
                    who.Message("" + Combat.CanCrit(who));
            """, world);

        using ScriptedWorldModule scripts = module;
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^2..], Is.EqualTo(new[] { "false", "false" }));
    }

    /// <summary>And a heavy wind stops all three outright, whatever is in hand.</summary>
    [Test]
    public void AHeavyWindStopsEveryReaction()
    {
        var world = Armed(shield: 5, weapon: 7);
        world.Weather = "heavywind";

        Assert.Multiple(() =>
        {
            Assert.That(Asks(world, "Combat.CanBlock(who)"), Is.EqualTo("false"));
            Assert.That(Asks(world, "Combat.CanCrit(who)"), Is.EqualTo("false"));
            Assert.That(Asks(Armed(shield: 0, weapon: 0, weather: "heavywind"), "Combat.CanDodge(who)"),
                Is.EqualTo("false"));
        });
    }

    /// <summary>What each costs: a fiftieth of the pool for a block or a crit, a twenty-fifth for a
    /// dodge — twice as much — and never less than a point.</summary>
    [TestCase(200, "Combat.BlockCostShare", ExpectedResult = "4")]
    [TestCase(200, "Combat.CritCostShare", ExpectedResult = "4")]
    [TestCase(200, "Combat.DodgeCostShare", ExpectedResult = "8")]
    [TestCase(10, "Combat.BlockCostShare", ExpectedResult = "1")]
    [TestCase(1, "Combat.DodgeCostShare", ExpectedResult = "1")]
    public string AReactionCostsAShareOfTheWholePool(int pool, string share) =>
        Answered($"Combat.Cost({pool}, {share}, 1)");

    /// <summary>A heat wave doubles every one of them.</summary>
    [Test]
    public void AHeatWaveDoublesWhatAReactionCosts()
    {
        var world = new ScriptedWorldTests.RecordingWorld { Weather = "heatwave" };

        Assert.That(Asks(world, "Combat.Cost(200, Combat.BlockCostShare, 1)"), Is.EqualTo("8"));
    }

    /// <summary>And spending it takes the body no further than nothing.</summary>
    [Test]
    public void SpendingStaminaStopsAtNothing()
    {
        var world = Armed(shield: 5, weapon: 7);

        var (module, _) = Asking("""
                    who.SetNumber(Vitals.Stamina, 3);
                    Combat.Spend(who, Combat.DodgeCostShare);
            """, world);

        using ScriptedWorldModule scripts = module;
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Bag["sp"].AsLong(), Is.Zero, "eight off three is nothing, never minus five");
    }

    /// <summary>A body wearing what the test says, with a full pool and clear skies.</summary>
    private static ScriptedWorldTests.RecordingWorld Armed(int shield, int weapon, string weather = "clear")
    {
        var world = new ScriptedWorldTests.RecordingWorld { Weather = weather };
        var who = EntityHandle.ForPlayer(1);

        world.Here.Add(who);
        world.Bag.Set("sp", AttributeValue.From(100L));
        world.Bag.Set("maxsp", AttributeValue.From(200L));

        if (shield > 0) world.Equipped[(who, "shield")] = shield;
        if (weapon > 0) world.Equipped[(who, "weapon")] = weapon;

        return world;
    }

    /// <summary>One expression, answered against a world a test built.</summary>
    private string Asks(ScriptedWorldTests.RecordingWorld world, string expression)
    {
        var (module, _) = Asking($"        who.Message(\"\" + {expression});", world);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    // ── Who may fight whom ───────────────────────────────────────────────────
    //
    // The original's gates, from CombatSystem.Pvp: party protection everywhere, guild protection
    // except in an arena, a protected map on either side, and nobody under level ten on either side.
    // The player reads the first refusal back, so each of these pins which reason comes back.

    /// <summary>🔴 <b>A partymate is protected everywhere, the arena included.</b> An organized team
    /// match has no friendly fire in it, which separates party protection from guild
    /// protection.</summary>
    [Test]
    public void APartymateCannotBeFoughtEvenInAnArena()
    {
        var world = Fighters(out var who, out var them);
        world.Parties[who] = [them];
        world.MapFields[1] = Row(("moral", 2L));

        Assert.That(Refusal(world), Does.Contain("your own party"));
    }

    /// <summary>A guildmate is protected too, and that protection lifts in an arena, where a kill
    /// costs nothing anyway.</summary>
    [Test]
    public void AGuildmateIsProtectedExceptInAnArena()
    {
        var world = Fighters(out var who, out var them);
        world.InGuild[who] = 3;
        world.InGuild[them] = 3;

        Assert.That(Refusal(world), Does.Contain("your own guild"));

        var arena = Fighters(out var mate, out var other);
        arena.InGuild[mate] = 3;
        arena.InGuild[other] = 3;
        arena.MapFields[1] = Row(("moral", 2L));

        Assert.That(Refusal(arena), Is.Empty, "in an arena they may duel");
    }

    /// <summary>A protected map on EITHER side stops it, and a map that says nothing takes its
    /// region's answer.</summary>
    [Test]
    public void AProtectedMapStopsAFightFromEitherSide()
    {
        var theirs = Fighters(out _, out _);
        theirs.MapFields[2] = Row(("moral", 1L));

        Assert.That(Refusal(theirs), Does.Contain("safe place"),
            "the map the other one is standing on is enough");
    }

    /// <summary>Nobody under level ten fights, or is fought.</summary>
    [Test]
    public void NobodyUnderLevelTenFightsOrIsFought()
    {
        var world = Fighters(out var who, out _);
        world.Bag.Set("level", AttributeValue.From(4L));

        Assert.That(Refusal(world), Does.Contain("too low a level to fight"));
    }

    /// <summary>Two bodies on two different maps, each at level twenty, with nothing between them.
    /// The attribute bag is shared by the double, so the level is one number for both.</summary>
    private static ScriptedWorldTests.RecordingWorld Fighters(out EntityHandle who, out EntityHandle them)
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        who = EntityHandle.ForPlayer(1);
        them = EntityHandle.ForPlayer(2);

        world.Here.Add(who);
        world.Here.Add(them);
        world.Names[who] = "Ours";
        world.Names[them] = "Rival";
        world.Standing[new WorldPlace(1, 5, 5)] = who;
        world.Standing[new WorldPlace(2, 5, 5)] = them;
        world.Bag.Set("level", AttributeValue.From(20L));

        return world;
    }

    /// <summary>What the rules say about one hitting the other, or empty where they allow it.</summary>
    private string Refusal(ScriptedWorldTests.RecordingWorld world)
    {
        var (module, _) = Asking("""
                    # ⚠ Only the one asking. The tick walks every body in the world, and the
                    # rival asking about themselves is a different question with a different answer.
                    if who.Name == "Ours"
                        Player? them = who.Find("Rival");

                        if them.HasValue()
                            who.Message(Duel.Judge(who, them.Value()));
                        end if
                    end if
            """, world);

        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        return world.Said.Count > 0 ? world.Said[^1] : "(nothing)";
    }

    // ── What an ordinary death costs ─────────────────────────────────────────

    /// <summary>Worn gear loses that share of its FULL durability, never less than a point — so a
    /// death always leaves a mark, whatever the piece is made of.</summary>
    [TestCase(100, 10, ExpectedResult = "10")]
    [TestCase(100, 20, ExpectedResult = "20")]
    [TestCase(3, 10, ExpectedResult = "1")]
    [TestCase(1, 10, ExpectedResult = "1")]
    [TestCase(45, 20, ExpectedResult = "9")]
    public string DeathWearsGearByShareOfItsWholeDurability(int full, int percent) =>
        Answered($"Death.WearOn({full}, {percent})");

    // ── The credit board ─────────────────────────────────────────────────────

    /// <summary>🔴 <b>A war death belongs to whoever dealt the MOST damage, not to whoever landed the
    /// last blow.</b> Two guilds beating on one person is exactly the case that tells them apart, and
    /// getting it wrong charges the wrong vault.</summary>
    [Test]
    public void TheCreditedKillerIsWhoeverDealtTheMost()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var who = EntityHandle.ForPlayer(1);
        var first = EntityHandle.ForPlayer(2);
        var second = EntityHandle.ForPlayer(3);

        world.Here.Add(who);
        world.Here.Add(first);
        world.Here.Add(second);
        world.Names[who] = "Ours";
        world.Names[first] = "First";
        world.Names[second] = "Second";

        // A bag each. The running totals are held on the ATTACKER, keyed by who they are hitting, so
        // two attackers sharing one bag would be one attacker with both their totals.
        world.BagFor(first);
        world.BagFor(second);

        // Enough health to take all three, so the board is read while they are still alive.
        world.BagFor(who).Set("hp", AttributeValue.From(500L));

        var (module, _) = Asking("""
                    if who.Name == "Ours"
                        Player? one = who.Find("First");
                        Player? two = who.Find("Second");

                        Duel.Land(one.Value(), who, 30, false);
                        Duel.Land(two.Value(), who, 50, false);
                        Duel.Land(one.Value(), who, 10, false);

                        who.Message(Duel.CreditedTo(who));
                    end if
            """, world);

        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^1], Is.EqualTo("Second"),
            "forty from one and fifty from the other, and the last blow was the smaller one");
    }

    // ── The weekly settlement ────────────────────────────────────────────────

    /// <summary>A guild pays 35,000 a week for every level it holds. Level zero is free, and has no
    /// privileges to lose either.</summary>
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(1, ExpectedResult = "35000")]
    [TestCase(3, ExpectedResult = "105000")]
    [TestCase(5, ExpectedResult = "175000")]
    public string TheWeeklyTaxIsPerLevel(int level) => Answered($"Ledger.WeeklyTax({level})");

    /// <summary>🔴 <b>Valor is spent on the bill before gold is, in whole steps, and never past half
    /// of it.</b> Ten valor takes 3,500 off — so at level five, 250 valor takes 87,500 off 175,000 and
    /// a vault holding more than that gets nothing extra for it.</summary>
    [TestCase(0, 175_000, ExpectedResult = "0")]
    [TestCase(9, 175_000, ExpectedResult = "0")]
    [TestCase(10, 175_000, ExpectedResult = "1")]
    [TestCase(19, 175_000, ExpectedResult = "1")]
    [TestCase(250, 175_000, ExpectedResult = "25")]
    [TestCase(1_000, 175_000, ExpectedResult = "25")]
    [TestCase(1_000, 35_000, ExpectedResult = "5")]
    [TestCase(100, 0, ExpectedResult = "0")]
    public string ValorOffsetsTheTaxInWholeStepsUpToHalfOfIt(int valor, int tax) =>
        Answered($"Ledger.ValorSteps({valor}, {tax})");

    /// <summary>The tax comes out of the vault whole or not at all, and the valor goes with it.</summary>
    [Test]
    public void AVaultThatCanPayTheTaxPaysItWithItsValor()
    {
        var (module, world) = AtTaxDay(gold: 200_000L, valor: 100L, level: 5);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            // 175,000 owed, ten steps of valor takes 35,000 off it.
            Assert.That(world.Spent, Is.EqualTo(new[] { (1, 140_000L) }));
            Assert.That(Guild(world, 1, "gvalor"), Is.Zero, "and the valor went with it");
            Assert.That(Guild(world, 1, "gperks"), Is.EqualTo(1L), "privileges stay in force");
        });
    }

    /// <summary>⚠ <b>A vault that cannot cover the bill pays NOTHING</b> — no part payment, no back
    /// taxes, and the valor is untouched. What it loses is the privileges, until a week it can pay.</summary>
    [Test]
    public void AVaultThatCannotPayTheTaxLosesItsPrivileges()
    {
        var (module, world) = AtTaxDay(gold: 100L, valor: 100L, level: 5);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Spent, Is.Empty, "nothing was taken");
            Assert.That(Guild(world, 1, "gvalor"), Is.EqualTo(100L), "and the valor is still there");
            Assert.That(Guild(world, 1, "gperks"), Is.Zero, "the privileges are suspended");
            Assert.That(world.Announced, Has.Some.Matches<(string Audience, string Text)>(
                a => a.Text.Contains("suspended")));
        });
    }

    /// <summary>A guild at level zero owes nothing, so its settlement takes nothing and says
    /// nothing.</summary>
    [Test]
    public void AGuildAtLevelZeroOwesNothing()
    {
        var (module, world) = AtTaxDay(gold: 200_000L, valor: 0L, level: 0);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Spent, Is.Empty);
    }

    /// <summary>🔴 <b>A declaration nobody answered costs half its price every day it stands, and a
    /// vault that cannot keep up loses the war.</b> That is the whole penalty: a one-sided grievance
    /// costs gold to hold open.</summary>
    [Test]
    public void AnUnansweredDeclarationIsPaidForDaily()
    {
        var (module, world) = AtTaxDay(gold: 200_000L, valor: 0L, level: 0);
        using ScriptedWorldModule scripts = module;

        world.GuildNames[2] = "Theirs";
        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1we", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1live", AttributeValue.From(world.Clock - 1));
        world.SetGuildValue(1, "gw1cost", AttributeValue.From(35_000L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Spent, Is.EqualTo(new[] { (1, 17_500L) }), "half the declare price");
            Assert.That(Guild(world, 1, "gw1op"), Is.EqualTo(2L), "and the war still stands");
        });
    }

    /// <summary>And one it cannot pay for is dropped, from both sides.</summary>
    [Test]
    public void ADeclarationTheVaultCannotKeepUpIsDropped()
    {
        var (module, world) = AtTaxDay(gold: 100L, valor: 0L, level: 0);
        using ScriptedWorldModule scripts = module;

        world.GuildNames[2] = "Theirs";
        world.SetGuildValue(1, "gw1op", AttributeValue.From(2L));
        world.SetGuildValue(1, "gw1we", AttributeValue.From(1L));
        world.SetGuildValue(1, "gw1live", AttributeValue.From(world.Clock - 1));
        world.SetGuildValue(1, "gw1cost", AttributeValue.From(35_000L));
        world.SetGuildValue(2, "gw1op", AttributeValue.From(1L));
        world.SetGuildValue(2, "gw1they", AttributeValue.From(1L));

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(Guild(world, 1, "gw1op"), Is.Zero, "our side is gone");
            Assert.That(Guild(world, 2, "gw1op"), Is.Zero, "and so is theirs");
        });
    }

    /// <summary>⚠ A mutual war costs NOTHING to keep. Both sides want it, so neither pays to be
    /// there.</summary>
    [Test]
    public void AMutualWarCostsNothingToKeep()
    {
        var (module, world) = AtTaxDay(gold: 200_000L, valor: 0L, level: 0);
        using ScriptedWorldModule scripts = module;

        world.GuildNames[2] = "Theirs";
        Mutual(world);
        world.SetGuildValue(1, "gw1cost", AttributeValue.From(35_000L));

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Spent, Is.Empty);
    }

    /// <summary>A guild with one day owing, settled on the tick. Founded a week ago to the day, so
    /// today is its tax day.</summary>
    private (ScriptedWorldModule Module, ScriptedWorldTests.RecordingWorld World) AtTaxDay(
        long gold, long valor, int level)
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var who = EntityHandle.ForPlayer(1);

        world.Here.Add(who);
        world.Names[who] = "Ours";
        world.InGuild[who] = 1;
        world.GuildNames[1] = "Ours";
        world.Vaults[1] = gold;

        long today = world.Clock / 86_400L;

        world.SetGuildValue(1, "glevel", AttributeValue.From((long)level));
        world.SetGuildValue(1, "gvalor", AttributeValue.From(valor));
        world.SetGuildValue(1, "gperks", AttributeValue.From(1L));
        world.SetGuildValue(1, "gfound", AttributeValue.From(today - 7));

        // One day owing: the cursor sits on yesterday, so the tick settles exactly today.
        world.SetGuildValue(1, "gsettled", AttributeValue.From(today - 1));

        return (Asking("", world).Module, world);
    }

    // ── Guild quests ─────────────────────────────────────────────────────────────

    /// <summary>What a leader pays to acquire one, which is also the floor the reward has to
    /// beat.</summary>
    [TestCase(0, ExpectedResult = "0")]
    [TestCase(1, ExpectedResult = "17500")]
    [TestCase(5, ExpectedResult = "87500")]
    public string AGuildQuestCostsPerGuildLevel(int level) => Answered($"GuildQuest.Cost({level})");

    /// <summary>🔴 <b>The same roll sizes the objective and the reward</b>, so a quest that asks for
    /// more always pays proportionally more. Fifty is the flat baseline; nought and a hundred are the
    /// quarter either side of it.</summary>
    [TestCase(0, 0, ExpectedResult = "225")]
    [TestCase(50, 0, ExpectedResult = "300")]
    [TestCase(100, 0, ExpectedResult = "375")]
    [TestCase(50, 200, ExpectedResult = "500")]
    [TestCase(100, 1000, ExpectedResult = "1000")]
    public string AGuildQuestsKillCountScalesWithTheCreature(int roll, int difficulty) =>
        Answered($"GuildQuest.KillsFor({difficulty}, {roll}, false)");

    /// <summary>⚠ A boss is asked for in TENS on a far shallower slope, so "kill three hundred bosses"
    /// can never come up.</summary>
    [TestCase(50, 0, ExpectedResult = "30")]
    [TestCase(50, 300, ExpectedResult = "60")]
    [TestCase(100, 3000, ExpectedResult = "100")]
    public string AGuildQuestOnABossIsAskedForInTens(int roll, int difficulty) =>
        Answered($"GuildQuest.KillsFor({difficulty}, {roll}, true)");

    /// <summary>Experience scales with the guild's level and the creature — and a guild at the top is
    /// paid none, because it has nothing left to spend it on.</summary>
    [TestCase(0, 0, ExpectedResult = "33000")]
    [TestCase(1, 0, ExpectedResult = "66000")]
    [TestCase(1, 100, ExpectedResult = "96000")]
    [TestCase(5, 100, ExpectedResult = "0")]
    public string AGuildQuestsExperienceScalesWithTheGuild(int level, int difficulty) =>
        Answered($"GuildQuest.ExpFor({difficulty}, {level}, 50, false)");

    /// <summary>🔴 <b>Finishing a quest always leaves the vault better off than acquiring it did.</b>
    /// The reward is floored at the acquire cost plus the base, so a level-five guild can never pay
    /// 87,500 for a target worth less.</summary>
    [TestCase(0, 0, ExpectedResult = "8750")]
    [TestCase(1, 0, ExpectedResult = "26250")]
    [TestCase(5, 0, ExpectedResult = "96250")]
    [TestCase(5, 1000, ExpectedResult = "284375")]
    public string AGuildQuestPaysMoreThanItCost(int level, int difficulty) =>
        Answered($"GuildQuest.GoldFor({difficulty}, {level}, 50, false)");

    // ── What a war death costs the loser ─────────────────────────────────────
    //
    // The original's rule, from GuildWarFormulas + CombatSystem.GuildWar: worn gear wears at 20% of
    // full durability, the vault pre-pays 75% of repairing all of that, and when it can only a
    // quarter of the wear reaches the gear. When it cannot, the whole doubled wear lands.

    /// <summary>🔴 <b>An empty vault means the whole doubled wear lands on the player.</b> 20% of a
    /// hundred-point piece is twenty points, and nothing absorbs any of it.</summary>
    [Test]
    public void AWarDeathWithNothingInTheVaultWearsTheGearInFull()
    {
        var (module, world) = AtWarAndKilled(vault: 0L);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Wearing[(EntityHandle.ForPlayer(1), 7)].Left, Is.EqualTo(80),
                "twenty points of a hundred");
            Assert.That(world.Spent, Is.Empty, "and there was nothing to take");
            Assert.That(Guild(world, 1, "gw1unc"), Is.EqualTo(1L),
                "a death the vault could not cover is one step toward bankruptcy");
        });
    }

    /// <summary>And a vault that can pay takes three quarters of the repair bill, after which only a
    /// quarter of the wear reaches the gear — half what an ordinary death would have cost.</summary>
    [Test]
    public void AVaultThatCanPayTakesThreeQuartersOfTheRepair()
    {
        var (module, world) = AtWarAndKilled(vault: 1_000L);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            // Twenty points at two gold a point is forty; three quarters of that is thirty.
            Assert.That(world.Spent, Is.EqualTo(new[] { (1, 30L) }), "the vault paid the repair share");
            Assert.That(world.Wearing[(EntityHandle.ForPlayer(1), 7)].Left, Is.EqualTo(95),
                "and a quarter of the wear reached the gear");
            Assert.That(Guild(world, 1, "gw1unc"), Is.Zero, "a covered death resets the streak");
        });
    }

    /// <summary>A vault holding less than the whole share pays NOTHING. Whole or nothing is the rule:
    /// a part payment would mean a guild bleeding gold on every death and still taking the full
    /// wear.</summary>
    [Test]
    public void AVaultShortOfTheShareLetsTheWholeWearLand()
    {
        var (module, world) = AtWarAndKilled(vault: 29L);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Spent, Is.Empty, "a short vault pays nothing at all");
            Assert.That(world.Wearing[(EntityHandle.ForPlayer(1), 7)].Left, Is.EqualTo(80),
                "so the whole doubled wear lands");
        });
    }

    /// <summary>⚠ <b>Only the side that DECLARED pays.</b> A guild declared upon that never answered
    /// loses nothing by dying, so a one-sided war is no way to bleed a guild that never agreed to
    /// fight.</summary>
    [Test]
    public void AGuildThatNeverDeclaredLosesNothingByDying()
    {
        var (module, world) = AtWarAndKilled(vault: 1_000L, weDeclared: false);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Spent, Is.Empty, "the vault was not touched");
            Assert.That(world.Wearing[(EntityHandle.ForPlayer(1), 7)].Left, Is.EqualTo(100),
                "and the gear is as it was");
        });
    }

    /// <summary>A piece worn through comes OFF, and stays in the bag. Nothing about a death destroys
    /// gear.</summary>
    [Test]
    public void GearWornThroughInAWarComesOff()
    {
        var (module, world) = AtWarAndKilled(vault: 0L);
        using ScriptedWorldModule scripts = module;

        // Fifteen points left against a twenty-point loss: it goes to nothing.
        world.Wearing[(EntityHandle.ForPlayer(1), 7)] = (15, 100);

        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(world.Wearing[(EntityHandle.ForPlayer(1), 7)].Left, Is.Zero);
            Assert.That(world.Removed, Is.EqualTo(new[] { 7 }), "and it was taken off");
        });
    }

    /// <summary>Guild 1 at mutual war with guild 2, its member wearing one hundred-point piece, and
    /// the rival's member killing them on the tick. Each test varies the vault.</summary>
    private (ScriptedWorldModule Module, ScriptedWorldTests.RecordingWorld World) AtWarAndKilled(
        long vault, bool weDeclared = true)
    {
        var (module, world) = Asking("""
                    if World.GuildOf(who) == 1
                        Player? them = who.Find("Rival");
                        Guilds.WarDeath(who, them.Value());
                    end if
            """);

        var who = EntityHandle.ForPlayer(1);
        var rival = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(rival);
        world.Names[who] = "Ours";
        world.Names[rival] = "Rival";
        world.InGuild[who] = 1;
        world.InGuild[rival] = 2;
        world.GuildNames[1] = "Ours";
        world.GuildNames[2] = "Theirs";
        world.Vaults[1] = vault;

        Mutual(world);

        if (!weDeclared) world.SetGuildValue(1, "gw1we", AttributeValue.From(0L));

        world.HasOn[who] = [7];
        world.Wearing[(who, 7)] = (100, 100);

        return (module, world);
    }

    /// <summary>A mutual war between guilds 1 and 2, both meters full, as each side records it.</summary>
    private static void Mutual(ScriptedWorldTests.RecordingWorld world)
    {
        foreach (var (guild, other) in new[] { (1, 2), (2, 1) })
        {
            world.SetGuildValue(guild, "gw1op", AttributeValue.From((long)other));
            world.SetGuildValue(guild, "gw1we", AttributeValue.From(1L));
            world.SetGuildValue(guild, "gw1they", AttributeValue.From(1L));
            world.SetGuildValue(guild, "gw1live", AttributeValue.From(world.Clock - 1));
            world.SetGuildValue(guild, "gw1att", AttributeValue.From(1000L));
            world.SetGuildValue(guild, "gw1low", AttributeValue.From(1000L));
            world.SetGuildValue(guild, "gw1prog", AttributeValue.From(world.Clock));
            world.SetGuildValue(guild, "gw1mut", AttributeValue.From(world.Clock));
        }
    }

    /// <summary>One of a guild's own values.</summary>
    private static long Guild(ScriptedWorldTests.RecordingWorld world, int guild, string key) =>
        world.GuildValues(guild) is { } bag && bag.TryGet(key, out AttributeValue held) ? held.AsLong() : 0L;

    /// <summary>One text value off the body the double is keeping.</summary>
    private static string Text(ScriptedWorldTests.RecordingWorld world, string key) =>
        world.Bag.TryGet(key, out AttributeValue value) ? value.AsText() : "(nothing)";

    /// <summary>The original's own pool curve, so a test that computed it the port's way could not agree
    /// with the port by being wrong the same way.</summary>
    private static long Quadratic(int level, int stat, int fromClass)
    {
        double shifted = level + stat * 0.22 + fromClass + 15;
        return (long)Math.Round(shifted * shifted / 15.0 * 1.5, MidpointRounding.AwayFromZero);
    }

    // ── What a kill is worth to the people who made it ─────────────────────────
    //
    // 🔴 The engine rolls a creature's authored table; who shared the kill and whose the thing is are
    // this game's. Both are unreachable from a tick, so these ask the loot policy directly — which is
    // the same call the engine makes, line by line, before it rolls.

    private const int Coin = 7, Sword = 8;

    /// <summary>A creature dead on (5,5) with two people standing over it, the damage each of them dealt
    /// already on the body, and a world that knows which item is the coin.</summary>
    private (ScriptedWorldModule Module, ScriptedWorldTests.RecordingWorld World, Spoil Line) AKill(
        int itemNum, int quantity, int chance, long annDealt, long bobDealt)
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("""
                    who.Message("");
            """, world);

        var beast = EntityHandle.ForNpc(1, 1);
        var ann = EntityHandle.ForPlayer(1);
        var bob = EntityHandle.ForPlayer(2);

        world.Standing[new WorldPlace(1, 5, 5)] = beast;
        world.Standing[new WorldPlace(1, 5, 6)] = ann;
        world.Standing[new WorldPlace(1, 5, 7)] = bob;
        world.Names[ann] = "Ann";
        world.Names[bob] = "Bob";

        // The ledger the game keeps on the creature as it is hit, written here rather than swung for.
        world.BagFor(beast).Set("dealt.Ann", annDealt);
        world.BagFor(beast).Set("dealt.Bob", bobDealt);
        world.BagFor(ann);
        world.BagFor(bob);

        world.Records["Items"] =
        [
            Row(("name", "Rag")),
            .. Enumerable.Range(2, Coin - 2).Select(_ => Row(("name", "Rag"))),
            Row(("name", "Gold"), ("coin", true)),
            Row(("name", "Sword")),
        ];

        var line = new Spoil
        {
            Body = beast,
            Killer = ann,
            Kind = 1,
            ItemNum = itemNum,
            Quantity = quantity,
            ChancePercent = chance,
        };

        return (module, world, line);
    }

    /// <summary>🔴 <b>Coin alone divides on a table.</b> Everybody who fought for it
    /// takes a share, and every share is held for the person who earned it — otherwise a purse belongs
    /// to whoever is standing nearest when it lands.</summary>
    [Test]
    public void ThePurseIsSplitBetweenEverybodyWhoFoughtForIt()
    {
        var (module, world, line) = AKill(Coin, quantity: 10, chance: 100, annDealt: 100, bobDealt: 90);
        using ScriptedWorldModule scripts = module;

        ((ILootPolicy)scripts).Weigh(line);

        Assert.Multiple(() =>
        {
            Assert.That(line.ChancePercent, Is.Zero, "the line is the game's from here, so the engine stands down");
            Assert.That(world.Littered, Has.Count.EqualTo(2), "two who fought, two stacks");
            Assert.That(world.Littered.Sum(l => l.Quantity), Is.EqualTo(10),
                "nothing is created and nothing is destroyed");
            Assert.That(world.Littered.Select(l => l.ClaimedBy),
                Is.EquivalentTo(new[] { EntityHandle.ForPlayer(1), EntityHandle.ForPlayer(2) }),
                "and each share is held for whoever earned it");
        });
    }

    /// <summary>⚠ Somebody who chipped once and walked away is not somebody who shared the kill. The bar
    /// is three quarters of what the top dealer did, so nine tenths is in and a tenth is out.</summary>
    [Test]
    public void SomebodyWhoOnlyChippedAtIt_SharesNothing()
    {
        var (module, world, line) = AKill(Coin, quantity: 10, chance: 100, annDealt: 100, bobDealt: 10);
        using ScriptedWorldModule scripts = module;

        ((ILootPolicy)scripts).Weigh(line);

        Assert.Multiple(() =>
        {
            Assert.That(world.Littered, Has.Count.EqualTo(1));
            Assert.That(world.Littered[0].Quantity, Is.EqualTo(10), "the whole purse");
            Assert.That(world.Littered[0].ClaimedBy, Is.EqualTo(EntityHandle.ForPlayer(1)));
        });
    }

    /// <summary>🔴 <b>A creature another creature did most of the work on drops nothing.</b> Chipping a
    /// monster and letting a guard finish it is the cheapest exploit in the game and the one every player
    /// finds, so the answer is no loot at all rather than a smaller share.</summary>
    [Test]
    public void AKillAGuardFinished_PaysNobody()
    {
        var (module, world, line) = AKill(Sword, quantity: 1, chance: 100, annDealt: 40, bobDealt: 10);
        using ScriptedWorldModule scripts = module;

        world.BagFor(EntityHandle.ForNpc(1, 1)).Set("beastdealt", 90L);

        ((ILootPolicy)scripts).Weigh(line);

        Assert.Multiple(() =>
        {
            Assert.That(line.ChancePercent, Is.Zero);
            Assert.That(world.Littered, Is.Empty);
        });
    }

    /// <summary>Nobody laid a hand on it, so whatever killed it was not a person and the table is the
    /// world's own business — the engine rolls it as authored.</summary>
    [Test]
    public void ACreatureNobodyFought_IsLeftToTheEngine()
    {
        var (module, world, line) = AKill(Sword, quantity: 1, chance: 60, annDealt: 0, bobDealt: 0);
        using ScriptedWorldModule scripts = module;

        ((ILootPolicy)scripts).Weigh(line);

        Assert.Multiple(() =>
        {
            Assert.That(line.ChancePercent, Is.EqualTo(60), "left exactly as it was authored");
            Assert.That(world.Littered, Is.Empty);
        });
    }

    /// <summary>Anything that is not coin goes to one of them, whole. A sword cannot be halved, so it is
    /// drawn for rather than divided.</summary>
    [Test]
    public void AnythingElseGoesToOneOfThem()
    {
        var (module, world, line) = AKill(Sword, quantity: 1, chance: 100, annDealt: 100, bobDealt: 90);
        using ScriptedWorldModule scripts = module;

        ((ILootPolicy)scripts).Weigh(line);

        Assert.Multiple(() =>
        {
            Assert.That(world.Littered, Has.Count.EqualTo(1));
            Assert.That(world.Littered[0].ItemNum, Is.EqualTo(Sword));
            Assert.That(world.Littered[0].ClaimedBy.IsPlayer, Is.True, "one of the two, held for them");
            Assert.That(world.Littered[0].ClaimSeconds, Is.GreaterThan(0));
        });
    }

    // ── After dark ───────────────────────────────────────────────────────────
    //
    // 🔴 The original boosts creatures at night on three counts at once, and every one of them is a
    // bare multiplier that looks like nothing in a diff. Damage ×1.10, effective health ×1.10, and the
    // experience a kill pays ×1.20. A port that dropped one of the three would play almost right, and
    // the difference would show up as a world that feels the same at every hour.

    /// <summary>A creature hits harder after dark, and by the original's exact factor.</summary>
    [TestCase("day", 100, ExpectedResult = "100")]
    [TestCase("dusk", 100, ExpectedResult = "100")]
    [TestCase("dawn", 100, ExpectedResult = "100")]
    [TestCase("night", 100, ExpectedResult = "110")]
    [TestCase("night", 1, ExpectedResult = "1")]
    [TestCase("night", 5, ExpectedResult = "6")]
    public string ACreatureHitsHarderAtNight(string hour, int damage) =>
        Asks(new ScriptedWorldTests.RecordingWorld { Hour = hour }, $"Combat.Nightly({damage})");

    /// <summary>⚠ And hits a PERSON softer than it hits anything else, which is the disfavor the
    /// original applies to the NPC-versus-player path alone. The two stack, in that order.</summary>
    [TestCase(100, ExpectedResult = "70")]
    [TestCase(1, ExpectedResult = "1")]
    [TestCase(0, ExpectedResult = "0")]
    public string ACreatureLaysASofterHandOnAPerson(int damage) => Answered($"Combat.Softened({damage})");

    [Test]
    public void TheDisfavorAndTheHourStack()
    {
        var world = new ScriptedWorldTests.RecordingWorld { Hour = "night" };

        Assert.That(Asks(world, "Combat.Nightly(Combat.Softened(100))"), Is.EqualTo("77"));
    }

    /// <summary>Foul weather pays, and the gale pays most. Clear weather pays nothing extra.</summary>
    [TestCase("clear", ExpectedResult = "100")]
    [TestCase("rain", ExpectedResult = "105")]
    [TestCase("heatwave", ExpectedResult = "115")]
    [TestCase("snow", ExpectedResult = "115")]
    [TestCase("heavywind", ExpectedResult = "125")]
    public string TheSkyPaysForAKill(string weather) =>
        Asks(new ScriptedWorldTests.RecordingWorld { Weather = weather },
             "Math.Round(Combat.SkyWorth(1) * 100)");

    /// <summary>And the hour pays on top of the sky, multiplicatively: a kill made in a gale after dark
    /// is worth both.</summary>
    [TestCase("day", "clear", 100, ExpectedResult = "100")]
    [TestCase("night", "clear", 100, ExpectedResult = "120")]
    [TestCase("day", "heavywind", 100, ExpectedResult = "125")]
    [TestCase("night", "heavywind", 100, ExpectedResult = "150")]
    public string AKillIsWorthTheHourAndTheSkyTogether(string hour, string weather, int earned) =>
        Asks(new ScriptedWorldTests.RecordingWorld { Hour = hour, Weather = weather },
             $"Combat.Worth({earned}, 1)");

    /// <summary>⚠ A gale doubles every beat as well — a swing, a cast, and the drink between them — so
    /// the sky slows a fight as well as making it less certain.</summary>
    [TestCase("clear", 1, ExpectedResult = "1")]
    [TestCase("rain", 1, ExpectedResult = "1")]
    [TestCase("heavywind", 1, ExpectedResult = "2")]
    [TestCase("heavywind", 3, ExpectedResult = "6")]
    public string AGaleDoublesEveryBeat(string weather, int seconds) =>
        Asks(new ScriptedWorldTests.RecordingWorld { Weather = weather },
             $"Combat.Beat({seconds}, 1)");

    // ── A murderer's minute ──────────────────────────────────────────────────

    /// <summary>🔴 <b>A murderer who has just died is treated as though they were not one.</b> They come
    /// back on open ground wearing a flag anybody may swing at, and without the minute they are killed
    /// again where they land, for as long as somebody keeps waiting there. This is the only place
    /// that reads the flag differently, so nothing else asks IsMurderer whether the world is hunting
    /// somebody.</summary>
    [Test]
    public void AMurderersMinuteHidesTheFlagWithoutClearingIt()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("""
                    Death.MarkMurderer(who);
                    who.Message("" + Death.IsMurderer(who));
                    who.Message("" + Death.IsHunted(who));
                    Death.BeginGrace(who);
                    who.Message("" + Death.IsMurderer(who));
                    who.Message("" + Death.IsHunted(who));
                    Death.BreakGrace(who);
                    who.Message("" + Death.IsHunted(who));
            """, world);
        using ScriptedWorldModule scripts = module;

        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Said[^7..], Is.EqualTo(new[]
        {
            "true", "true",
            "You have a minute before the world comes for you again.",
            "true", "false",
            "Your minute is up.",
            "true",
        }), "flagged and hunted, then flagged and not hunted, then hunted again");
    }

    /// <summary>The whole minute, as the original counted it.</summary>
    [Test]
    public void TheMinuteIsAMinute() => Assert.That(Answered("Death.GraceSeconds"), Is.EqualTo("60"));

    // ── Something to drink ───────────────────────────────────────────────────

    /// <summary>🔴 A draught puts a pool back, never past its ceiling, and a NEGATIVE amount takes
    /// instead — which is the original's other half, and one item rather than two rules.
    ///
    /// <para>⚠ One that would do nothing is REFUSED rather than drunk. Core paces a consumable by the
    /// item leaving the bag, so a full bar costs neither the draught nor the beat.</para></summary>
    [Test]
    public void ADraughtFillsAPoolAndStopsAtTheCeiling()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Vitals.Drink(who, 1);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Items"] = [Row(("restoresHealth", 50L), ("restoresMana", 0L), ("restoresStamina", 0L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        world.SetAttribute(who, "hp", AttributeValue.From(1L));
        long ceiling = Held(world, "maxhp");

        ((ITickWork)scripts).Tick(1);
        long afterOne = Held(world, "hp");

        ((ITickWork)scripts).Tick(1);
        ((ITickWork)scripts).Tick(1);

        Assert.Multiple(() =>
        {
            Assert.That(afterOne, Is.EqualTo(Math.Min(51L, ceiling)));
            Assert.That(Held(world, "hp"), Is.EqualTo(ceiling), "and never past what the body can hold");
            Assert.That(world.Taken.Where(t => t.Item == 1), Is.Not.Empty, "a draught that worked is spent");
        });
    }

    /// <summary>An item with nothing on the three fields is not a draught, so drinking it does nothing
    /// and costs nothing.</summary>
    [Test]
    public void SomethingThatIsNotADraughtIsLeftAlone()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Vitals.Drink(who, 1);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Items"] = [Row(("restoresHealth", 0L), ("restoresMana", 0L), ("restoresStamina", 0L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Taken.Where(t => t.Item == 1), Is.Empty);
    }

    /// <summary>A pool already full refuses, so the draught stays in the bag.</summary>
    [Test]
    public void AFullPoolRefusesTheDraught()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Vitals.Drink(who, 1);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        world.Records["Items"] = [Row(("restoresHealth", 50L), ("restoresMana", 0L), ("restoresStamina", 0L))];

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Taken.Where(t => t.Item == 1), Is.Empty,
            "enrollment left every pool full, so there was nothing for it to do");
    }

    // ── A creature that fights at range ──────────────────────────────────────
    //
    // 🔴 The original's casters backed out of melee and threw from the gap they had made. The engine
    // keeps the gap — a body authored to keep its distance closes to its standoff and holds — and this
    // covers what it does once it is standing where it meant to stand.

    /// <summary>Which of the two a creature does is read off the RECORD, not off the body: every copy
    /// of that creature fights the same way, and one that decided at spawn would be an archer by
    /// accident.</summary>
    [Test]
    public void ABodyThatKeepsItsDistanceLoosesInsteadOfSwinging()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Fight.Meet(World.NpcAt(1, 5, 5).Value(), who);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var archer = EntityHandle.ForNpc(1, 1);
        world.Here.Add(who);
        world.Here.Add(archer);
        world.Standing[new WorldPlace(1, 5, 5)] = archer;
        world.BagFor(archer);
        world.Authored[archer] = ("shadow", 0, 8);
        world.SetAttribute(archer, "int", AttributeValue.From(30L));
        world.SetAttribute(archer, "name", AttributeValue.From("Archer"));

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Shown.Where(e => e.Item2.StartsWith("throw")), Is.Not.Empty,
            "a bolt, rather than a swing");
    }

    /// <summary>And a pursuer still swings, which is the whole difference between the two.</summary>
    [Test]
    public void APursuerStillSwings()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Fight.Meet(World.NpcAt(1, 5, 5).Value(), who);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var wolf = EntityHandle.ForNpc(1, 1);
        world.Here.Add(who);
        world.Here.Add(wolf);
        world.Standing[new WorldPlace(1, 5, 5)] = wolf;
        world.BagFor(wolf);
        world.Authored[wolf] = ("pursue", 0, 8);
        world.SetAttribute(wolf, "str", AttributeValue.From(30L));
        world.SetAttribute(wolf, "name", AttributeValue.From("Wolf"));

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Shown.Where(e => e.Item2.StartsWith("throw")), Is.Empty);
    }

    /// <summary>⚠ A body authored to hold off and given no mind stands there doing nothing. That is a
    /// heckler, not a mistake, so it must cost no bolt and no cooldown.</summary>
    [Test]
    public void ABodyWithNoMindLoosesNothing()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        Fight.Meet(World.NpcAt(1, 5, 5).Value(), who);", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var heckler = EntityHandle.ForNpc(1, 1);
        world.Here.Add(who);
        world.Here.Add(heckler);
        world.Standing[new WorldPlace(1, 5, 5)] = heckler;
        world.BagFor(heckler);
        world.Authored[heckler] = ("shadow", 0, 8);
        world.SetAttribute(heckler, "name", AttributeValue.From("Heckler"));

        ((IWorldObserver)scripts).OnPlayerJoined(who);
        ((ITickWork)scripts).Tick(1);

        Assert.That(world.Shown.Where(e => e.Item2.StartsWith("throw")), Is.Empty);
    }

    /// <summary>A bolt is slower than a swing, because the body is shaping the fight rather than in
    /// it.</summary>
    [Test]
    public void ABoltIsSlowerThanASwing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Answered("Fight.LooseSeconds"), Is.EqualTo("2"));
            Assert.That(Answered("Fight.SwingSeconds"), Is.EqualTo("1"));
        });
    }

    /// <summary>The name Core gives the behavior that closes to a distance and keeps it. Read off the
    /// engine rather than guessed, because a misspelling here is a creature that quietly never
    /// fires.</summary>
    [Test]
    public void TheGameKnowsWhatCoreCallsIt() =>
        Assert.That(Answered("Beasts.KeepsDistance"), Is.EqualTo("shadow"));

    /// <summary>The repeat fire, without which a caster is not a caster.
    ///
    /// <para>Contact is raised once, on the beat it reaches the distance it wanted. Everything after
    /// that comes off the world tick: a sweep of every creature that keeps its distance, each loosing
    /// at whatever the engine says it is after. Without the sweep a caster throws one bolt and stands
    /// there.</para>
    /// </summary>
    [Test]
    public void ACasterKeepsLoosingOffTheWorldTick()
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        yield;", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var archer = EntityHandle.ForNpc(1, 1);
        world.Here.Add(who);
        world.Here.Add(archer);
        world.Standing[new WorldPlace(1, 9, 5)] = archer;
        world.BagFor(archer);
        world.Authored[archer] = ("shadow", 0, 8);
        world.SetAttribute(archer, "int", AttributeValue.From(22L));
        world.SetAttribute(archer, "name", AttributeValue.From("Adept"));

        // The sweep walks every map, so there has to be one to walk.
        world.Records["Maps"] = [Row(("name", "East Walk"))];

        // What the engine says it is after. The rule reads this rather than remembering a target.
        world.Chasing[archer] = who;

        ((IWorldObserver)scripts).OnPlayerJoined(who);

        int before = world.Shown.Count(e => e.Item2.StartsWith("throw"));
        for (int tick = 1; tick <= 6; tick++) ((ITickWork)scripts).Tick(tick);
        int after = world.Shown.Count(e => e.Item2.StartsWith("throw"));

        Assert.That(after - before, Is.GreaterThan(0), "the world tick has to keep it firing");
    }

    /// <summary>A body comes back whole, and comes back whole when it GETS UP.
    ///
    /// <para>The original set all three pools to their ceiling in RespawnPlayer, which runs when the
    /// player presses the button — not when they fell. Filling them at the moment of death instead puts
    /// a full bar over a corpse, and it is the ordering rather than the fill that this pins.</para>
    ///
    /// <para>Without the fill a player who died walked around on nothing: the bar read 0 out of 29 and
    /// stayed there, which is not a state the game has any other way to be in — every rule that reads
    /// health treats nought as dead, and nothing sweeps for a body sitting at it.</para>
    ///
    /// <para>Checked on the ordinary death and on the one that costs nothing, because what a death took
    /// must not decide whether the body is put back together.</para>
    /// </summary>
    [TestCase(1, Description = "too low a level to lose anything")]
    [TestCase(20, Description = "an ordinary death, with a cost")]
    public void GettingUpPutsTheBodyBackTogether(int level)
    {
        var world = new ScriptedWorldTests.RecordingWorld();
        var (module, _) = Asking("        yield;", world);
        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);
        ((IWorldObserver)scripts).OnPlayerJoined(who);

        world.SetAttribute(who, "level", AttributeValue.From((long)level));
        foreach (string key in (string[])["hp", "mp", "sp"])
            world.SetAttribute(who, key, AttributeValue.From(0L));

        ((IDeathPolicy)scripts).OnDied(new Death(who, EntityHandle.None, "slain"));

        Assert.That(Held(world, "hp"), Is.Zero, "a body on the floor was handed a full bar");

        ((IDeathPolicy)scripts).OnRose(who);

        Assert.Multiple(() =>
        {
            Assert.That(Held(world, "hp"), Is.EqualTo(Held(world, "maxhp")), "health");
            Assert.That(Held(world, "mp"), Is.EqualTo(Held(world, "maxmp")), "mana");
            Assert.That(Held(world, "sp"), Is.EqualTo(Held(world, "maxsp")), "stamina");
            Assert.That(Held(world, "hp"), Is.GreaterThan(0), "and not full of nothing");
        });
    }

    private static AttributeBag Row(params (string Key, object Value)[] fields)



    {
        var bag = new AttributeBag();
        foreach (var (key, value) in fields)
        {
            bag.Set(key, value switch
            {
                long number => AttributeValue.From(number),
                bool ticked => AttributeValue.From(ticked),
                // A set is already one of these, so it is passed through rather than made again.
                AttributeValue made => made,
                _ => AttributeValue.From((string)value),
            });
        }

        return bag;
    }
}
