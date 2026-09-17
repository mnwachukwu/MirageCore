using Mirage.Scripting;
using NUnit.Framework;
using Mirage.Server.Host.Scripting;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// Survey, declared entirely in Compass, checked against the engine it declares into.
///
/// <para>🔴 <b>This is the whole claim of the scripted route, so it is held to the same bar as the
/// compiled one.</b> Not "the files compile" — what the engine ends up holding: the same attributes,
/// the same records, the same panel, the same verbs on the same surfaces. A seam the script cannot
/// reach shows up here as a thing the registry does not have.</para>
///
/// <para>It is also the multi-file case. The rules live in seven files across three folders, and the
/// engine is handed one module: a folder is an organizing idea rather than a boundary.</para>
/// </summary>
[TestFixture]
public class ScriptedSurveyTests
{
    /// <summary>The shipped Survey scripts, compiled and declared into a registry.</summary>
    private static (ScriptedWorldModule Module, CoreRegistry Registry) Built()
    {
        string world = Path.Combine(
            Repository(), "server", "src", "Mirage.Server.Host", "world");
        var module = new ScriptedWorldModule(world);

        return (module, CoreRegistry.Build(module));
    }

    private static AttributeVisibility Visibility(CoreRegistry registry, string key)
    {
        registry.Attributes.TryGet(key, out var declared);
        return declared.Visibility;
    }

    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);

        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? throw new InvalidOperationException("The repository root is not above here.");
    }

    /// <summary>🔴 They compile, and every handler they wrote is one the engine took.
    ///
    /// <para>A handler is matched by NAME AND ARITY, so a signature that drifts never gets called:
    /// it compiles, it loads, and the rule it served quietly stops working.</para></summary>
    [Test]
    public void TheScriptsCompile_AndEveryHandlerTheyWroteWasTaken()
    {
        var (module, _) = Built();
        using ScriptedWorldModule scripts = module;

        string[] written = File.ReadAllLines(Path.Combine(
                Repository(), "server", "src", "Mirage.Server.Host", "world", "scripts", "rules.cm"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("public ", StringComparison.Ordinal) && l.Contains("function On", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("function On", StringComparison.Ordinal) + "function ".Length)..])
            .Select(l => l[..l.IndexOf('(')])
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(module.Problems.Where(p => p.Severity == ScriptSeverity.Error), Is.Empty,
                "Survey's rules do not compile:\n" + string.Join("\n", module.Problems));
            Assert.That(module.IsLoaded, Is.True);

            Assert.That(written, Is.Not.Empty, "no handler was found to check");
            Assert.That(module.Offered, Is.SupersetOf(written),
                "Survey wrote a handler the engine did not take \u2014 check its arity against "
                + "ScriptedWorldModule.Handlers");
        });
    }

    /// <summary>Nothing it declared was refused.</summary>
    [Test]
    public void NothingItDeclaredWasRefused()
    {
        var (module, _) = Built();
        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Select(p => p.Message), Is.Empty);
    }

    /// <summary>What a surveyor carries, and where.</summary>
    [Test]
    public void ItDeclaresWhatASurveyorCarries()
    {
        var (module, registry) = Built();
        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(Visibility(registry, "stamina"), Is.EqualTo(AttributeVisibility.Viewport));
            Assert.That(Visibility(registry, "staminaMax"), Is.EqualTo(AttributeVisibility.Viewport));
            Assert.That(Visibility(registry, "rank"), Is.EqualTo(AttributeVisibility.Viewport));
            Assert.That(Visibility(registry, "specimens"), Is.EqualTo(AttributeVisibility.Owner));

            Assert.That(registry.EquipSlots.Slots.Single().LabelKey, Is.EqualTo("Satchel"));
            Assert.That(registry.OverheadBars.Bars.Single().ValueKey, Is.EqualTo("stamina"));
        });
    }

    /// <summary>The species a surveyor is looking for, and the drop-down the enumeration became.</summary>
    [Test]
    public void ItDeclaresTheSpeciesAndTheirHabitats()
    {
        var (module, registry) = Built();
        using ScriptedWorldModule scripts = module;

        var species = registry.Schema.Families.Single(f => f.Id == "Species");
        var habitats = registry.Schema.Choices("Species.habitat")!;

        Assert.Multiple(() =>
        {
            Assert.That(species.LabelKey, Is.EqualTo("Species"));
            Assert.That(species.DefaultLimit, Is.EqualTo(200));

            Assert.That(species.Fields.Select(f => f.Key),
                Is.EqualTo(new[] { "name", "habitat", "notes" }).AsCollection);
            Assert.That(species.Fields[0].LabelKey, Is.EqualTo("Common name"));
            Assert.That(species.Fields[0].MaxLength, Is.EqualTo(40));
            Assert.That(species.Fields[1].Kind, Is.EqualTo(FieldKind.Choice));

            Assert.That(habitats.Members.Select(m => m.Id),
                Is.EqualTo(new[] { "Shore", "Woodland", "Meadow" }).AsCollection,
                "the enumeration's members ARE the set, and nothing declared one");
        });
    }

    /// <summary>The field book, its rows, and the verb that opens it.</summary>
    [Test]
    public void ItDeclaresTheFieldBook()
    {
        var (module, registry) = Built();
        using ScriptedWorldModule scripts = module;

        var book = registry.Panels.All.Single();
        var rows = registry.DisplayFields.For(book.Surface);

        Assert.Multiple(() =>
        {
            Assert.That(book.Id, Is.EqualTo("survey.fieldbook"));
            Assert.That(book.Key, Is.EqualTo("B"));
            Assert.That(book.Buttons.Single().ActionId, Is.EqualTo("survey.note"));

            Assert.That(rows.Select(r => r.LabelKey),
                Is.EqualTo(new[] { "Field record", "Rank", "Specimens cataloged", "Stamina" }).AsCollection);
            Assert.That(rows.Single(r => r.LabelKey == "Stamina").Style, Is.EqualTo(DisplayStyle.Meter));

            var opener = registry.Actions.All.Single(a => a.Id == "survey.openbook");
            Assert.That(opener.Surface, Is.EqualTo(ActionSurface.Hud));
            Assert.That(opener.OpensPanel, Is.EqualTo("survey.fieldbook"));
        });
    }

    /// <summary>Every verb, on the surface it belongs to, with the one condition this game has.</summary>
    [Test]
    public void ItDeclaresItsVerbsOnTheSurfacesTheyBelongTo()
    {
        var (module, registry) = Built();
        using ScriptedWorldModule scripts = module;

        var carrying = new AttributeBag();
        carrying.Set("specimens", AttributeValue.From(3));

        var compare = registry.Actions.All.Single(a => a.Id == "survey.compare");

        Assert.Multiple(() =>
        {
            Assert.That(registry.Actions.All.Single(a => a.Id == "survey.note").Key, Is.EqualTo("Q"));
            Assert.That(registry.Actions.All.Single(a => a.Id == "survey.note").Surface,
                Is.EqualTo(ActionSurface.Tile));
            Assert.That(registry.Actions.All.Single(a => a.Id == "survey.identify").Surface,
                Is.EqualTo(ActionSurface.Npc));

            Assert.That(compare.Surface, Is.EqualTo(ActionSurface.Player));
            Assert.That(compare.When.Holds(carrying), Is.True, "a surveyor with notes");
            Assert.That(compare.When.Holds(new AttributeBag()), Is.False, "and one with none");
        });
    }

    /// <summary>The sidebar, in the order it is read in.</summary>
    [Test]
    public void ItDeclaresTheSidebar()
    {
        var (module, registry) = Built();
        using ScriptedWorldModule scripts = module;

        Assert.That(registry.DisplayFields.For(DisplaySurfaces.Hud).Select(r => r.LabelKey),
            Is.EqualTo(new[] { "Survey", "Rank", "Specimens", "Stamina" }).AsCollection);
    }

    /// <summary>Nobody dies, bodies linger, and the tick comes round at the rate the rules asked for.</summary>
    [Test]
    public void ItDecidesDeath_Lingering_AndHowOftenItIsAsked()
    {
        var (module, _) = Built();
        using ScriptedWorldModule scripts = module;
        scripts.Start(new ScriptedWorldTests.RecordingWorld());

        var who = EntityHandle.ForPlayer(1);

        Assert.Multiple(() =>
        {
            Assert.That(scripts.MayDie(new Death(who, EntityHandle.None, "fell")).Allowed, Is.False);
            Assert.That(scripts.LingerFor(who).IsSet, Is.True);
            Assert.That(scripts.EveryTicks, Is.EqualTo(20));
        });
    }
}
