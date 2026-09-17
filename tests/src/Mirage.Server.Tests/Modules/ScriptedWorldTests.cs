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
    /// started, which is where the world arrives. The registry holds what the declarations landed in.
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

    /// <summary>🔴 <b>A store is a set of named bags nobody declared.</b> Writing into one makes it,
    /// so a rule can pile up a row per player without a family, a slot count or an editor form behind
    /// it. Walked by count and index, which is the loop every other script here already writes.</summary>
    [Test]
    public void AScriptKeepsItsOwnStoreAndWalksIt()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    World.SetKeptNumber("ladder", "rowan", "kills", 7);
                    World.SetKeptNumber("ladder", "auden", "kills", 4);
                    World.SetKeptText("ladder", "rowan", "guild", "Ironhelm");

                    string said = "";

                    loop for i = 1 to World.KeptCount("ladder")
                        string name = World.KeptKeyAt("ladder", i);
                        said = said + name + "=" + World.KeptNumber("ladder", name, "kills") + " ";
                    end loop

                    who.Message(said + "| rowan is in " + World.KeptText("ladder", "rowan", "guild"));
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(world.Said, Is.EqualTo(new[] { "auden=4 rowan=7 | rowan is in Ironhelm" }),
                        "walked in key order, and both fields of a row survive together");
            Assert.That(world.Stores["ladder"], Has.Count.EqualTo(2));
        });
    }

    /// <summary>A key that was never written and one that was dropped read the same as each other, and
    /// HasKept tells a rule which it is looking at.</summary>
    [Test]
    public void AForgottenKeyIsGoneAndSaysSo()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    World.SetKeptNumber("ladder", "rowan", "kills", 7);
                    World.Forget("ladder", "rowan");

                    who.Message("held: " + World.HasKept("ladder", "rowan")
                                + " kills: " + World.KeptNumber("ladder", "rowan", "kills")
                                + " left: " + World.KeptCount("ladder")
                                + " never: " + World.HasKept("ladder", "nobody"));
                end function
            end model
            """, world);

        ((IWorldObserver)module).OnPlayerJoined(Someone);

        Assert.That(world.Said, Is.EqualTo(new[] { "held: false kills: 0 left: 0 never: false" }));
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
                    game.Field("harvest.baskets", "Baskets", 0, 0, 0);
                    game.Meter("harvest.sap", "harvest.sapMax", "Sap", 0, 0, 0);
                    game.Bar("harvest.sap", "harvest.sapMax", 0, 255, 0);
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

            // The half a count does not check: three channels have to arrive packed the way the wire
            // reads them, and nothing between here and the client would notice if they did not.
            Assert.That(registry.OverheadBars.Bars[0].Rgb, Is.EqualTo(0x00FF00));
            Assert.That(registry.Actions.All.Select(a => a.Id), Does.Contain("harvest.gather"));
            Assert.That(module.Actions, Is.EqualTo(new[] { "harvest.gather" }));
        });
    }

    /// <summary>A world's own rules may bind a key, and a key nobody may bind is refused on its own.
    ///
    /// <para>Refused on its own rather than stopping the server: a world is a folder somebody hands
    /// somebody else, and an operator should not be left with a server that will not start over a
    /// stranger's typo. The rest of the declarations are still made.</para></summary>
    [Test]
    public void AScriptBindsAKey_AndIsRefusedOneItMayNotHave()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb g = game.Action("harvest.gather", "Gather here", "Harvest");
                    g.Key("Q");
                    Verb r = game.Action("harvest.run", "Run", "Harvest");
                    r.Key("W");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(registry.Actions.All.Single().Id, Is.EqualTo("harvest.gather"));
            Assert.That(registry.Actions.All.Single().Key, Is.EqualTo("Q"));
        });
    }

    // ── A game's own records ──────────────────────────────────────────────────

    /// <summary>🔴 The seam the whole scripted route was missing: a world's rules declaring a kind of
    /// record, and getting an editor section for it without a compiler.
    ///
    /// <para>The MODEL is the declaration. Its fields, in order, in the shapes they hold, are the rows
    /// of the form — a field typed as an enumeration is a drop-down over that enumeration's members,
    /// and one typed as another model is a picker over that model's records. What is left over is
    /// what a model cannot say, and the model says it in <c>Describe</c>.</para>
    /// </summary>
    [Test]
    public void AScriptDeclaresItsOwnRecords()
    {
        var (module, registry) = Built("""
            enumeration Habitat
                Shore, Woodland
            end enumeration

            model Species
                public string commonName;
                public Habitat habitat;
                public integer height;
                public boolean protection;
                public real spread;
                public Species nearest;

                public shared function Describe(Records these)
                    these.Are("Species", "Species", 200);

                    these.Caption("commonName", "Common name");
                    these.Length("commonName", 60);
                    these.Range("height", 0, 400);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var family = registry.Schema.Families.Single(f => f.Id == "Species");
        var habitats = registry.Schema.Choices("Species.habitat")!;

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(family.LabelKey, Is.EqualTo("Species"));
            Assert.That(family.DefaultLimit, Is.EqualTo(200));

            // Declaration order, because that is the order the form is drawn in.
            Assert.That(family.Fields.Select(f => f.Key),
                Is.EqualTo(new[] { "commonName", "habitat", "height", "protection", "spread", "nearest" })
                  .AsCollection);

            Assert.That(family.Fields.Select(f => f.Kind), Is.EqualTo(new[]
            {
                FieldKind.Text, FieldKind.Choice, FieldKind.Integer,
                FieldKind.Flag, FieldKind.Real, FieldKind.RecordRef,
            }).AsCollection);

            // A field says what it is called unless the script says otherwise.
            Assert.That(family.Fields[0].LabelKey, Is.EqualTo("Common name"), "the caption given");
            Assert.That(family.Fields[2].LabelKey, Is.EqualTo("Height"), "and the one nobody gave");

            // The parts a kind alone does not carry, and which a form is useless without.
            Assert.That(family.Fields[0].MaxLength, Is.EqualTo(60));
            Assert.That(family.Fields[1].ChoiceSetId, Is.EqualTo("Species.habitat"));
            Assert.That(family.Fields[2].Max, Is.EqualTo(400));
            Assert.That(family.Fields[5].RecordFamilyId, Is.EqualTo("Species"),
                "a field typed as a model points at that model's records, unprompted");

            // The enumeration's members ARE the set, and nothing declared one.
            Assert.That(habitats.Members.Select(m => m.Id),
                Is.EqualTo(new[] { "Shore", "Woodland" }).AsCollection);
            Assert.That(habitats.Members[0].LabelKey, Is.EqualTo("Shore"));
        });
    }

    /// <summary>A model the script says nothing else about still gets a section, named after itself.
    /// An empty <c>Describe</c> is the whole of what a game has to write to author a kind of record.</summary>
    [Test]
    public void RecordsNothingElseIsSaidAbout_StillGetASection()
    {
        var (module, registry) = Built("""
            model SurveySite
                public string siteName;

                public shared function Describe(Records these)
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var family = registry.Schema.Families.Single(f => f.Id == "SurveySite");

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);
            Assert.That(family.LabelKey, Is.EqualTo("Survey site"), "the model's own name, as words");
            Assert.That(family.Fields.Single().LabelKey, Is.EqualTo("Site name"));
        });
    }

    /// <summary>🔴 No line depends on the one above it.
    ///
    /// <para>Every line names the field it is about, and the model it is written in is the records it
    /// is about, so a file may be written in whatever order reads best and mean the same thing. An
    /// order that quietly decided which records a line landed on would be invisible: both forms would
    /// render, one short a bound and one carrying a bound that makes no sense on it.</para></summary>
    [Test]
    public void NoLineDependsOnTheOneAboveIt()
    {
        var (module, registry) = Built("""
            model Species
                public integer height;

                public shared function Describe(Records these)
                    these.Range("height", 0, 400);
                    these.Are("Species", "Species", 50);
                end function
            end model

            model Site
                public integer visits;

                public shared function Describe(Records these)
                    these.Are("Sites", "Site", 20);
                    these.Range("visits", 0, 9);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(registry.Schema.Families.Single(f => f.Id == "Site")
                                .Fields.Single(f => f.Key == "visits").Max, Is.EqualTo(9));
            Assert.That(registry.Schema.Families.Single(f => f.Id == "Species")
                                .Fields.Single(f => f.Key == "height").Max, Is.EqualTo(400));
        });
    }

    /// <summary>A field nothing can edit, and a name a model has not got, are each refused BY NAME.
    ///
    /// <para>Compass has no type values, so a field arrives as text and a typo cannot be a compile
    /// error. It has to be a message that says which fields DO exist — otherwise the row is simply
    /// missing from the form, which reads as a broken engine rather than a misspelled word.</para></summary>
    [Test]
    public void AFieldNothingCanEdit_AndOneTheModelHasNotGot_AreRefusedByName()
    {
        var (module, registry) = Built("""
            model Species
                public string commonName;
                public string[] tags;

                public shared function Describe(Records these)
                    these.Caption("commonNmae", "Common name");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("tags"), "a field no form could edit");
            Assert.That(refused, Does.Contain("commonNmae"), "and a field the model has not got");

            // And the rest of the file still declared, which is the whole rule for a world's content.
            Assert.That(registry.Schema.Families.Single(f => f.Id == "Species").Fields.Select(f => f.Key),
                Is.EqualTo(new[] { "commonName" }).AsCollection,
                "the field nobody could edit is left out rather than taking the records with it");
        });
    }

    /// <summary>🔴 A <c>Describe</c> that is not <c>shared</c> is refused BY NAME.
    ///
    /// <para>It compiles, it loads, and the engine has no record to hand it, so it is never called —
    /// which from inside the script looks exactly like working code and produces an editor with no
    /// section for records the author has already written fields for.</para></summary>
    [Test]
    public void ADescribeThatIsNotShared_IsRefusedByName()
    {
        var (module, registry) = Built("""
            model Species
                public string commonName;

                public function Describe(Records these)
                    these.Are("Species", "Species", 5);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("Species"), "the records nobody will see");
            Assert.That(refused, Does.Contain("shared"), "and the one word that fixes it");

            Assert.That(registry.Schema.Families.Any(f => f.Id == "Species"), Is.False,
                "nothing half-declared: neither the section nor the reason for it is there");
        });
    }

    /// <summary>🔴 A field typed as a model nobody declared records for is dropped and NAMED.
    ///
    /// <para>The picker would otherwise list nothing, which reads as a game holding no records rather
    /// than as a <c>Describe</c> somebody forgot to write on the model being pointed at. Both halves
    /// have to be there and only one of them is in the type.</para></summary>
    [Test]
    public void AFieldPointingAtRecordsNobodyDeclared_IsDroppedAndNamed()
    {
        var (module, registry) = Built("""
            model Habitat
                public string name;
            end model

            model Species
                public string commonName;
                public Habitat home;

                public shared function Describe(Records these)
                    these.Are("Species", "Species", 5);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("Species.home"), "the field that points nowhere");
            Assert.That(refused, Does.Contain("Habitat"), "and what it was pointing at");

            Assert.That(registry.Schema.Families.Single(f => f.Id == "Species").Fields.Select(f => f.Key),
                Is.EqualTo(new[] { "commonName" }).AsCollection,
                "the rest of the form still renders");
        });
    }

    /// <summary>A model named for something the compiled game already declared is refused on its own,
    /// like every other declaration a script makes — and saying more about it afterwards is quiet,
    /// because the one line about what went wrong is the useful one.</summary>
    [Test]
    public void RecordsCollidingWithTheCompiledGame_AreRefusedAlone()
    {
        var (module, registry) = Built("""
            model Items
                public string name;

                public shared function Describe(Records these)
                    these.Are("Things", "Thing", 10);
                    these.Caption("name", "Name");
                end function
            end model

            model Mine
                public string name;

                public shared function Describe(Records these)
                    these.Are("Mine", "Mine", 10);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(string.Join("\n", module.Problems.Select(p => p.Message)), Does.Contain("Items"));
            Assert.That(registry.Schema.Families.Any(f => f.Id == "Mine"), Is.True,
                "the declaration after the refused one still lands");
        });
    }

    // ── Panels, slots, and what a verb is offered on ────────────────────────────────

    /// <summary>A screen of a game's own, its rows, its buttons, and the verb that opens it.
    ///
    /// <para>The panel's SURFACE is derived from its id rather than named, so a row cannot land on a
    /// surface nothing draws — which would take, render nowhere, and say nothing.</para></summary>
    [Test]
    public void AScriptDeclaresAPanel_ItsRows_AndTheVerbThatOpensIt()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("harvest.baskets", "owner");
                    game.Attribute("harvest.sap", "viewport");
                    game.Attribute("harvest.sapMax", "viewport");

                    Panel book = game.Panel("harvest.book", "Field Book", 240, 180);
                    book.Key("B");
                    book.Button("Gather here", "harvest.gather");
                    book.Heading("Field record");
                    book.Field("harvest.baskets", "Baskets", 200, 200, 160);
                    book.Meter("harvest.sap", "harvest.sapMax", "Sap", 0, 0, 0);

                    Verb o = game.Action("harvest.open", "Field Book", "Harvest");
                    o.OnHud();
                    o.Opens("harvest.book");
                    Verb g = game.Action("harvest.gather", "Gather here", "Harvest");
                    g.Key("Q");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var panel = registry.Panels.All.Single(p => p.Id == "harvest.book");
        var rows = registry.DisplayFields.For(panel.Surface);
        var opener = registry.Actions.All.Single(a => a.Id == "harvest.open");

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(panel.TitleKey, Is.EqualTo("Field Book"));
            Assert.That(panel.Key, Is.EqualTo("B"));
            Assert.That(panel.Width, Is.EqualTo(240));
            Assert.That(panel.Buttons.Single().ActionId, Is.EqualTo("harvest.gather"));

            Assert.That(rows.Select(r => r.LabelKey),
                Is.EqualTo(new[] { "Field record", "Baskets", "Sap" }).AsCollection,
                "the panel's rows are on the panel's own surface, and in the order written");
            Assert.That(rows.Single(r => r.LabelKey == "Baskets").Rgb,
                Is.EqualTo(GameColor.Pack(200, 200, 160)), "a color said after the row still lands");

            Assert.That(opener.Surface, Is.EqualTo(ActionSurface.Hud));
            Assert.That(opener.OpensPanel, Is.EqualTo("harvest.book"));
        });
    }


    /// <summary>🔴 A panel's list, and the pick that travels with the button under it.
    ///
    /// <para>A verb that acts on one of several things needs a way to say WHICH. Without a list, a
    /// screen about a set of anything - wars to retract, offers to accept - has to ask for a name in a
    /// box, which means reading one off the screen above and typing it back in.</para>
    ///
    /// <para>The two halves fail differently and both fail quietly. Rows declared and never carried give
    /// a screen with nothing to choose from; a pick carried and never read gives a button that always
    /// acts on whatever the game guesses. So this pins the declaration AND the delivery.</para></summary>
    [Test]
    public void APanelDeclaresAList_AndThePickReachesTheHandler()
    {
        var world = new RecordingWorld();
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("war.line1", "owner");
                    game.Attribute("war.of1", "owner");

                    Panel wars = game.Panel("guild.wars", "Wars", 240, 200);
                    wars.Row("war.line1", "war.of1");
                    wars.Button("Retract", "guild.retract");

                    Verb r = game.Action("guild.retract", "Retract", "Guild");
                    r.Nowhere();
                end function

                public function OnAction(Player who, string action, string on, integer map,
                                         integer x, integer y, string picked)
                    who.Message("retracting against " + picked);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        var panel = registry.Panels.All.Single(p => p.Id == "guild.wars");

        ((IActionHandler)scripts).Invoke(Someone, "guild.retract", EntityHandle.None,
                                         new WorldPlace(1, 2, 3), picked: "Ironhelm");

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(panel.Rows.Single().LabelKey, Is.EqualTo("war.line1"));
            Assert.That(panel.Rows.Single().IdKey, Is.EqualTo("war.of1"),
                "what the line reads as and what it IS are two keys, or a verb gets the caption");

            Assert.That(world.Said, Is.EqualTo(new[] { "retracting against Ironhelm" }).AsCollection,
                "the pick reaches the handler exactly as the client sent it");
        });
    }

    /// <summary>🔴 A world written before OnAction grew its last argument keeps working.
    ///
    /// <para>Compass matches a function by name AND count, so a handler the engine asks for with seven
    /// arguments and a world that wrote six is a handler that is never called. Nothing errors: the verbs
    /// are declared, the menu draws them, the player presses one, and the game does nothing. That is the
    /// worst shape a break can take, and this stops it.</para></summary>
    [Test]
    public void AHandlerWrittenToTheOlderSignatureIsStillCalled()
    {
        var world = new RecordingWorld();
        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb g = game.Action("old.go", "Go", "Old");
                    g.OnHud();
                end function

                public function OnAction(Player who, string action, string on, integer map,
                                         integer x, integer y)
                    who.Message("went to " + map + ":" + x + "," + y);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        ((IActionHandler)scripts).Invoke(Someone, "old.go", EntityHandle.None,
                                         new WorldPlace(4, 5, 6), picked: "");

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);
            Assert.That(world.Said, Is.EqualTo(new[] { "went to 4:5,6" }).AsCollection);
        });
    }


    /// <summary>🔴 A panel the game holds up, and the refusal that stops one nothing can take down.
    ///
    /// <para>A readout that has to be on screen while something is true is not a window somebody chose
    /// to open: a score during a fight, the wait over a body that cannot act. It carries no close
    /// control, so the condition IS the way it goes away — and a declaration with the close control
    /// gone and no condition is a rectangle over the player's game until they restart the client. That
    /// is the one shape of this seam that cannot be recovered from in play, so it is refused at load,
    /// by name.</para></summary>
    [Test]
    public void APanelTheGameHoldsUp_NeedsSomethingToTakeItDown()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("war.on", "owner");

                    Panel score = game.Panel("war.score", "The war", 200, 120);
                    score.HeldWhile("war.on", 1);
                    score.Field("war.on", "Fighting over", 200, 200, 160);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var panel = registry.Panels.All.Single(p => p.Id == "war.score");
        var carried = new AttributeBag();

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(panel.Held, Is.True, "no close control");
            Assert.That(panel.Key, Is.Empty, "and no key: it is not a window to go and open");

            Assert.That(panel.While.Holds(carried), Is.False, "down while the key says nothing");
            carried.Set("war.on", 1L);
            Assert.That(panel.While.Holds(carried), Is.True, "and up the moment it does");
        });
    }

    /// <summary>The other half, refused rather than loaded.</summary>
    [Test]
    public void APanelHeldWithNoCondition_IsRefusedByName()
    {
        var thrown = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(new Held()));

        Assert.That(thrown!.Message, Does.Contain("war.score").And.Contain("held"));
    }

    private sealed class Held : ICoreModule
    {
        public string Name => "Held";

        public void Configure(ICoreBuilder builder)
            => builder.AddPanel(new GamePanel { Id = "war.score", TitleKey = "The war", Held = true });
    }

    /// <summary>🔴 A verb opening a panel nobody declared is refused BY NAME, and still offered.
    ///
    /// <para>The button draws, the player presses it, and nothing happens — which reads as a broken
    /// client rather than as a panel somebody forgot. Neither half can see the other: the verb compiles
    /// and the panel's absence is only visible from here.</para></summary>
    [Test]
    public void AVerbOpeningAPanelNobodyDeclared_IsRefusedByName()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb o = game.Action("harvest.open", "Field Book", "Harvest");
                    o.OnHud();
                    o.Opens("harvest.bok");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("harvest.bok"), "the panel that is not there");

            var opener = registry.Actions.All.Single();
            Assert.That(opener.OpensPanel, Is.Empty, "it opens nothing rather than something absent");
            Assert.That(opener.Surface, Is.EqualTo(ActionSurface.Hud),
                "and the verb itself still lands, because a menu entry that does nothing beats one that "
                + "is missing along with everything after it");
        });
    }

    /// <summary>Each surface a verb may be offered on, and the condition that grays one.</summary>
    [Test]
    public void AVerbIsOfferedOnTheSurfaceItNames_AndOnlyWhenItsConditionHolds()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.Attribute("harvest.baskets", "owner");

                    game.Action("harvest.gather", "Gather", "Harvest");
                    Verb gr = game.Action("harvest.greet", "Greet", "Harvest");
                    gr.OnPlayer();
                    Verb nm = game.Action("harvest.name", "Identify", "Harvest");
                    nm.OnNpc();
                    Verb cp = game.Action("harvest.compare", "Compare notes", "Harvest");
                    cp.OnPlayer();
                    cp.NeedsAtLeast("harvest.baskets", 1);
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var carrying = new AttributeBag();
        carrying.Set("harvest.baskets", AttributeValue.From(2));

        var compare = registry.Actions.All.Single(a => a.Id == "harvest.compare");

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(registry.Actions.All.Single(a => a.Id == "harvest.gather").Surface,
                Is.EqualTo(ActionSurface.Tile), "a square, unless the verb says otherwise");
            Assert.That(registry.Actions.All.Single(a => a.Id == "harvest.greet").Surface,
                Is.EqualTo(ActionSurface.Player));
            Assert.That(registry.Actions.All.Single(a => a.Id == "harvest.name").Surface,
                Is.EqualTo(ActionSurface.Npc));

            Assert.That(compare.When.Holds(carrying), Is.True, "carrying enough");
            Assert.That(compare.When.Holds(new AttributeBag()), Is.False, "and carrying nothing");
        });
    }

    /// <summary>A place on a body something can be worn, and a caption that defaults to the key.</summary>
    [Test]
    public void AScriptDeclaresAnEquipSlot()
    {
        var (module, registry) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    game.EquipSlot("satchel", "Satchel");
                    game.EquipSlot("fieldGlass", "");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);
            Assert.That(registry.EquipSlots.Slots.Select(s => s.LabelKey),
                Is.EqualTo(new[] { "Satchel", "Field glass" }).AsCollection,
                "a caption left blank becomes the key, as words");
        });
    }

    // ── What the rules decide ───────────────────────────────────────────────

    /// <summary>A world where nobody dies, decided by the rules rather than by a compiled policy.</summary>
    [Test]
    public void RulesCanRefuseADeath_AndSayHowLongABodyLingers()
    {
        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                end function

                public string function OnMayDie(Player who, string cause)
                    if cause == "drowned"
                        yield "";
                    end if

                    yield "Nobody dies out here.";
                end function

                public integer function OnLinger(Player who)
                    yield 30;
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(scripts.MayDie(new Death(who, EntityHandle.None, "fell")).Allowed, Is.False,
                "the rules said no");
            Assert.That(scripts.MayDie(new Death(who, EntityHandle.None, "drowned")).Allowed, Is.True,
                "and the cause is theirs to read");

            Assert.That(scripts.LingerFor(who).IsSet, Is.True, "a body that stays");
        });
    }

    /// <summary>What a death COSTS, which is a different question from whether it happens. The handler
    /// runs while the body is still on the tile it fell on, and it reads who did it.</summary>
    [Test]
    public void RulesSayWhatADeathCosts_AndWhereTheBodyComesBack()
    {
        var world = new RecordingWorld();
        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                end function

                public function OnDied(Player who, Player killer, string cause)
                    who.SetNumber("lost", 10);

                    if killer.IsHere
                        who.SetNumber("murdered", 1);
                    end if

                    who.RespawnAt(4, 5, 6);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        var killer = EntityHandle.ForPlayer(2);
        world.Here.Add(who);
        world.Here.Add(killer);

        var death = new Death(who, killer, "slain");
        scripts.OnDied(in death);

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);
            Assert.That(world.Bag["lost"].AsLong(), Is.EqualTo(10L), "the cost was applied");
            Assert.That(world.Bag["murdered"].AsLong(), Is.EqualTo(1L), "and the killer is readable");
            Assert.That(scripts.RespawnFor(in death), Is.EqualTo(new Respawn(4, 5, 6)),
                "the place the handler named");
        });
    }

    /// <summary>⚠ A place named for one death is not read for the next. The handler is asked again,
    /// and a body it says nothing about comes back where the world puts it.</summary>
    [Test]
    public void APlaceNamedForOneDeath_IsNotReadForAnother()
    {
        var world = new RecordingWorld();
        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                end function

                public function OnDied(Player who, Player killer, string cause)
                    if cause == "war"
                        who.RespawnAt(4, 5, 6);
                    end if
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        var who = EntityHandle.ForPlayer(1);
        world.Here.Add(who);

        var inWar = new Death(who, EntityHandle.None, "war");
        scripts.OnDied(in inWar);
        Assert.That(scripts.RespawnFor(in inWar), Is.EqualTo(new Respawn(4, 5, 6)));

        var drowned = new Death(who, EntityHandle.None, "drowned");
        scripts.OnDied(in drowned);

        Assert.Multiple(() =>
        {
            Assert.That(scripts.RespawnFor(in drowned).IsSet, Is.False,
                "the handler named nowhere this time");
            Assert.That(scripts.RespawnFor(in inWar).IsSet, Is.False,
                "and the place it named for the other death is gone with it");
        });
    }

    /// <summary>🔴 Rules that write neither policy leave the engine's own answers alone.
    ///
    /// <para>A policy registered for a world that never wrote one would put a call into the death path
    /// for no answer, and a handler that failed would then decide whether people can die.</para></summary>
    [Test]
    public void RulesThatDecideNeither_LeaveBothToTheEngine()
    {
        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(scripts.MayDie(new Death(EntityHandle.ForPlayer(1), EntityHandle.None, "")).Allowed,
                Is.True);
            Assert.That(scripts.LingerFor(EntityHandle.ForPlayer(1)).IsSet, Is.False);
        });
    }

    // ── Creatures ───────────────────────────────────────────────────────

    /// <summary>🔴 A verb used on a creature reaches the creature.
    ///
    /// <para>A script could OFFER a verb on one — <c>OnNpc</c> has always worked — and then do nothing
    /// with it: <c>OnAction</c> hands over the target's NAME, and the only lookup was over players. So
    /// "Attack" could sit on a wolf's menu and no rule could touch the wolf.</para>
    ///
    /// <para>⚠ <c>OnAction</c> is not the thing that changed, and must not be. A handler is matched by
    /// name AND arity, so retyping its third parameter would leave every script already written
    /// matching, loading, and being handed a value of a type its body does not expect. The square it
    /// already carries is turned back into a body instead.</para></summary>
    [Test]
    public void AVerbUsedOnACreature_ReachesTheCreature()
    {
        var world = new RecordingWorld();
        var wolf = EntityHandle.ForNpc(spawnMap: 1, spawnSlot: 3);
        world.Standing[new WorldPlace(1, 5, 7)] = wolf;

        var (module, registry) = Built("""
            shared model Rules
                public string Bite = "wild.bite";

                public function Configure(Builder game)
                    Verb bite = game.Action(Rules.Bite, "Bite it", "Wild");
                    bite.OnNpc();
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    Npc? it = World.NpcAt(map, x, y);

                    if not it.HasValue()
                        who.Message("There is nothing there.");
                        yield;
                    end if

                    who.Message("You bite " + it.Name + " at " + it.X + "," + it.Y + ".");
                    it.SetNumber("wild.bitten", it.Number("wild.bitten") + 1);
                    it.Kill("bitten");
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

        ((IActionHandler)scripts).Invoke(
            Someone, "wild.bite", EntityHandle.None, new WorldPlace(1, 5, 7), picked: "");

        Assert.Multiple(() =>
        {
            Assert.That(world.Said.Single(), Does.Contain("npc:1/3").And.Contains("5,7"));
            Assert.That(world.Killed.Single().Who, Is.EqualTo(wolf));
            Assert.That(world.Killed.Single().Cause, Is.EqualTo("bitten"));
        });
    }

    /// <summary>An empty square answers nothing rather than a body that is not there — and so does one
    /// holding the other kind of body, because a rule aimed at creatures needs to hear "there is no
    /// creature here".</summary>
    [Test]
    public void AnEmptySquare_AndOneHoldingAPlayer_AnswerNoCreature()
    {
        var world = new RecordingWorld();
        world.Standing[new WorldPlace(1, 2, 2)] = EntityHandle.ForPlayer(4);

        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb look = game.Action("wild.look", "Look", "Wild");
                    look.OnTile();
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    Npc? it = World.NpcAt(map, x, y);
                    Player? them = World.PlayerAt(map, x, y);

                    who.Message("creature=" + it.HasValue() + " player=" + them.HasValue());
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        var verbs = (IActionHandler)scripts;

        verbs.Invoke(Someone, "wild.look", EntityHandle.None, new WorldPlace(1, 9, 9), picked: "");
        verbs.Invoke(Someone, "wild.look", EntityHandle.None, new WorldPlace(1, 2, 2), picked: "");

        Assert.That(world.Said, Is.EqualTo(new[]
        {
            "creature=false player=false",
            "creature=false player=true",
        }).AsCollection);
    }

    /// <summary>A game reads the records its own editor authored, at run time.
    ///
    /// <para>Declaring a kind of record has worked for a while; READING one back while the world runs
    /// had no seam at all, and a game whose class stats or species traits live in records cannot do
    /// anything with them until it can.</para></summary>
    [Test]
    public void AGameReadsItsOwnRecords_WhileTheWorldRuns()
    {
        var world = new RecordingWorld();
        var heron = new AttributeBag();
        heron.Set("name", AttributeValue.From("Heron"));
        heron.Set("wingspan", AttributeValue.From(180L));
        world.Records["Species"] = [heron];

        var (module, _) = Built("""
            model Species
                public string name;
                public integer wingspan;

                public shared function Describe(Records these)
                    these.Are("Species", "Species", 50);
                end function
            end model

            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Message("kinds=" + World.Records("Species")
                            + " first=" + World.Record("Species", 1, "name")
                            + " span=" + World.RecordNumber("Species", 1, "wingspan")
                            + " missing=" + World.Record("Species", 9, "name"));
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.That(world.Said.Single(),
            Is.EqualTo("kinds=1 first=Heron span=180 missing="));
    }

    /// <summary>🔴 A game can say something to more than one person.
    ///
    /// <para><c>Tell</c> carried literal text to ONE player, and every system worth announcing
    /// announces to a room: somebody died here, the gate opened, the season turned. A rule that can
    /// only whisper leaves everybody else unable to see the result.</para>
    ///
    /// <para>⚠ <b>A room is who can SEE the map, not who is standing on it.</b> The world scrolls
    /// contiguously, so a player on the next map along is looking at this one — scoped to occupants,
    /// they would watch the event happen in silence. Earshot is the tighter third audience.</para>
    /// </summary>
    [Test]
    public void AGameAnnouncesToARoom_ToEarshot_AndToEverybody()
    {
        var world = new RecordingWorld();

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    World.Tell("A season turns.");
                    World.TellOn(who.Map, "Somebody arrives.");
                    World.TellNear(who.Map, who.X, who.Y, "You hear footsteps.");
                    who.Message("And this is only for you.");
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        world.Place = new WorldPlace(3, 4, 5);

        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(world.Announced, Is.EqualTo(new[]
            {
                ("everyone", "A season turns."),
                ("map 3", "Somebody arrives."),
                ("near 3,4,5", "You hear footsteps."),
            }).AsCollection);

            Assert.That(world.Said.Single(), Is.EqualTo("And this is only for you."),
                "the one-player path is untouched");
        });
    }

    /// <summary>🔴 A game floats a number off a body.
    ///
    /// <para>The client has always known how to do this — centering on an oversize footprint, holding
    /// the text until an in-flight projectile lands — and its own comment said Core spawns none,
    /// because what the text SAYS is a game's. There was simply no wire between them, so a fight
    /// happened in silence and the numbers only existed in a chat line.</para>
    ///
    /// <para>⚠ It is the one place a script asks the client to DRAW. Everything else a game does sets
    /// state and lets the client decide what that looks like; a number that happened once is not state
    /// and there is nothing to derive it from.</para></summary>
    [Test]
    public void AGameFloatsANumberOffABody()
    {
        var world = new RecordingWorld();
        var wolf = EntityHandle.ForNpc(spawnMap: 1, spawnSlot: 2);
        world.Standing[new WorldPlace(1, 3, 4)] = wolf;

        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb hit = game.Action("wild.hit", "Hit it", "Wild");
                    hit.OnNpc();
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    Npc? it = World.NpcAt(map, x, y);

                    if not it.HasValue()
                        yield;
                    end if

                    it.Float("-12", 255, 80, 80);
                    who.Float("hit!", 0, 255, 0);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

        ((IActionHandler)scripts).Invoke(
            Someone, "wild.hit", EntityHandle.None, new WorldPlace(1, 3, 4), picked: "");

        Assert.That(world.Floated, Is.EqualTo(new[]
        {
            (wolf, "-12", 0xFF5050u),
            (Someone, "hit!", 0x00FF00u),
        }).AsCollection);
    }

    /// <summary>A channel outside 0-255 is clamped rather than refused. A game doing arithmetic on a
    /// color should get a color out of it.</summary>
    [Test]
    public void AColorChannelOutOfRange_IsClamped()
    {
        var world = new RecordingWorld();

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    who.Float("ow", 999, 0 - 40, 128);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.That(world.Floated.Single().Rgb, Is.EqualTo(0xFF0080u));
    }

    /// <summary>🔴 A creature can be put in the four timed states a creature has.
    ///
    /// <para>The engine already kept these clocks and the client already drew them — an NPC's
    /// overhead bars appear while it is engaged, and the packet has carried a <c>combatMs</c> field
    /// the whole time. The server simply always sent "never", because nothing could set one. A fight
    /// with a wolf ran with the wolf's bars hidden.</para>
    ///
    /// <para>⚠ Four, not five. <c>Down</c> is a body lying there waiting to get up; a creature that
    /// runs out of health despawns and its slot counts down to a respawn, which the spawn clock
    /// already owns. Giving it an NPC meaning would be a second, conflicting answer to "when does it
    /// come back".</para></summary>
    [Test]
    public void ACreatureCanBeEngaged_Marked_Flagged_AndHeldOff()
    {
        var world = new RecordingWorld();
        var wolf = EntityHandle.ForNpc(spawnMap: 1, spawnSlot: 6);
        world.Standing[new WorldPlace(2, 8, 8)] = wolf;

        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb hit = game.Action("wild.hit", "Hit it", "Wild");
                    hit.OnNpc();
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    Npc? it = World.NpcAt(map, x, y);

                    if not it.HasValue()
                        yield;
                    end if

                    it.Engage(10);
                    it.Mark(60);
                    it.Flag(5);
                    it.Wait(2);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

        ((IActionHandler)scripts).Invoke(
            Someone, "wild.hit", EntityHandle.None, new WorldPlace(2, 8, 8), picked: "");

        Assert.That(world.Timed, Is.EqualTo(new[]
        {
            (wolf, "engaged", 10),
            (wolf, "marked", 60),
            (wolf, "aggressor", 5),
            (wolf, "cooldown", 2),
        }).AsCollection);
    }

    /// <summary>🔴 A game can ask the client to show a swing, a throw and a burst.
    ///
    /// <para>All three were written for a game to call and none had a caller — <c>EmitArc</c>,
    /// <c>SpawnProjectile</c> and <c>EmitSplatter</c> each say so in their own header. There was no
    /// wire between the game and the machinery, so a fight had no picture at all.</para>
    ///
    /// <para>⚠ These are the ONLY draws a game may call, and they are one kind of thing: an event
    /// that happened once with nothing for a client to derive it from. A swing is not state.</para>
    ///
    /// <para>A throw is named for what it is aimed AT rather than taking "a body", because Compass has
    /// no union type — and a player throwing at a creature is the common case, so a throw that only
    /// accepted its own kind would be the wrong half.</para></summary>
    [Test]
    public void AGameShowsASwing_AThrow_AndABurst()
    {
        var world = new RecordingWorld();
        var wolf = EntityHandle.ForNpc(spawnMap: 1, spawnSlot: 5);
        world.Standing[new WorldPlace(1, 6, 6)] = wolf;

        var (module, _) = Built("""
            shared model Rules
                public function Configure(Builder game)
                    Verb hit = game.Action("wild.hit", "Hit it", "Wild");
                    hit.OnNpc();
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    Npc? it = World.NpcAt(map, x, y);

                    if not it.HasValue()
                        who.Sweep(false);
                        yield;
                    end if

                    who.Sweep(true);
                    who.ThrowAtNpc(it, "bolt", 255, 220, 90);
                    it.Burst(190, 20, 20, 70);
                    it.ThrowAtPlayer(who, "glitter", 0, 0, 255);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

        var verbs = (IActionHandler)scripts;
        verbs.Invoke(Someone, "wild.hit", EntityHandle.None, new WorldPlace(1, 6, 6), picked: "");
        verbs.Invoke(Someone, "wild.hit", EntityHandle.None, new WorldPlace(1, 9, 9), picked: "");

        Assert.That(world.Shown, Is.EqualTo(new[]
        {
            (Someone, "sweep hit", EntityHandle.None, 0u),
            (Someone, "throw Bolt", wolf, 0xFFDC5Au),
            (wolf, "burst 0.7", EntityHandle.None, 0xBE1414u),
            (wolf, "throw Glitter", Someone, 0x0000FFu),

            // The second call found nothing there, so the swing whiffed.
            (Someone, "sweep", EntityHandle.None, 0u),
        }).AsCollection);
    }

    /// <summary>A style nobody offers falls back to a bolt rather than refusing. A projectile is
    /// decoration: a misspelled one should throw something visible and read as the author's own typo,
    /// rather than take the hit it belongs to down with it.</summary>
    [Test]
    public void AnUnknownProjectileStyle_FallsBackRatherThanRefusing()
    {
        var world = new RecordingWorld();
        world.Standing[new WorldPlace(1, 2, 2)] = EntityHandle.ForPlayer(3);

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    Player? them = World.PlayerAt(1, 2, 2);

                    if them.HasValue()
                        who.ThrowAtPlayer(them, "banana", 1, 2, 3);
                    end if
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.That(world.Shown.Single().What, Is.EqualTo("throw Bolt"));
    }

    /// <summary>⚠ A stain is the one worldspace mark that LASTS. Everything else a game shows is gone
    /// the moment it has played.</summary>
    [Test]
    public void AStainOutlastsEverythingElseAGameShows()
    {
        var world = new RecordingWorld();

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    World.Stain(who.Map, who.X, who.Y, 2, 80);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        world.Place = new WorldPlace(4, 7, 8);

        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(world.Stained.Single().At, Is.EqualTo(new WorldPlace(4, 7, 8)));
            Assert.That(world.Stained.Single().Size, Is.EqualTo(2));
            Assert.That(world.Stained.Single().Amount, Is.EqualTo(0.8f).Within(0.001f));
        });
    }

    /// <summary>🔴 A game says something to a guild.
    ///
    /// <para>The other three audiences are all about PLACE — one body, a region, an earshot — and a
    /// guild is not a place. Every MSR system that announced to one had nothing to announce
    /// through.</para>
    ///
    /// <para>A set and a lookup rather than a <c>TellGuild</c>: the set tell is the primitive, and a
    /// game gathering its own raid or everybody carrying a key wants the same call.</para></summary>
    [Test]
    public void AGameSaysSomethingToAGuild()
    {
        var world = new RecordingWorld();
        var them = new List<EntityHandle>
        {
            Someone, EntityHandle.ForPlayer(4), EntityHandle.ForPlayer(9),
        };

        world.GuildOfBody[Someone] = "The Gathering";
        world.Groups[Someone] = them;

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    if who.Guild == ""
                        who.Message("You are in no guild.");
                        yield;
                    end if

                    Player[] mates = World.Guildmates(who);
                    World.TellThese(mates, who.Guild + " gains a member.");
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(world.Announced.Single().Audience,
                Is.EqualTo("these player:1 player:4 player:9"));

            Assert.That(world.Announced.Single().Text, Is.EqualTo("The Gathering gains a member."));
        });
    }

    /// <summary>Somebody in no guild gets an empty set and a blank name, and the two say different
    /// things: no guild at all, against a guild with nobody else online.</summary>
    [Test]
    public void SomebodyInNoGuild_GetsAnEmptySetAndABlankName()
    {
        var world = new RecordingWorld();

        var (module, _) = Built("""
            shared model Rules
                public function OnPlayerJoined(Player who)
                    Player[] mates = World.Guildmates(who);
                    who.Message("guild='" + who.Guild + "' mates=" + mates.Count);
                end function
            end model
            """, world);

        using ScriptedWorldModule scripts = module;
        ((IWorldObserver)scripts).OnPlayerJoined(Someone);

        Assert.That(world.Said.Single(), Is.EqualTo("guild='' mates=0"));
    }

    // ── A message of a game's own ────────────────────────────────────────────

    /// <summary>🔴 A script declares a message, and a line nobody compiled a type for arrives as the
    /// fields its model named.
    ///
    /// <para>The registry takes a PARSE DELEGATE rather than a type — <c>Register&lt;T&gt;</c> is only
    /// the convenience overload — so nothing about a packet requires an assembly. What the model adds
    /// is which field is a number and which is text, because a line is text either way.</para></summary>
    [Test]
    public void AScriptDeclaresAMessage_AndIsHandedItsFields()
    {
        var (module, registry) = Built("""
            model Note
                public integer species;
                public string comment;
                public boolean sure;
            end model

            shared model Rules
                public function Configure(Builder game)
                    game.Message("Note");
                end function

                public function OnMessage(Player who, string message, Values values)
                    who.Message(message
                            + ": " + values.Number("species")
                            + " / " + values.Text("comment")
                            + " / " + values.Truth("sure")
                            + " / missing=" + values.Has("nothing"));
                end function
            end model
            """);

        var world = new RecordingWorld();
        using ScriptedWorldModule scripts = module;
        scripts.Start(world);

        var route = (IPacketRoute)scripts;
        var packet = registry.Packets.Deserialize(
            "Note", """{"cmd":"Note","species":3,"comment":"by the shore","sure":true}""", false);

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(registry.Packets.Knows("Note"), Is.True, "the command deserializes");
            Assert.That(route.Commands, Is.EqualTo(new[] { "Note" }).AsCollection, "and is routed");
            Assert.That(packet, Is.Not.Null, "a line nobody compiled a type for still reads");
        });

        route.Handle(EntityHandle.ForPlayer(1), packet!);

        Assert.That(world.Said.Single(),
            Is.EqualTo("Note: 3 / by the shore / true / missing=false"));
    }

    /// <summary>🔴 A panel asks the player to fill a model in, and a stock client can send it.
    ///
    /// <para>This is the half that was missing. A client originates a VERB — an action id and the
    /// square it was used on — and nothing else, so "they filled this in and sent it" had no carrier
    /// and three documents said so. A panel that asks carries the controls, and the line it sends is
    /// read by the parse delegate the same model registered.</para>
    ///
    /// <para>⚠ Both halves come from one line on purpose. Inputs with no message would collect values
    /// nothing sends; a message with no inputs is one a stock client still could not compose.</para>
    /// </summary>
    [Test]
    public void APanelAsksForAMessage_AndCarriesTheControlsToFillIt()
    {
        var (module, registry) = Built("""
            enumeration Habitat
                Shore, Woodland
            end enumeration

            model Sighting
                public string comment;
                public integer count;
                public Habitat where;
                public boolean sure;
            end model

            shared model Rules
                public function Configure(Builder game)
                    Panel book = game.Panel("survey.book", "Field Book", 240, 200);
                    book.Asks("Sighting", "Record it");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        var panel = registry.Panels.Find("survey.book")!;

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty);

            Assert.That(panel.Asks, Is.EqualTo("Sighting"));
            Assert.That(panel.SendLabelKey, Is.EqualTo("Record it"));

            // Declaration order, because that is the order the form is drawn in.
            Assert.That(panel.Inputs.Select(i => i.Field),
                Is.EqualTo(new[] { "comment", "count", "where", "sure" }).AsCollection);

            Assert.That(panel.Inputs.Select(i => i.Kind), Is.EqualTo(new[]
            {
                FieldKind.Text, FieldKind.Integer, FieldKind.Choice, FieldKind.Flag,
            }).AsCollection);

            // 🔴 The members travel with the input. Choice sets go to the EDITOR and not to a
            // player's client, so an id here would be a drop-down with nothing in it.
            Assert.That(panel.Inputs[2].Choices,
                Is.EqualTo(new[] { "Shore", "Woodland" }).AsCollection);

            Assert.That(panel.Inputs[0].LabelKey, Is.EqualTo("Comment"), "a caption nobody gave");

            // Asking for it is all it takes to put it on the wire: nothing else had to be written.
            Assert.That(registry.Packets.Knows("Sighting"), Is.True);
            Assert.That(((IPacketRoute)scripts).Commands,
                Is.EqualTo(new[] { "Sighting" }).AsCollection);
        });
    }

    /// <summary>What a panel sends reaches the game's own handler, values and all.</summary>
    [Test]
    public void WhatAPanelSends_ArrivesAtOnMessage()
    {
        var (module, registry) = Built("""
            model Sighting
                public string comment;
                public integer count;
            end model

            shared model Rules
                public function Configure(Builder game)
                    Panel book = game.Panel("survey.book", "Field Book", 240, 200);
                    book.Asks("Sighting", "Record it");
                end function

                public function OnMessage(Player who, string message, Values values)
                    who.Message(message + ": " + values.Number("count")
                            + " x " + values.Text("comment"));
                end function
            end model
            """);

        var world = new RecordingWorld();
        using ScriptedWorldModule scripts = module;
        scripts.Start(world);

        var packet = registry.Packets.Deserialize(
            "Sighting", """{"cmd":"Sighting","comment":"herons","count":4}""", false);

        ((IPacketRoute)scripts).Handle(EntityHandle.ForPlayer(1), packet!);

        Assert.That(world.Said.Single(), Is.EqualTo("Sighting: 4 x herons"));
    }

    /// <summary>⚠ A panel asks for ONE message. A second is refused by name rather than adding a row
    /// group whose button nobody could tell from the first one's.</summary>
    [Test]
    public void APanelAskingTwice_HasTheSecondRefusedByName()
    {
        var (module, registry) = Built("""
            model Sighting
                public string comment;
            end model

            model Correction
                public string comment;
            end model

            shared model Rules
                public function Configure(Builder game)
                    Panel book = game.Panel("survey.book", "Field Book", 240, 200);
                    book.Asks("Sighting", "Record it");
                    book.Asks("Correction", "Fix it");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("Correction"), "the one that was refused");
            Assert.That(refused, Does.Contain("Sighting"), "and the one already there");

            Assert.That(registry.Panels.Find("survey.book")!.Asks, Is.EqualTo("Sighting"),
                "the first still stands");
            Assert.That(registry.Packets.Knows("Correction"), Is.False,
                "and the refused one is not on the wire either");
        });
    }

    /// <summary>Asking for a message and declaring it outright is one message, in either order — a
    /// game wanting a bot to send the same thing writes both.</summary>
    [Test]
    public void AMessageAskedForAndDeclared_IsOneMessage()
    {
        var (module, registry) = Built("""
            model Sighting
                public string comment;
            end model

            shared model Rules
                public function Configure(Builder game)
                    Panel book = game.Panel("survey.book", "Field Book", 240, 200);
                    book.Asks("Sighting", "Record it");
                    game.Message("Sighting");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty,
                "saying it twice is not an error");
            Assert.That(((IPacketRoute)scripts).Commands,
                Is.EqualTo(new[] { "Sighting" }).AsCollection, "and registers one route");
        });
    }

    /// <summary>A line carrying a field the model never named leaves it behind.
    ///
    /// <para>A script reads what it declared, so a sender cannot reach past what the rules said they
    /// may send — and a field the line simply left out is absent rather than zero.</para></summary>
    [Test]
    public void AMessageCarriesOnlyWhatItsModelNamed()
    {
        var (module, registry) = Built("""
            model Note
                public integer species;
            end model

            shared model Rules
                public function Configure(Builder game)
                    game.Message("Note");
                end function

                public function OnMessage(Player who, string message, Values values)
                    who.Message("species=" + values.Has("species")
                            + " smuggled=" + values.Has("admin"));
                end function
            end model
            """);

        var world = new RecordingWorld();
        using ScriptedWorldModule scripts = module;
        scripts.Start(world);

        var packet = registry.Packets.Deserialize(
            "Note", """{"cmd":"Note","admin":true}""", false);

        ((IPacketRoute)scripts).Handle(EntityHandle.ForPlayer(1), packet!);

        Assert.That(world.Said.Single(), Is.EqualTo("species=false smuggled=false"));
    }

    /// <summary>A model that cannot travel, and one that is not there, are refused BY NAME — and a
    /// world that declares no message registers no route at all.</summary>
    [Test]
    public void AMessageThatCannotTravel_IsRefusedByName()
    {
        var (module, registry) = Built("""
            model Note
                public string[] tags;
            end model

            shared model Rules
                public function Configure(Builder game)
                    game.Message("Note");
                    game.Message("Note2");
                end function
            end model
            """);

        using ScriptedWorldModule scripts = module;
        string refused = string.Join("\n", module.Problems.Select(p => p.Message));

        Assert.Multiple(() =>
        {
            Assert.That(refused, Does.Contain("tags"), "a field no message could carry");
            Assert.That(refused, Does.Contain("Note2"), "and a model nobody declared");
            Assert.That(registry.Packets.Knows("Note"), Is.False,
                "a message with nothing left to carry is not registered");
            Assert.That(((IPacketRoute)scripts).Commands, Is.Empty);
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

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    who.Message(action + " at " + map + ":" + x + "," + y);
                end function
            end model
            """, world);

        ((IActionHandler)module).Invoke(Someone, "harvest.gather", EntityHandle.None,
                                        new WorldPlace(3, 11, 4), picked: "");

        Assert.That(world.Said, Is.EqualTo(new[] { "harvest.gather at 3:11,4" }));
    }

    /// <summary>A verb offered ON somebody tells the script who that was.
    ///
    /// <para>The name rather than the body, because the boundary cannot carry "somebody, or nobody" —
    /// and blank answers for the square and HUD surfaces, covering most verbs. A script handed
    /// the argument but never a value would be a seam that looks present and answers nothing.</para>
    /// </summary>
    [Test]
    public void AVerbUsedOnSomebody_TellsTheScriptWho()
    {
        var world = new RecordingWorld();
        using var module = Loaded("""
            shared model Rules
                public function Configure(Builder game)
                    game.Action("harvest.greet", "Greet", "Harvest");
                end function

                public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
                    if on == ""
                        who.Message("nobody");
                        yield;
                    end if

                    who.Message("greeted " + on);
                end function
            end model
            """, world);

        var handler = (IActionHandler)module;
        handler.Invoke(Someone, "harvest.greet", Someone, new WorldPlace(1, 1, 1), picked: "");
        handler.Invoke(Someone, "harvest.greet", EntityHandle.None, new WorldPlace(1, 1, 1), picked: "");

        Assert.That(world.Said, Is.EqualTo(new[] { "greeted " + world.NameOf(Someone), "nobody" }));
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

        // Every `public function` the file declares whose name is one the engine asks for. If the
        // script wrote it, the engine has to have taken it.
        string source = string.Join("\n", Directory.GetFiles(
            Path.Combine(world, ScriptedWorldModule.ScriptsFolder), "*.cm", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        var written = ScriptedWorldModule.Handlers
            .Select(h => h.Name)
            .Where(name => source.Contains("public function " + name + "(", StringComparison.Ordinal))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty,
                "the shipped rules do not compile:\n" + string.Join("\n", module.Problems));
            Assert.That(module.IsLoaded, Is.True);

            Assert.That(written, Is.Not.Empty, "the shipped rules declare no handler at all");

            // \U0001F534 The half compiling does not cover. A handler is matched by NAME AND ARITY, so a
            // signature that drifts from the table never gets called: it compiles, it loads, and the
            // verb it served quietly stops working, as changing OnAction's arity did.
            Assert.That(module.Offered, Is.SupersetOf(written),
                "the shipped rules declare a handler the engine did not take \u2014 check its arity "
                + "against ScriptedWorldModule.Handlers");
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
    /// Stands in for the engine, recording what a script asked it to do. The module is under test
    /// here; what <c>ServerWorld</c> does with a <c>Tell</c> is pinned by its own tests.
    /// </summary>
    /// <summary>Shared with the scripted-Survey fixture, which needs a world to hand a module.</summary>
    internal sealed class RecordingWorld : IWorld
    {
        public List<string> Said { get; } = [];
        public AttributeBag Bag { get; } = new();
        public WorldPlace Place { get; set; } = new(1, 0, 0);

        public void Tell(EntityHandle who, string text, string channel, int color) => Said.Add(text);

        /// <summary>Which bodies are in the world, or empty for "every handle names one".
        ///
        /// <para>⚠ Empty is the default because almost every test here acts on one body and does not
        /// care. A test that runs a PER-PLAYER tick does care: this double keeps ONE attribute bag,
        /// so a tick that visits every slot applies the same rule to the same bag hundreds of times
        /// and any rate compounds.</para></summary>
        public HashSet<EntityHandle> Here { get; } = [];

        public bool IsInWorld(EntityHandle who) =>
            who.IsSet && (Here.Count == 0 || Here.Contains(who));
        /// <summary>Where a test put this body, or <see cref="Place"/> for one it said nothing
        /// about — which is most of them, and what every test written before Standing existed
        /// relies on.</summary>
        public WorldPlace PlaceOf(EntityHandle who)
        {
            foreach (var (at, body) in Standing)
            {
                if (body == who) return at;
            }

            return Place;
        }

        /// <summary>Whoever a test put on that square, by the place it is standing at.</summary>
        public Dictionary<WorldPlace, EntityHandle> Standing { get; } = [];

        /// <summary>What was announced, and to whom — "everyone", "map 3", or "near 1,4,5".</summary>
        public List<(string Audience, string Text)> Announced { get; } = [];

        public void TellEveryone(string text, string channel, int color) =>
            Announced.Add(("everyone", text));

        public void TellEveryoneOn(int mapNum, string text, string channel, int color) =>
            Announced.Add(($"map {mapNum}", text));

        public void TellEveryoneNear(WorldPlace at, string text, string channel, int color) =>
            Announced.Add(($"near {at.Map},{at.X},{at.Y}", text));

        public void TellThese(IReadOnlyCollection<EntityHandle> them, string text,
                              string channel, int color) =>
            Announced.Add(($"these {string.Join(' ', them)}", text));

        /// <summary>Which guild a test put somebody in, who else is in it with them, and who is in
        /// their party. Guildmates and partymates are kept apart: a rule that treats the two
        /// differently is one a double answering both from one list could not be asked about.</summary>
        public Dictionary<EntityHandle, string> GuildOfBody { get; } = [];
        public Dictionary<EntityHandle, List<EntityHandle>> Groups { get; } = [];
        public Dictionary<EntityHandle, List<EntityHandle>> Parties { get; } = [];

        public string GuildOf(EntityHandle who) =>
            GuildOfBody.TryGetValue(who, out string? name) ? name : string.Empty;

        public IReadOnlyList<EntityHandle> GuildmatesOf(EntityHandle who) =>
            Groups.TryGetValue(who, out var mates) ? mates : [];

        public IReadOnlyList<EntityHandle> PartyOf(EntityHandle who) =>
            Parties.TryGetValue(who, out var mates) ? mates : [];

        /// <summary>What floated off whom, and in what color.</summary>
        public List<(EntityHandle Who, string Text, uint Rgb)> Floated { get; } = [];

        public void Float(EntityHandle who, string text, uint rgb, float splatter) =>
            Floated.Add((who, text, rgb));

        /// <summary>Which state was put on whom, and for how long.</summary>
        public List<(EntityHandle Who, string State, int Seconds)> Timed { get; } = [];

        /// <summary>What the game asked to be shown, as "sweep", "throw" or "burst".</summary>
        public List<(EntityHandle From, string What, EntityHandle To, uint Rgb)> Shown { get; } = [];

        public void Sweep(EntityHandle who, bool connected) =>
            Shown.Add((who, connected ? "sweep hit" : "sweep", EntityHandle.None, 0u));

        public void Throw(EntityHandle from, EntityHandle to, ProjectileStyle style, uint rgb) =>
            Shown.Add((from, $"throw {style}", to, rgb));

        public void Burst(EntityHandle who, uint rgb, float intensity) =>
            Shown.Add((who, $"burst {intensity:0.##}", EntityHandle.None, rgb));

        public EntityHandle At(WorldPlace place) =>
            Standing.TryGetValue(place, out var who) ? who : EntityHandle.None;
        /// <summary>A bag of its own for each body a test asked to keep apart. Everything else shares
        /// <see cref="Bag"/>, which suits nearly every test here.</summary>
        public Dictionary<EntityHandle, AttributeBag> Bags { get; } = [];

        /// <summary>Gives this body a bag of its own and hands it back. A test about two bodies
        /// writing the same key needs it; one about a single body does not.</summary>
        public AttributeBag BagFor(EntityHandle who)
        {
            if (!Bags.TryGetValue(who, out var own)) Bags[who] = own = new AttributeBag();

            return own;
        }

        private AttributeBag Held(EntityHandle who) =>
            Bags.TryGetValue(who, out var own) ? own : Bag;

        public AttributeBag? AttributesOf(EntityHandle who) => Held(who);

        public bool SetAttribute(EntityHandle who, string key, AttributeValue value)
        {
            Held(who).Set(key, value);
            return true;
        }

        public bool SetAttributes(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values)
        {
            foreach (var (key, value) in values) Held(who).Set(key, value);
            return true;
        }

        public bool RemoveAttribute(EntityHandle who, string key) => Held(who).Remove(key);
        /// <summary>Which states a test has put a body in, for the readers to answer from.</summary>
        public HashSet<(EntityHandle Who, string State)> In { get; } = [];

        public bool IsEngaged(EntityHandle who) => In.Contains((who, "engaged"));
        public bool IsDowned(EntityHandle who) => In.Contains((who, "downed"));
        public bool IsMarked(EntityHandle who) => In.Contains((who, "marked"));
        public bool IsAggressor(EntityHandle who) => In.Contains((who, "aggressor"));
        public bool IsWaiting(EntityHandle who) => In.Contains((who, "cooldown"));

        public void SetEngaged(EntityHandle who, int seconds) => Timed.Add((who, "engaged", seconds));
        public void SetDowned(EntityHandle who, int seconds) => Timed.Add((who, "downed", seconds));
        public void SetMarked(EntityHandle who, int seconds) => Timed.Add((who, "marked", seconds));
        public void SetAggressor(EntityHandle who, int seconds) => Timed.Add((who, "aggressor", seconds));
        public void SetActionCooldown(EntityHandle who, int seconds) => Timed.Add((who, "cooldown", seconds));
        /// <summary>What a rule asked to die, and why.</summary>
        public List<(EntityHandle Who, string Cause)> Killed { get; } = [];

        public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "")
        {
            Killed.Add((who, causeKey));
            return true;
        }

        public bool Warp(EntityHandle who, WorldPlace to)
        {
            Place = to;
            return true;
        }

        /// <summary>What was put in a bag, in the order it was granted — a kit, in other words.</summary>
        public List<(int Item, int Many)> Given { get; } = [];

        public void Give(EntityHandle who, int itemNum, int quantity = 1) => Given.Add((itemNum, quantity));

        /// <summary>What was taken back out of a bag, in the order it went.</summary>
        public List<(int Item, int Many)> Taken { get; } = [];

        public void Take(EntityHandle who, int itemNum, int quantity = 1) => Taken.Add((itemNum, quantity));
        public void ReleaseGhost(EntityHandle who) { }
        /// <summary>What was marked on the ground, which unlike everything else shown, lasts.</summary>
        public List<(WorldPlace At, int Size, float Amount)> Stained { get; } = [];

        public void Stain(WorldPlace at, int size, WorldLayer layer, float amount) =>
            Stained.Add((at, size, amount));
        /// <summary>What a test authored, by the kind of record it belongs to. 1-based on the wire,
        /// so slot 1 is index 0 here.</summary>
        public Dictionary<string, IReadOnlyList<AttributeBag>> Records { get; } = [];

        public IReadOnlyList<AttributeBag> RecordsOf(string familyId) =>
            Records.TryGetValue(familyId, out var rows) ? rows : [];

        public AttributeBag? RecordAt(string familyId, int num) =>
            RecordsOf(familyId) is { } rows && num >= 1 && num <= rows.Count ? rows[num - 1] : null;

        /// <summary>The game's own stores, kept for real so a script can be watched using them.</summary>
        public Dictionary<string, Dictionary<string, AttributeBag>> Stores { get; } =
            new(StringComparer.Ordinal);

        public AttributeValue? Kept(string store, string key, string field) =>
            Stores.TryGetValue(store, out var entries)
            && entries.TryGetValue(key, out AttributeBag? bag)
            && bag.TryGet(field, out AttributeValue held) ? held : null;

        public void SetKept(string store, string key, string field, AttributeValue value)
        {
            if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            if (!Stores.TryGetValue(store, out var entries))
            {
                entries = new Dictionary<string, AttributeBag>(StringComparer.Ordinal);
                Stores[store] = entries;
            }

            if (!entries.TryGetValue(key, out AttributeBag? bag))
            {
                bag = new AttributeBag();
                entries[key] = bag;
            }

            bag.Set(field, value);
        }

        public bool HasKept(string store, string key) =>
            Stores.TryGetValue(store, out var entries) && entries.ContainsKey(key);

        public bool Forget(string store, string key)
        {
            if (!Stores.TryGetValue(store, out var entries) || !entries.Remove(key)) return false;

            if (entries.Count == 0) Stores.Remove(store);
            return true;
        }

        public int KeptCount(string store) => KeptKeys(store).Length;

        public string KeptKeyAt(string store, int index)
        {
            string[] keys = KeptKeys(store);
            return index >= 1 && index <= keys.Length ? keys[index - 1] : string.Empty;
        }

        private string[] KeptKeys(string store) =>
            Stores.TryGetValue(store, out var entries)
                ? [.. entries.Keys.Order(StringComparer.Ordinal)] : [];

        /// <summary>What a test called each record, by family and slot.</summary>
        public Dictionary<(string, int), string> RecordNames { get; } = [];

        public string RecordName(string familyId, int num) =>
            RecordNames.TryGetValue((familyId, num), out string? name) ? name
                : RecordAt(familyId, num) is { } row && row.TryGet("name", out AttributeValue named)
                    ? named.AsText() : string.Empty;

        /// <summary>A game's fields on each map, keyed by map number. Stands in for the map's own bag
        /// with its group's behind it, which a test has no groups to build.</summary>
        public Dictionary<int, AttributeBag> MapFields { get; } = [];

        public AttributeValue? MapValue(int mapNum, string key) =>
            MapFields.TryGetValue(mapNum, out var bag) && bag.TryGet(key, out AttributeValue held)
                ? held : null;

        /// <summary>What a test called each body. Anything unnamed answers with its handle, which is
        /// distinct per body and is all most tests ever need.</summary>
        public Dictionary<EntityHandle, string> Names { get; } = [];

        public string NameOf(EntityHandle who) =>
            Names.TryGetValue(who, out string? name) ? name
            : who.IsSet ? who.ToString() : string.Empty;

        /// <summary>Which creature a handle is a copy of. The double has no creature table, so a test that
        /// cares sets one; everything else reads the spawn slot, which is distinct per body and is the
        /// key a rule about a species would use.</summary>
        public Dictionary<EntityHandle, int> Kinds { get; } = [];

        public int KindOf(EntityHandle who) =>
            !who.IsNpc ? 0 : Kinds.TryGetValue(who, out int kind) ? kind : who.SpawnSlot;

        /// <summary>Who each creature was last sent after, and who was told to let go — the two halves of
        /// pointing a body at somebody, kept so a test can read back what the script asked for.</summary>
        public Dictionary<EntityHandle, EntityHandle> Chasing { get; } = [];
        public List<EntityHandle> Forgotten { get; } = [];

        public bool Provoke(EntityHandle npc, EntityHandle target)
        {
            if (!IsInWorld(npc) || !IsInWorld(target)) return false;
            if (npc == target) return false;

            Chasing[npc] = target;
            return true;
        }

        public bool Forget(EntityHandle npc)
        {
            if (!IsInWorld(npc)) return false;

            Chasing.Remove(npc);
            Forgotten.Add(npc);
            return true;
        }

        /// <summary>What the ground is, by square. A test that cares sets one; everything else is
        /// walkable, as an open map is.</summary>
        public Dictionary<WorldPlace, string> Ground { get; } = [];

        public string TileAt(WorldPlace place) =>
            Ground.TryGetValue(place, out string? kind) ? kind : "walkable";

        /// <summary>Squares a test declared sightless. Nothing is in the way otherwise, as an open map
        /// with no walls on it answers.</summary>
        public HashSet<WorldPlace> Unseen { get; } = [];

        public bool CanSee(WorldPlace from, WorldPlace to) => !Unseen.Contains(to);

        /// <summary>Manhattan on one map, and -1 across two. The double has no grid, so it answers the
        /// shape of the question rather than the world's own geometry.</summary>
        public int Distance(WorldPlace from, WorldPlace to) =>
            from.Map == to.Map ? Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) : -1;

        /// <summary>What a test said the sky is doing. Clear until it says otherwise.</summary>
        public string Weather { get; set; } = "clear";

        public string WeatherOn(int mapNum) => Weather;

        /// <summary>Which region each map belongs to. A test that cares about territory says so.</summary>
        public Dictionary<int, int> Regions { get; } = [];

        public int MapGroupOf(int mapNum) => Regions.TryGetValue(mapNum, out int group) ? group : 0;

        /// <summary>What a rule asked to be put on, in order.</summary>
        public List<int> Worn { get; } = [];

        public bool Wear(EntityHandle who, int itemNum)
        {
            if (!IsInWorld(who)) return false;

            Worn.Add(itemNum);
            return true;
        }

        /// <summary>What a test put in each bag slot: the item, how many, and whether it is worn.</summary>
        public Dictionary<(EntityHandle, int), (int ItemNum, int Quantity, bool Worn)> Carried { get; } = [];

        public IReadOnlyList<int> BagOf(EntityHandle who) =>
            [.. Carried.Keys.Where(k => k.Item1 == who).Select(k => k.Item2).OrderBy(s => s)];

        public (int ItemNum, int Quantity, bool Worn) InSlot(EntityHandle who, int slot) =>
            Carried.TryGetValue((who, slot), out var held) ? held : (0, 0, false);

        /// <summary>What a rule put on the ground, by the slot it came out of.</summary>
        public List<(EntityHandle Who, int Slot, int Quantity)> Dropped { get; } = [];

        public bool DropFrom(EntityHandle who, int slot, int quantity = 0)
        {
            if (InSlot(who, slot).ItemNum <= 0) return false;

            Dropped.Add((who, slot, quantity));
            Carried.Remove((who, slot));
            return true;
        }

        /// <summary>What a test says each account may do. Everybody is an ordinary player until it
        /// says otherwise.</summary>
        public Dictionary<EntityHandle, string> Access { get; } = [];

        public string AccessOf(EntityHandle who) =>
            Access.TryGetValue(who, out string? level) ? level : "player";

        /// <summary>Where a test says each map sends somebody who leaves it.</summary>
        public Dictionary<int, WorldPlace> Exits { get; } = [];

        public WorldPlace ExitFrom(int mapNum) =>
            Exits.TryGetValue(mapNum, out var at) ? at : WorldPlace.Nowhere;

        /// <summary>How much of each item a test says a body is carrying.</summary>
        public Dictionary<(EntityHandle, int), long> Holding { get; } = [];

        public long Carrying(EntityHandle who, int itemNum) =>
            Holding.TryGetValue((who, itemNum), out long many) ? many : 0L;

        /// <summary>What a rule asked to be taken off, in order.</summary>
        public List<int> Removed { get; } = [];

        public bool Remove(EntityHandle who, int itemNum)
        {
            if (!HasOn.TryGetValue(who, out var on) || !on.Contains(itemNum)) return false;

            on.Remove(itemNum);
            Removed.Add(itemNum);
            return true;
        }

        /// <summary>Whether a test said this body is running. Walking, until it does.</summary>
        public HashSet<EntityHandle> Runners { get; } = [];

        public bool IsRunning(EntityHandle who) => Runners.Contains(who);

        public Dictionary<EntityHandle, int> Paces { get; } = [];

        public int PaceOf(EntityHandle who) => Paces.TryGetValue(who, out int pace) ? pace : 0;

        public void SetPace(EntityHandle who, int pace) => Paces[who] = pace;

        public int RunMsOf(EntityHandle who) => (int)Math.Round(MovementFormulas.RunMsPerTile(PaceOf(who)));

        public int WalkMs => (int)Math.Round(MovementFormulas.BaseWalkMsPerTile);

        /// <summary>What each creature was authored as. A test that cares sets one; a body nobody described
        /// ambles, notices nothing, and keeps to no pack, as an unauthored record does.</summary>
        public Dictionary<EntityHandle, (string Behavior, int Group, int Range)> Authored { get; } = [];

        /// <summary>How far back each body holds, for the tests that care. Everything else keeps none.</summary>
        public Dictionary<EntityHandle, int> Standoffs { get; } = [];

        public string BehaviorOf(EntityHandle npc) =>
            !IsInWorld(npc) ? string.Empty
            : Authored.TryGetValue(npc, out var authored) ? authored.Behavior : "wander";

        public int GroupOf(EntityHandle npc) =>
            Authored.TryGetValue(npc, out var authored) ? authored.Group : 0;

        public int RangeOf(EntityHandle npc) =>
            Authored.TryGetValue(npc, out var authored) ? authored.Range : 0;

        public int StandoffOf(EntityHandle npc) =>
            Standoffs.TryGetValue(npc, out int tiles) ? tiles : 0;

        public bool IsChasing(EntityHandle npc) => Chasing.ContainsKey(npc);

        public EntityHandle TargetOf(EntityHandle npc) =>
            Chasing.TryGetValue(npc, out var target) ? target : EntityHandle.None;

        /// <summary>Which guild each body belongs to by NUMBER, what each is called, and what is in its
        /// vault. A test that cares about guilds says so; everything else is in none.</summary>
        public Dictionary<EntityHandle, int> InGuild { get; } = [];
        public Dictionary<int, string> GuildNames { get; } = [];
        public Dictionary<int, long> Vaults { get; } = [];
        public Dictionary<EntityHandle, string> Ranks { get; } = [];

        /// <summary>Each guild's own values, made on first ask — a guild a test named exists.</summary>
        public Dictionary<int, AttributeBag> GuildBags { get; } = [];

        public int GuildNumber(EntityHandle who) => InGuild.TryGetValue(who, out int guild) ? guild : 0;

        public string GuildName(int guild) => GuildNames.TryGetValue(guild, out string? name) ? name : string.Empty;

        public int GuildNamed(string name)
        {
            foreach (var (guild, named) in GuildNames)
            {
                if (string.Equals(named, name, StringComparison.OrdinalIgnoreCase)) return guild;
            }

            return 0;
        }

        public string GuildRankOf(EntityHandle who) =>
            Ranks.TryGetValue(who, out string? rank) ? rank : GuildNumber(who) > 0 ? "member" : string.Empty;

        public AttributeBag? GuildValues(int guild)
        {
            if (guild < 1) return null;
            if (!GuildBags.TryGetValue(guild, out var bag)) GuildBags[guild] = bag = new AttributeBag();
            return bag;
        }

        public bool SetGuildValue(int guild, string key, AttributeValue value)
        {
            if (GuildValues(guild) is not { } bag) return false;

            bag.Set(key, value);
            return true;
        }

        public IReadOnlyList<EntityHandle> MembersOf(int guild) =>
            [.. InGuild.Where(g => g.Value == guild && IsInWorld(g.Key)).Select(g => g.Key)];

        public long GuildGold(int guild) => Vaults.TryGetValue(guild, out long gold) ? gold : 0L;

        /// <summary>What a test says the time is. Moved by hand, so a rule about a window or a
        /// cooldown is asked at a moment the test chose rather than at whatever the clock reads.</summary>
        public long Clock { get; set; } = 1_700_000_000L;

        public long Now() => Clock;

        /// <summary>What each body is wearing, and how worn each piece is. A test that cares sets
        /// them; everything else is wearing nothing.</summary>
        public Dictionary<EntityHandle, List<int>> HasOn { get; } = [];
        public Dictionary<(EntityHandle, int), (int Left, int Full)> Wearing { get; } = [];

        /// <summary>What the repair rate is, per point, for a test that charges for wear.</summary>
        public int RepairPerPoint { get; set; } = 2;

        public double RepairRatePerTier { get; set; } = 0.5;

        public IReadOnlyList<int> WornBy(EntityHandle who) =>
            HasOn.TryGetValue(who, out var on) ? on : [];

        /// <summary>What a test put in each named equip slot, by body.</summary>
        public Dictionary<(EntityHandle, string), int> Equipped { get; } = [];

        public int WornIn(EntityHandle who, string slotKey) =>
            Equipped.TryGetValue((who, slotKey), out int item) ? item : 0;

        public (int Left, int Full) DurabilityOf(EntityHandle who, int itemNum) =>
            Wearing.TryGetValue((who, itemNum), out var dur) ? dur : (0, 0);

        public int Wear(EntityHandle who, int itemNum, int points)
        {
            var (left, full) = DurabilityOf(who, itemNum);
            int taken = Math.Min(Math.Max(points, 0), left);
            if (taken > 0) Wearing[(who, itemNum)] = (left - taken, full);
            return taken;
        }

        public int RepairCost(int itemNum, int points) => Math.Max(0, points) * RepairPerPoint;

        public double RepairRateAt(int tier) => Math.Max(tier, 0) * RepairRatePerTier;

        /// <summary>How far a test has put the server's civil day from UTC. Zero unless it cares.</summary>
        public int Offset { get; set; }

        public int LocalOffset() => Offset;

        /// <summary>What a test says the time of day is. Day unless it cares.</summary>
        public string Hour { get; set; } = "day";

        public string TimeOfDay() => Hour;

        /// <summary>What a game has marked on the ground, by the name it marked under.</summary>
        public Dictionary<string, WorldMarker> Marked { get; } = [];

        public bool Mark(WorldMarker marker)
        {
            if (marker is null || string.IsNullOrWhiteSpace(marker.Id)) return false;

            Marked[marker.Id] = marker;
            return true;
        }

        public bool Unmark(string id) => Marked.Remove(id);

        /// <summary>What a test says SpreadOver answers with, and the maps it says are quiet.</summary>
        public List<WorldPlace> Spread { get; } = [];
        public HashSet<int> Emptied { get; } = [];

        public IReadOnlyList<WorldPlace> SpreadOver(int region, int count, string onlyWhere = "") =>
            [.. Spread.Take(Math.Max(count, 0))];

        public bool Empty(int mapNum) => Emptied.Add(mapNum);

        public bool Refill(int mapNum) => Emptied.Remove(mapNum);

        public bool IsEmptied(int mapNum) => Emptied.Contains(mapNum);

        /// <summary>Which guilds a test says exist, and what was posted to their members.</summary>
        public List<int> AllGuilds { get; } = [];
        public List<(int Guild, int ItemNum, int Quantity, string Subject, bool OnlyActive)> Posted { get; } = [];

        /// <summary>How many members a test says each guild has, for MailMembers to answer with.</summary>
        public Dictionary<int, int> Roster { get; } = [];

        public IReadOnlyList<int> Guilds() => AllGuilds;

        /// <summary>What was posted to one body, and what was attached.</summary>
        public List<(EntityHandle Who, string Subject, int ItemNum, int Quantity)> Letters { get; } = [];

        public bool Mail(EntityHandle who, string subject, string body, int itemNum = 0, int quantity = 0)
        {
            if (!who.IsPlayer || !IsInWorld(who)) return false;

            Letters.Add((who, subject, itemNum, quantity));
            return true;
        }

        /// <summary>What a test says each body's account is called, and who belongs to each guild's
        /// roster whether or not they are here.</summary>
        public Dictionary<EntityHandle, string> Accounts { get; } = [];
        public Dictionary<int, List<string>> Rosters { get; } = [];
        public HashSet<(int Guild, string Account)> Live { get; } = [];
        public List<(string Account, string Subject, int ItemNum, int Quantity)> PostedTo { get; } = [];

        public string AccountOf(EntityHandle who) =>
            Accounts.TryGetValue(who, out string? login) ? login : string.Empty;

        public EntityHandle WhoIs(string account)
        {
            foreach (var (who, login) in Accounts)
            {
                if (login == account && IsInWorld(who)) return who;
            }

            return EntityHandle.None;
        }

        public IReadOnlyList<string> AccountsIn(int guild) =>
            Rosters.TryGetValue(guild, out var roster) ? roster : [];

        public bool IsActiveIn(int guild, string account) => Live.Contains((guild, account));

        public bool MailTo(string account, string subject, string body, int itemNum = 0, int quantity = 0)
        {
            if (string.IsNullOrWhiteSpace(account)) return false;

            PostedTo.Add((account, subject, itemNum, quantity));
            return true;
        }

        public int MailMembers(int guild, int itemNum, int quantity, string subject, string body,
                               bool onlyActive = false)
        {
            if (guild < 1 || itemNum < 1 || quantity < 1) return 0;

            Posted.Add((guild, itemNum, quantity, subject, onlyActive));
            return Roster.TryGetValue(guild, out int many) ? many : 0;
        }

        public bool InsideMark(string id, WorldPlace place)
        {
            if (!Marked.TryGetValue(id, out var found) || found.Radius <= 0 || found.At.Map != place.Map) return false;

            int dx = place.X - found.At.X;
            int dy = place.Y - found.At.Y;

            return dx * dx + dy * dy <= found.Radius * found.Radius;
        }

        /// <summary>What the game kept about the world itself.</summary>
        public AttributeBag WorldBag { get; } = new();

        public AttributeBag WorldValues() => WorldBag;

        public void SetWorldValue(string key, AttributeValue value) => WorldBag.Set(key, value);

        public bool SetRecordValue(string familyId, int num, string key, AttributeValue value)
        {
            if (RecordAt(familyId, num) is not { } row) return false;

            row.Set(key, value);
            return true;
        }

        public bool GiveGuildGold(int guild, long amount)
        {
            if (guild < 1 || amount <= 0) return false;

            Vaults[guild] = GuildGold(guild) + amount;
            return true;
        }

        /// <summary>What was taken out of a vault, so a test can read back what a rule charged.</summary>
        public List<(int Guild, long Amount)> Spent { get; } = [];

        public bool SpendGuildGold(int guild, long amount, EntityHandle by)
        {
            if (guild < 1 || amount <= 0 || GuildGold(guild) < amount) return false;

            Vaults[guild] = GuildGold(guild) - amount;
            Spent.Add((guild, amount));
            return true;
        }

        /// <summary>Read off the same Standing table At answers from, so a test places a body once and both
        /// questions agree about where it is.</summary>
        public List<(WorldPlace At, int ItemNum, int Quantity, EntityHandle ClaimedBy, int ClaimSeconds)> Littered { get; } = [];

        public bool DropAt(WorldPlace at, int itemNum, int quantity = 1,
                           EntityHandle claimedBy = default, int claimSeconds = 0)
        {
            if (itemNum < 1) return false;

            Littered.Add((at, itemNum, Math.Max(quantity, 1), claimedBy, claimSeconds));
            return true;
        }

        public IReadOnlyList<EntityHandle> NpcsNear(WorldPlace at, int tiles) => Near(at, tiles, wanted: true);

        public IReadOnlyList<EntityHandle> PlayersNear(WorldPlace at, int tiles) => Near(at, tiles, wanted: false);

        public IReadOnlyList<EntityHandle> NpcsOn(int mapNum) =>
        [
            .. Standing.Where(s => s.Key.Map == mapNum && s.Value.IsNpc).Select(s => s.Value),
        ];

        private IReadOnlyList<EntityHandle> Near(WorldPlace at, int tiles, bool wanted) =>
        [
            .. Standing
                .Where(s => s.Key.Map == at.Map && s.Value.IsNpc == wanted
                            && Math.Abs(s.Key.X - at.X) + Math.Abs(s.Key.Y - at.Y) <= tiles)
                .OrderBy(s => Math.Abs(s.Key.X - at.X) + Math.Abs(s.Key.Y - at.Y))
                .Select(s => s.Value),
        ];
    }
}
