using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The record folders a world is made of: created with it, and put back if they go missing.
///
/// <para>They exist EMPTY. A slot with no file is a blank record, not a missing one, so nothing is written
/// until something is authored — what the folders do is claim the names, so an operator looking at a world
/// sees the shape it will take and cannot make one of their own that the editor would collide with.</para>
///
/// <para>Because they are restored on every open, emptying a world is deleting everything but the
/// manifest: the folders come back and the records are what is gone.</para>
/// </summary>
[TestFixture]
public class WorldFoldersTests
{
    private readonly List<string> _made = [];

    private string NewDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "mirage-folders-" + Guid.NewGuid().ToString("N"));
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
    }

    private static MainWindowViewModel Vm() =>
        new(new EditorDataService(), new EditorConnection(), new EditorBitmapCache())
        {
            ShowAlertAsync = _ => Task.CompletedTask,
            ConfirmAsync = _ => Task.FromResult(true),
        };

    [Test]
    public async Task ACreatedWorld_HasEveryRecordFolder()
    {
        string root = NewDir();

        await EditorDataService.CreateWorldAsync(root, new WorldManifest { Name = "Demo Landia" });

        Assert.That(EditorDataService.RecordFolders.Where(f => !Directory.Exists(Path.Combine(root, f))),
                    Is.Empty);
    }

    /// <summary>Empty, and staying that way. A folder of blank files would be a thousand records claiming
    /// to exist.</summary>
    [Test]
    public async Task ACreatedWorld_HasNoRecordFilesAtAll()
    {
        string root = NewDir();

        await EditorDataService.CreateWorldAsync(root, new WorldManifest());

        Assert.Multiple(() =>
        {
            foreach (string f in EditorDataService.RecordFolders)
                Assert.That(Directory.EnumerateFileSystemEntries(Path.Combine(root, f)), Is.Empty, f);

            Assert.That(Directory.EnumerateFiles(root).Select(Path.GetFileName),
                        Is.EquivalentTo(new[] { WorldManifest.FileName }),
                        "the manifest is the only file a new world has");
        });
    }

    /// <summary>The wipe: everything but the manifest deleted. Opening it puts the shape back.</summary>
    [Test]
    public async Task AWorldStrippedToItsManifest_IsRepairedOnOpen()
    {
        string root = NewDir();
        await EditorDataService.CreateWorldAsync(root, new WorldManifest { Name = "Demo Landia" });
        foreach (string d in Directory.GetDirectories(root)) Directory.Delete(d, recursive: true);

        await Vm().OpenWorldAsync(root, remember: false);

        Assert.That(EditorDataService.RecordFolders.Where(f => !Directory.Exists(Path.Combine(root, f))),
                    Is.Empty);
    }

    [Test]
    public async Task OneMissingFolder_ComesBackWithoutTouchingTheRest()
    {
        string root = NewDir();
        await EditorDataService.CreateWorldAsync(root, new WorldManifest());
        File.WriteAllText(Path.Combine(root, "items", "item1.json"), """{ "name": "Sword" }""");
        Directory.Delete(Path.Combine(root, "maps"));

        await Vm().OpenWorldAsync(root, remember: false);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(root, "maps")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "items", "item1.json")), Is.True,
                        "repairing the shape must not disturb the records");
        });
    }

    /// <summary>Accounts are the server's, not the world folder's — the account editor says so itself and
    /// shows nothing offline. A world folder claiming the name would be claiming something it does not own.</summary>
    [Test]
    public void TheRuntimeFolders_AreNotAWorldsToMake()
    {
        Assert.That(EditorDataService.RecordFolders,
                    Has.No.Member("accounts").And.No.Member("guilds").And.No.Member("market")
                       .And.No.Member("trades").And.No.Member("seasons").And.No.Member("map_items"));
    }

    /// <summary>Two lists name the same folders, and a section added to one and not the other would either
    /// go unclaimed or be claimed with nothing to put in it.</summary>
    [Test]
    public void TheFolderList_MatchesTheSectionsThatHaveOne()
    {
        var sections = typeof(MainWindowViewModel)
            .GetField("SectionFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as Dictionary<string, string>;

        Assert.That(sections, Is.Not.Null);
        Assert.That(EditorDataService.RecordFolders,
                    Is.EquivalentTo(sections!.Values.Where(v => v != "accounts")));
    }
}
