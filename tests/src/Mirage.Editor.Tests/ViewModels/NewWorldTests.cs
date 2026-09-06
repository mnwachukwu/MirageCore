using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Making a world: a name, then somewhere to keep it.
///
/// <para><b>The name is the folder's name.</b> A world called Demo Landia is a folder called Demo Landia,
/// made inside the one that was picked — so the picker chooses where a world goes, never what it is poured
/// into, and a world is never left loose among whatever else was in there.</para>
///
/// <para>Creation is a command rather than a side effect: this is the only thing that writes a manifest
/// into a folder that had none, which is what keeps a mistaken pick from quietly becoming a world.</para>
/// </summary>
[TestFixture]
public class NewWorldTests
{
    private readonly List<string> _made = [];

    private string EmptyDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "mirage-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _made.Add(root);
        return root;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string d in _made.Where(Directory.Exists)) Directory.Delete(d, recursive: true);
        _made.Clear();
        EditorPaths.OpenWorld("");
        AppSettings.Current.LastWorldBrowsePath = null;
    }

    /// <param name="name">null = backed out of the name prompt.</param>
    /// <param name="folder">null = backed out of the folder picker.</param>
    private (MainWindowViewModel Vm, List<string> Asked, List<string> Alerts) Build(
        string? name, string? folder, bool confirm = true)
    {
        var asked = new List<string>();
        var alerts = new List<string>();
        var vm = new MainWindowViewModel(new EditorDataService(), new EditorConnection(), new EditorBitmapCache())
        {
            AskNewWorldNameAsync = _ => Task.FromResult(name),
            PickWorldFolderAsync = _ => Task.FromResult(folder),
            ConfirmAsync = msg => { asked.Add(msg); return Task.FromResult(confirm); },
            ShowAlertAsync = msg => { alerts.Add(msg); return Task.CompletedTask; },
        };
        return (vm, asked, alerts);
    }

    private static WorldManifest Read(string folder) =>
        System.Text.Json.JsonSerializer.Deserialize<WorldManifest>(
            File.ReadAllText(Path.Combine(folder, WorldManifest.FileName)),
            Mirage.Shared.Serialization.RecordJson.Options)!;

    [Test]
    public async Task ANamedWorld_LandsInAFolderOfThatName()
    {
        string parent = EmptyDir();
        var (vm, _, _) = Build("Demo Landia", parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        string world = Path.Combine(parent, "Demo Landia");
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(world), Is.True, "the name is the folder's name");
            Assert.That(File.Exists(Path.Combine(world, WorldManifest.FileName)), Is.True);
            Assert.That(Read(world).Name, Is.EqualTo("Demo Landia"));
            Assert.That(vm.HasWorld, Is.True, "the world it just made is the one it opens");
            Assert.That(vm.WorldLabel, Is.EqualTo("Demo Landia"));
        });
    }

    /// <summary>The picked folder holds the world, and is not the world. Whatever else was in there is
    /// beside it and untouched.</summary>
    [Test]
    public async Task ThePickedFolder_IsNotWrittenInto()
    {
        string parent = EmptyDir();
        File.WriteAllText(Path.Combine(parent, "notes.txt"), "hello");
        var (vm, asked, _) = Build("Demo Landia", parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Empty, "a folder to keep a world in needs no warning about its contents");
            Assert.That(File.Exists(Path.Combine(parent, WorldManifest.FileName)), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(parent, "notes.txt")), Is.EqualTo("hello"));
            Assert.That(Directory.Exists(Path.Combine(parent, "Demo Landia")), Is.True);
        });
    }

    /// <summary>A second world of the same name in the same place is a failure, not a question: the answer
    /// is a different name, which is not something a yes/no can supply.</summary>
    [Test]
    public async Task ANameAlreadyUsedThere_Fails()
    {
        string parent = EmptyDir();
        Directory.CreateDirectory(Path.Combine(parent, "Demo Landia"));
        var (vm, asked, alerts) = Build("Demo Landia", parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(alerts, Has.Count.EqualTo(1));
            Assert.That(asked, Is.Empty);
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(File.Exists(Path.Combine(parent, "Demo Landia", WorldManifest.FileName)), Is.False,
                        "and nothing is written into what was already there");
        });
    }

    /// <summary>A name has to be usable as a folder name, since that is what it becomes.</summary>
    [TestCase("what/now")]
    [TestCase("a:b")]
    [TestCase("why?")]
    public async Task ANameAFolderCannotHave_Fails(string name)
    {
        string parent = EmptyDir();
        var (vm, _, alerts) = Build(name, parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(alerts, Has.Count.EqualTo(1));
            Assert.That(Directory.GetDirectories(parent), Is.Empty);
        });
    }

    /// <summary>A world that chose nothing writes an empty object, which reads as every default — the
    /// same answer as an absent file, and still unambiguously a world by being there at all. Its folder is
    /// called what the window would call it, so the two agree.</summary>
    [Test]
    public async Task AnUnnamedWorld_WritesAnEmptyObjectInAnUntitledFolder()
    {
        string parent = EmptyDir();
        var (vm, _, _) = Build("", parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        string world = Directory.GetDirectories(parent).Single();
        string json = File.ReadAllText(Path.Combine(world, WorldManifest.FileName));
        Assert.Multiple(() =>
        {
            Assert.That(json.Replace(" ", "").Replace("\r", "").Replace("\n", ""), Is.EqualTo("{}"));
            Assert.That(Path.GetFileName(world), Does.Contain("Untitled"));
            Assert.That(Read(world).IsNamed, Is.False, "blank stays blank in the record");
            Assert.That(vm.WorldLabel, Does.Contain("Untitled"), "and the window supplies the word");
        });
    }

    /// <summary>Only what differs from the stock answers, plus the name.</summary>
    [Test]
    public async Task AFreshWorld_WritesNothingItDidNotChoose()
    {
        string parent = EmptyDir();
        var (vm, _, _) = Build("Demo Landia", parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        string json = File.ReadAllText(Path.Combine(parent, "Demo Landia", WorldManifest.FileName));
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("records"));
            Assert.That(json, Does.Not.Contain("defaultMapSize"));
        });
    }

    [Test]
    public async Task BackingOutOfTheName_MakesNothing()
    {
        string parent = EmptyDir();
        var (vm, _, _) = Build(null, parent);

        await vm.NewWorldCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
            Assert.That(vm.HasWorld, Is.False);
        });
    }

    [Test]
    public async Task BackingOutOfTheFolder_MakesNothing()
    {
        var (vm, _, _) = Build("Demo Landia", null);

        await vm.NewWorldCommand.ExecuteAsync(null);

        Assert.That(vm.HasWorld, Is.False);
    }

}
