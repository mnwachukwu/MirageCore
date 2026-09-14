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
    /// and one typed as another model is a picker over that model's records. What is left is what a
    /// model cannot say, and the model says it in <c>Describe</c>.</para>
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
                "nothing half-declared: the section is absent, and so is the reason it is absent");
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
                                        new WorldPlace(3, 11, 4));

        Assert.That(world.Said, Is.EqualTo(new[] { "harvest.gather at 3:11,4" }));
    }

    /// <summary>A verb offered ON somebody tells the script who that was.
    ///
    /// <para>The name rather than the body, because the boundary cannot carry "somebody, or nobody" —
    /// and blank is the answer for the square and HUD surfaces, which is most verbs. A script that got
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
        handler.Invoke(Someone, "harvest.greet", Someone, new WorldPlace(1, 1, 1));
        handler.Invoke(Someone, "harvest.greet", EntityHandle.None, new WorldPlace(1, 1, 1));

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
            // signature that drifts from the table is a function nobody calls: it compiles, it loads,
            // and the verb it served quietly stops working. That is what changing OnAction's arity did.
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
    /// Stands in for the engine, recording what a script asked it to do. The module is what is under
    /// test here; what <c>ServerWorld</c> does with a <c>Tell</c> is pinned by its own tests.
    /// </summary>
    /// <summary>Shared with the scripted-Survey fixture, which needs a world to hand a module.</summary>
    internal sealed class RecordingWorld : IWorld
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

        public string NameOf(EntityHandle who) => who.IsSet ? who.ToString() : string.Empty;
    }
}
