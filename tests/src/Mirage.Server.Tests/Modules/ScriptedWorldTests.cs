using System.Reflection;
using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// A world's own rules, running.
///
/// <para>🔴 <b>This is the route the engine exists for, and the one every other seam is shaped around.</b>
/// Everything else needs C#, a build and a redeploy; a world carrying a <c>scripts/</c> folder needs an
/// edit and a restart. These pin the whole path — that the folder is found, that what it declares is
/// called, that what it says reaches a player, and that a world whose rules are broken still runs.</para>
/// </summary>
[TestFixture]
public class ScriptedWorldTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() =>
        _dir = Directory.CreateTempSubdirectory("mirage-scripted-").FullName;

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private ScriptedWorldModule Loaded(string rules, RecordingWorld? world = null) =>
        Built(rules, world).Module;

    /// <summary>
    /// The module through the path a server takes: configured, which is where a script declares, then
    /// started, which is where the world arrives. The registry is what the declarations landed in.
    /// </summary>
    private (ScriptedWorldModule Module, CoreRegistry Registry) Built(
        string rules, RecordingWorld? world = null)
    {
        string scripts = Path.Combine(_dir, ScriptedWorldModule.ScriptsFolder);
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "rules.cm"), rules);

        var module = new ScriptedWorldModule(_dir);
        var registry = CoreRegistry.Build(module);

        module.Start(world ?? new RecordingWorld());

        return (module, registry);
    }

    private static EntityHandle Someone => EntityHandle.ForPlayer(1);

    // ── The whole path ────────────────────────────────────────────────────────

    /// <summary>🔴 A rule in a folder, reaching a player. Everything else here is a variation on it.</summary>
    [Test]
    public void AScriptSaysSomethingToAPlayerWhoJoins()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Message("Welcome to the isles.");
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.True, string.Join("; ", module.Problems));
            Assert.That(world.Said, Is.EqualTo(new[] { "Welcome to the isles." }));
        });
    }

    [Test]
    public void AMoveHandlerIsToldWhereThePlayerCameFrom()
    {
        var world = new RecordingWorld { Place = new WorldPlace(1, 7, 9) };
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerMoved(Player who, integer fromX, integer fromY)
                    who.Message("from " + fromX + "," + fromY + " to " + who.X + "," + who.Y);
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerMoved(Someone, new WorldPlace(1, 6, 9), new WorldPlace(1, 7, 9));

        Assert.That(world.Said, Is.EqualTo(new[] { "from 6,9 to 7,9" }));
    }

    [Test]
    public void ATickHandlerRunsOnTheTick()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                integer ticks = 0;

                public function OnTick()
                    Rules.ticks = Rules.ticks + 1;
                end function

                public function OnPlayerJoined(Player who)
                    who.Message("ticks: " + Rules.ticks);
                end function
            end model
            """, world);

        ((ITickWork)module).Tick(1);
        ((ITickWork)module).Tick(2);
        ((ITickWork)module).Tick(3);
        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.That(world.Said, Is.EqualTo(new[] { "ticks: 3" }), "and the state survived between calls");
    }

    /// <summary>What a game counts lives in the attribute bag, and a script reaches it by name.</summary>
    [Test]
    public void AScriptReadsAndWritesWhatTheGameCounts()
    {
        var world = new RecordingWorld();
        world.Bag.Set("survey.stamina", AttributeValue.From(3L));

        using var module = Loaded("""
            shared model Rules
                public function OnPlayerMoved(Player who, integer fromX, integer fromY)
                    if who.Number("survey.stamina") <= 0
                        who.Message("You are too tired to go on.");
                    else
                        who.SetNumber("survey.stamina", who.Number("survey.stamina") - 1);
                    end if
                end function
            end model
            """, world);

        var moved = (IWorldObserver)module;
        for (int i = 0; i < 4; i++) moved.OnPlayerMoved(Someone, WorldPlace.Nowhere, WorldPlace.Nowhere);

        Assert.Multiple(() =>
        {
            Assert.That(world.Bag["survey.stamina"].AsLong(), Is.Zero);
            Assert.That(world.Said, Is.EqualTo(new[] { "You are too tired to go on." }));
        });
    }

    /// <summary>A key a body does not have reads as zero, with <c>Has</c> for the question that tells
    /// absence apart from nothing — a rule asking "how much" wants a number, not a decision.</summary>
    [Test]
    public void AKeyThatIsNotThereReadsAsNothingRatherThanFailing()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Message(who.Number("absent") + " / " + who.Has("absent"));
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.That(world.Said, Is.EqualTo(new[] { "0 / false" }));
    }

    // ── What a module does not have to write ──────────────────────────────────

    /// <summary>Every handler is optional, and a world declaring none is a world that runs.</summary>
    [Test]
    public void AModuleDeclaringNoHandlersIsStillAModule()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """, world);

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.True);
            Assert.DoesNotThrow(() => ((ITickWork)module).Tick(1));
            Assert.DoesNotThrow(() => ((IWorldObserver)module).OnPlayerJoined(Someone));
            Assert.That(world.Said, Is.Empty);
        });
    }

    [Test]
    public void AWorldWithNoScriptsFolderLoadsNothingAndDoesNothing()
    {
        using var module = new ScriptedWorldModule(_dir);
        CoreRegistry.Build(module);
        module.Start(new RecordingWorld());

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.False);
            Assert.That(module.Problems, Is.Empty);
            Assert.DoesNotThrow(() => ((ITickWork)module).Tick(1));
        });
    }

    // ── When a world's rules are wrong ────────────────────────────────────────

    /// <summary>
    /// 🔴 A server that refused to start because somebody's rules had a typo would be a server an
    /// operator cannot recover without an editor. The problems are reported and the game runs unscripted.
    /// </summary>
    [Test]
    public void RulesThatDoNotCompile_LeaveTheWorldRunningUnscripted()
    {
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Message(nonsense);
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.False);
            Assert.That(module.Problems, Is.Not.Empty, "and it says what was wrong");
            Assert.DoesNotThrow(() => ((IWorldObserver)module).OnPlayerJoined(Someone));
        });
    }

    /// <summary>
    /// 🔴 The sandbox, at the level it actually matters: a world folder is something one person hands to
    /// another, so rules that could read the accounts beside them never become a program at all.
    /// </summary>
    [Test]
    public void RulesThatReachTheMachine_AreRefusedAndTheWorldRunsUnscripted()
    {
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Message(File.Read("accounts.json").Or("nothing"));
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.False);
            Assert.That(module.Problems.Select(p => p.Id), Does.Contain("MS0003"));
        });
    }

    /// <summary>
    /// 🔴 A handler that raises runs in the middle of a join, a step or a tick. Throwing from there would
    /// end the event for everybody, and on the tick it would end the world.
    /// </summary>
    [Test]
    public void AHandlerThatRaises_DoesNotStopTheWorld()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                integer seen = 0;

                public function OnPlayerJoined(Player who)
                    Rules.seen = Rules.seen + 1;

                    if Rules.seen == 1
                        integer zero = 0;

                        who.Message("" + 1 / zero);
                    end if

                    who.Message("still here");
                end function
            end model
            """, world);

        var joined = (IWorldObserver)module;

        Assert.DoesNotThrow(() => joined.OnPlayerJoined(Someone));
        joined.OnPlayerJoined(Someone);

        Assert.That(world.Said, Is.EqualTo(new[] { "still here" }), "the second join ran to the end");
    }

    /// <summary>A script cannot name what the engine did not register, which is the other half of it.</summary>
    [Test]
    public void AScriptCannotReachATypeTheEngineDoesNotOffer()
    {
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    Server.Shutdown();
                end function
            end model
            """);

        Assert.That(module.IsLoaded, Is.False);
    }

    // ── Declaring ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 A script adds to the engine, rather than only reacting to it.
    ///
    /// <para>Reacting alone leaves a world able to change what happens and unable to change what the
    /// player SEES — no value of its own on the sidebar, no verb in a menu. Declaring is the half that
    /// makes a script a game rather than a set of triggers, and it is the same two phases a C# module
    /// has because what a module declares shapes the engine that is then built.</para>
    /// </summary>
    [Test]
    public void AScriptDeclaresWhatThePlayerSeesAndCanDo()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("harvest.baskets", "owner");
                    game.Heading("Harvest");
                    game.Field("harvest.baskets", "Baskets");
                    game.Meter("harvest.sap", "harvest.sapMax", "Sap");
                    game.Bar("harvest.sap", "harvest.sapMax", 65280);
                    game.Action("harvest.gather", "Gather here", "Harvest");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(registry.Attributes.TryGet("harvest.baskets", out var declared), Is.True);
            Assert.That(declared.Visibility, Is.EqualTo(AttributeVisibility.Owner));

            Assert.That(registry.DisplayFields.For(DisplaySurfaces.Hud).Select(f => f.LabelKey),
                Is.EqualTo(new[] { "Harvest", "Baskets", "Sap" }).AsCollection);

            Assert.That(registry.OverheadBars.Count, Is.EqualTo(1));
            Assert.That(registry.Actions.All.Select(a => a.Id), Does.Contain("harvest.gather"));
            Assert.That(module.Actions, Is.EqualTo(new[] { "harvest.gather" }));
        });
    }

    /// <summary>A handler is registered only for a script that declared something to handle.</summary>
    [Test]
    public void AScriptDeclaringNoVerbsIsNotRegisteredAsAHandler()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function OnTick()
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.That(registry.ActionHandlers, Is.Empty);
    }

    /// <summary>
    /// 🔴 The player picked the script's own verb, on a square the script is told about in full.
    ///
    /// <para>The square carries its MAP as well as its coordinates: everything inside the seamless view
    /// is pointable, so a handler given only x and y acts on the wrong tile the moment somebody stands
    /// near a border.</para>
    /// </summary>
    [Test]
    public void AScriptIsToldWhenThePlayerPicksItsVerb()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function Configure(Builder game)
                    game.Action("harvest.gather", "Gather here", "Harvest");
                end function

                public function OnAction(Player who, string action, integer map, integer x, integer y)
                    who.Message(action + " at " + map + ":" + x + "," + y);
                end function
            end model
            """, world);

        ((IActionHandler)module).Invoke(Someone, "harvest.gather", new WorldPlace(3, 11, 4));

        Assert.That(world.Said, Is.EqualTo(new[] { "harvest.gather at 3:11,4" }));
    }

    [Test]
    public void AVerbTheScriptDidNotDeclare_ReachesNothing()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function Configure(Builder game)
                    game.Action("harvest.gather", "Gather here", "Harvest");
                end function

                public function OnAction(Player who, string action, integer map, integer x, integer y)
                    who.Message("did " + action);
                end function
            end model
            """, world);

        Assert.That(((IActionHandler)module).Actions, Is.EqualTo(new[] { "harvest.gather" }),
            "the handler claims exactly what was declared, so the engine never routes anything else here");
    }

    // ── When a declaration cannot be made ─────────────────────────────────────

    /// <summary>
    /// 🔴 A world's content must not be able to stop the server.
    ///
    /// <para>Two modules claiming one attribute key is an error the engine raises at startup, which is
    /// right when both are assemblies somebody built and wrong when one of them is a folder a stranger
    /// handed over: the operator is left holding a server that will not start and a world they did not
    /// author. So the colliding declaration is refused on its own and the rest are still made.</para>
    ///
    /// <para>What it is not is silent. The engine's rule against last-one-wins is a rule against nobody
    /// being told, and the refusal names the declaration.</para>
    /// </summary>
    [Test]
    public void ADeclarationThatCollidesWithACompiledModule_IsRefusedRatherThanFatal()
    {
        string scripts = Path.Combine(_dir, ScriptedWorldModule.ScriptsFolder);
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "rules.cm"), """
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("taken.key", "owner");
                    game.Attribute("mine.key", "owner");
                end function
            end model
            """);

        using var module = new ScriptedWorldModule(_dir);
        CoreRegistry registry = null!;

        Assert.DoesNotThrow(() => registry = CoreRegistry.Build(new Claims("taken.key"), module),
            "a world's rules must not be able to stop a server from starting");

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.True, "and the rest of the module still runs");
            Assert.That(registry.Attributes.TryGet("mine.key", out _), Is.True, "and its other declarations landed");
            Assert.That(module.Problems.Select(p => p.Message),
                Has.Some.Contains("taken.key"), "and it says which one lost");
        });
    }

    /// <summary>
    /// An enum cannot cross the boundary — a script may name a registered type and nothing else — so the
    /// words are the vocabulary, and one that is not a word is refused with the list rather than falling
    /// back to a default nobody chose.
    /// </summary>
    [Test]
    public void AWordThatIsNotAVisibility_IsRefusedWithTheWordsThatAre()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("harvest.baskets", "public");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(registry.Attributes.TryGet("harvest.baskets", out _), Is.False);
            Assert.That(module.Problems.Select(p => p.Message), Has.Some.Contains("owner"),
                "and says what would have worked");
        });
    }

    /// <summary>
    /// 🔴 Declaring is a phase, and it ends. A script keeping the builder and declaring from a handler is
    /// declaring into an engine that has already been built around it, so it is told rather than
    /// silently changing nothing.
    /// </summary>
    [Test]
    public void DeclaringAfterConfigureHasReturned_IsRefused()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                Builder? kept = nothing;

                public function Configure(Builder game)
                    Rules.kept = game;
                end function

                public function OnPlayerJoined(Player who)
                    Rules.kept.Value().Attribute("sneaky.key", "owner");

                    who.Message("declared");
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.That(world.Said, Is.Empty, "the handler failed at the declaration rather than carrying on");
    }

    /// <summary>
    /// A script's <c>Configure</c> runs before the world does. Anything about a player there is told so,
    /// rather than meeting a null and taking the server's startup with it.
    /// </summary>
    [Test]
    public void AScriptReachingForTheWorldWhileDeclaring_IsToldRatherThanCrashing()
    {
        string scripts = Path.Combine(_dir, ScriptedWorldModule.ScriptsFolder);
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "rules.cm"), """
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("harvest.baskets", "owner");
                end function
            end model
            """);

        using var module = new ScriptedWorldModule(_dir);

        Assert.DoesNotThrow(() => CoreRegistry.Build(module));
        Assert.That(module.IsLoaded, Is.True);
    }

    // ── The world this repository ships ───────────────────────────────────────

    /// <summary>
    /// 🔴 The demo world's own rules compile against what the engine actually registers.
    ///
    /// <para>The scripts folder is CONTENT: no compiler sees it at build time, nothing links against it,
    /// and a typo in it is invisible until a server starts. It is also the worked example every author
    /// reads first, so an example that does not run teaches the wrong shape. This is the only thing that
    /// holds it to the catalog beside it.</para>
    /// </summary>
    [Test]
    public void TheShippedWorldsRulesCompile()
    {
        string root = typeof(ScriptedWorldTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

        string world = Path.Combine(root, "server", "src", "Mirage.Server.Host", "world");
        Assert.That(Directory.Exists(Path.Combine(world, ScriptedWorldModule.ScriptsFolder)), Is.True,
            "the shipped world carries no scripts folder — if it moved, teach this test where");

        using var module = new ScriptedWorldModule(world);
        CoreRegistry.Build(module);
        module.Start(new RecordingWorld());

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty,
                "the shipped rules do not compile:\n" + string.Join("\n", module.Problems));
            Assert.That(module.IsLoaded, Is.True);
        });
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>A compiled module that has already taken a key, so a script can collide with it.</summary>
    private sealed class Claims(string key) : ICoreModule
    {
        public string Name => "Claims";

        public void Configure(ICoreBuilder builder) =>
            builder.Attributes.Declare(key, AttributeVisibility.Owner);
    }

    /// <summary>
    /// Stands in for the engine, recording what a script asked it to do. The module is what is under
    /// test here; what <c>ServerWorld</c> does with a <c>Tell</c> is pinned by its own tests.
    /// </summary>
    private sealed class RecordingWorld : IWorld
    {
        public List<string> Said { get; } = [];
        public AttributeBag Bag { get; } = new();
        public WorldPlace Place { get; set; } = new(1, 0, 0);

        public void Tell(EntityHandle who, string text, ChatChannel channel, int color) => Said.Add(text);

        public bool IsInWorld(EntityHandle who) => who.IsPlayer;
        public WorldPlace PlaceOf(EntityHandle who) => Place;
        public AttributeBag? AttributesOf(EntityHandle who) => Bag;

        public bool SetAttribute(EntityHandle who, string key, AttributeValue value)
        {
            Bag.Set(key, value);
            return true;
        }

        public bool SetAttributes(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values)
        {
            foreach (var (key, value) in values) Bag.Set(key, value);
            return true;
        }

        public bool RemoveAttribute(EntityHandle who, string key) => Bag.Remove(key);
        public void SetEngaged(EntityHandle who, int seconds) { }
        public void SetDowned(EntityHandle who, int seconds) { }
        public void SetMarked(EntityHandle who, int seconds) { }
        public void SetAggressor(EntityHandle who, int seconds) { }
        public void SetActionCooldown(EntityHandle who, int seconds) { }
        public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "") => false;

        public bool Warp(EntityHandle who, WorldPlace to)
        {
            Place = to;
            return true;
        }

        public void Give(EntityHandle who, int itemNum, int quantity = 1) { }
        public void Take(EntityHandle who, int itemNum, int quantity = 1) { }
        public void ReleaseGhost(EntityHandle who) { }
        public void Stain(WorldPlace at, int size, WorldLayer layer, float amount) { }
        public IReadOnlyList<AttributeBag> RecordsOf(string familyId) => [];
        public AttributeBag? RecordAt(string familyId, int num) => null;
    }
}
