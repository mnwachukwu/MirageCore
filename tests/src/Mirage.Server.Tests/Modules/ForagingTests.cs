using Mirage.Modules.Foraging;
using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// Foraging, both ways, against one set of assertions.
///
/// <para>🔴 <b>This is what keeps the tutorial honest.</b> The First Game page on the site shows both
/// of these files, and the site generates those snippets from the files themselves. A page can only
/// show code that compiles and declares what the prose says it declares, because the code is here and
/// this test reads it.</para>
///
/// <para>Neither module is loaded by the shipped server. They declare the same attribute key and the
/// same records, so loading both would stop the server at startup — which is the engine working.</para>
/// </summary>
[TestFixture]
public class ForagingTests
{
    /// <summary>The repository root, found by walking up to the solution file.</summary>
    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);

        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? throw new InvalidOperationException("The repository root is not above here.");
    }

    private static string World() => Path.Combine(Repository(), "modules", "foraging", "world");

    /// <summary>What both routes must produce. Called twice, once per registry.</summary>
    private static void HoldsTheGame(CoreRegistry registry, string family)
    {
        var berries = registry.Schema.Families.Single(f => f.Id == family);
        var pick = registry.Actions.All.Single();

        Assert.Multiple(() =>
        {
            registry.Attributes.TryGet(ForageModule.Baskets, out var baskets);
            Assert.That(baskets.Visibility, Is.EqualTo(AttributeVisibility.Owner),
                "a basket count is nobody else's business");

            Assert.That(berries.LabelKey, Is.EqualTo("Berries"));
            Assert.That(berries.DefaultLimit, Is.EqualTo(50));
            Assert.That(berries.Fields.Select(f => f.Key),
                Is.EqualTo(new[] { "name", "ripeness", "where" }).AsCollection);
            Assert.That(berries.Fields[0].MaxLength, Is.EqualTo(40));
            Assert.That(berries.Fields[1].Kind, Is.EqualTo(FieldKind.Choice));
            Assert.That(berries.Fields[2].LabelKey, Is.EqualTo("Found where"));

            Assert.That(registry.Schema.Choices(berries.Fields[1].ChoiceSetId!)!.Members,
                Has.Count.EqualTo(3), "green, ripe, overripe");

            Assert.That(registry.DisplayFields.For(DisplaySurfaces.Hud).Select(r => r.LabelKey),
                Is.EqualTo(new[] { "Foraging", "Baskets" }).AsCollection);

            Assert.That(pick.Id, Is.EqualTo(ForageModule.Pick));
            Assert.That(pick.LabelKey, Is.EqualTo("Pick here"));
            Assert.That(pick.Key, Is.EqualTo("Q"));
            Assert.That(pick.Surface, Is.EqualTo(ActionSurface.Tile));
        });
    }

    [Test]
    public void TheScriptDeclaresTheGame()
    {
        var module = new ScriptedWorldModule(World());
        var registry = CoreRegistry.Build(module);

        using ScriptedWorldModule scripts = module;

        Assert.That(module.Problems.Select(p => p.Message), Is.Empty,
            "Foraging's script does not load cleanly");

        // The script's family id is the model's own name; the C# one is lowercase because that
        // module wrote it out by hand. Same records either way.
        HoldsTheGame(registry, "Berry");
    }

    [Test]
    public void TheModuleDeclaresTheSameGame() =>
        HoldsTheGame(CoreRegistry.Build(new ForageModule()), ForageModule.Berries);

    /// <summary>⚠ The script's handlers have to match the table, or the engine never calls them.</summary>
    [Test]
    public void TheScriptsHandlersAreOnesTheEngineTakes()
    {
        var module = new ScriptedWorldModule(World());

        // Building the registry is what runs Configure, which is what reads and loads the scripts.
        // Constructing the module alone loads nothing.
        CoreRegistry.Build(module);

        using ScriptedWorldModule scripts = module;

        Assert.Multiple(() =>
        {
            Assert.That(module.IsLoaded, Is.True);
            Assert.That(module.Offered,
                Is.SupersetOf(new[] { "Configure", "OnPlayerJoined", "OnAction" }));
        });
    }

    /// <summary>🔴 Neither is in the shipped server's list, and both declare the same key.</summary>
    [Test]
    public void NeitherIsLoadedByTheShippedServer()
    {
        var names = CoreRegistry.Build(Host.GameModules.Load("no-such-world")).ModuleNames;

        Assert.That(names, Does.Not.Contain("Foraging"),
            "Foraging is a worked example, not a game the server runs");
    }
}
