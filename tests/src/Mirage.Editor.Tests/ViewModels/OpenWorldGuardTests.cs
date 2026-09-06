using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Which folders the editor will open as a world, and where the picker starts.
///
/// <para><b>A world is a folder with a <c>world.json</c> in it.</b> The record directories deliberately do
/// not count — "maps" and "items" are ordinary words, and one of them matching should never be enough to
/// open somebody's documents folder and start writing a world into it. A manifest is a claim a folder
/// makes about itself, and only a world makes it.</para>
///
/// <para>One rule, no exceptions: no manifest, no world. Nothing is inferred from what a folder happens
/// to contain, so there is no case where the editor has to guess what somebody meant.</para>
/// </summary>
[TestFixture]
public class OpenWorldGuardTests
{
    private readonly List<string> _made = [];

    private string Dir(params string[] children)
    {
        string root = Path.Combine(Path.GetTempPath(), "mirage-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _made.Add(root);
        foreach (string c in children)
        {
            if (Path.HasExtension(c)) File.WriteAllText(Path.Combine(root, c), "{}");
            else Directory.CreateDirectory(Path.Combine(root, c));
        }

        return root;
    }

    private string World(params string[] children)
    {
        string root = Dir(children);
        File.WriteAllText(Path.Combine(root, WorldManifest.FileName), """{ "name": "Demo Landia" }""");
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

    private static (MainWindowViewModel Vm, List<string> Asked, List<string> Alerts) Build(bool answer)
    {
        var asked = new List<string>();
        var alerts = new List<string>();
        var vm = new MainWindowViewModel(new EditorDataService(), new EditorConnection(), new EditorBitmapCache())
        {
            ConfirmAsync = msg => { asked.Add(msg); return Task.FromResult(answer); },
            ShowAlertAsync = msg => { alerts.Add(msg); return Task.CompletedTask; },
        };
        return (vm, asked, alerts);
    }

    // ── What a world is ──────────────────────────────────────────────────────

    [Test]
    public async Task AFolderWithAManifest_OpensWithoutAsking()
    {
        var (vm, asked, _) = Build(answer: false);

        await vm.OpenWorldAsync(World("maps"), remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Empty, "a world needs no confirmation");
            Assert.That(vm.HasWorld, Is.True);
        });
    }

    /// <summary>The manifest is the claim, not the contents: a world nobody has authored yet is still a
    /// world, and that is exactly what New World leaves behind.</summary>
    [Test]
    public async Task AManifestAlone_IsAWorld()
    {
        var (vm, asked, _) = Build(answer: false);

        await vm.OpenWorldAsync(World(), remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Empty);
            Assert.That(vm.HasWorld, Is.True);
        });
    }

    /// <summary>The case the rule exists for: a folder that happens to contain a directory sharing a name
    /// with one of ours is not a world, and saying so is the whole point of requiring the manifest.</summary>
    [TestCase("items")]
    [TestCase("maps")]
    [TestCase("npcs")]
    public async Task RecordFolderNamesAlone_DoNotMakeAWorld(string marker)
    {
        var (vm, asked, alerts) = Build(answer: true);
        string looksClose = Dir(marker);

        await vm.OpenWorldAsync(looksClose, remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(asked, Is.Empty, "there is nothing to weigh up — it is not a world");
            Assert.That(alerts, Has.Count.EqualTo(1));
        });
    }

    /// <summary>Even a folder holding every record directory is refused: what makes a world is the
    /// manifest, and nothing writes one except New World.</summary>
    [Test]
    public async Task AFolderOfRecordsWithNoManifest_IsStillRefused()
    {
        var (vm, _, alerts) = Build(answer: true);
        string records = Dir("maps", "items", "npcs", "spells");

        await vm.OpenWorldAsync(records, remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(alerts, Has.Count.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(records, WorldManifest.FileName)), Is.False,
                        "and nothing is written into a folder that was only looked at");
        });
    }

    /// <summary>The folder this rule protects: somebody's documents, picked by mistake. It is turned away
    /// and nothing is written into it.</summary>
    [Test]
    public async Task AFolderOfSomethingElse_IsRefused()
    {
        var (vm, asked, alerts) = Build(answer: true);
        string notAWorld = Dir("holiday-photos", "notes.txt");

        await vm.OpenWorldAsync(notAWorld, remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(asked, Is.Empty, "there is nothing to weigh up");
            Assert.That(alerts, Has.Count.EqualTo(1), "and the author is told why");
            Assert.That(alerts[0], Does.Contain(notAWorld));
        });
    }

    /// <summary>An empty folder is not a world either. Making one is its own command, which is what stops
    /// a mistaken pick from quietly becoming a world on the first save.</summary>
    [Test]
    public async Task AnEmptyFolder_IsRefused()
    {
        var (vm, _, alerts) = Build(answer: true);

        await vm.OpenWorldAsync(Dir(), remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(alerts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AFolderThatIsNotThere_IsReported()
    {
        var (vm, asked, alerts) = Build(answer: true);
        string gone = Path.Combine(Path.GetTempPath(), "mirage-gone-" + Guid.NewGuid().ToString("N"));

        await vm.OpenWorldAsync(gone, remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(alerts, Has.Count.EqualTo(1));
            Assert.That(asked, Is.Empty);
            Assert.That(vm.HasWorld, Is.False);
        });
    }

    // ── Where the picker starts ──────────────────────────────────────────────

    /// <summary>Worlds live wherever an operator keeps them, which is rarely beside the application. The
    /// folder a world was chosen FROM is remembered, so the next pick opens among its siblings.</summary>
    [Test]
    public async Task OpeningAWorld_RemembersTheFolderItCameFrom()
    {
        var (vm, _, _) = Build(answer: true);
        string world = World("maps");

        await vm.OpenWorldAsync(world, remember: true);

        Assert.That(AppSettings.Current.LastWorldBrowsePath,
                    Is.EqualTo(Path.GetDirectoryName(world)).IgnoreCase);
    }
}
