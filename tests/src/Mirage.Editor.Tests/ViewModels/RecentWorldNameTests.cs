using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The recent-worlds menu shows a world's name, and says an unnamed one is unnamed.
///
/// <para>A name is what an operator picked to tell one world from a copy of it, so it identifies an entry
/// better than any part of a path can — two checkouts of the same world differ by a directory somewhere in
/// the middle, which is exactly the part a shortened path drops. An unnamed world is named for the reader
/// instead, and carries its folder so several of them do not read alike.</para>
/// </summary>
[TestFixture]
public class RecentWorldNameTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteManifest(WorldManifest m) =>
        File.WriteAllText(Path.Combine(_dir, WorldManifest.FileName), JsonSerializer.Serialize(m));

    private RecentWorldViewModel Row() => new(_dir, _ => Task.CompletedTask);

    [Test]
    public void ANamedWorld_ShowsItsName()
    {
        WriteManifest(new WorldManifest { Name = "Demo Landia" });

        Assert.That(Row().Display, Is.EqualTo("Demo Landia"));
    }

    [TestCase(null, Description = "no manifest at all")]
    [TestCase("", Description = "a manifest with no name")]
    [TestCase("   ", Description = "a name that is only spaces")]
    public void AnUnnamedWorld_IsCalledUntitled(string? name)
    {
        if (name is not null) WriteManifest(new WorldManifest { Name = name });

        Assert.That(Row().Display, Does.Contain("Untitled"));
    }

    /// <summary>Two unnamed worlds must not read alike, or the menu is a column of identical rows.</summary>
    [Test]
    public void UnnamedWorlds_AreStillTellableApart()
    {
        string other = Path.Combine(Path.GetTempPath(), "mirage-recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            var a = Row();
            var b = new RecentWorldViewModel(other, _ => Task.CompletedTask);

            Assert.That(a.Display, Is.Not.EqualTo(b.Display));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    /// <summary>A folder moved out from under the list still produces a row rather than throwing — the
    /// menu is built before anything checks whether the path still resolves.</summary>
    [Test]
    public void AMissingFolder_StillProducesARow()
    {
        string gone = Path.Combine(Path.GetTempPath(), "mirage-gone-" + Guid.NewGuid().ToString("N"));

        var row = new RecentWorldViewModel(gone, _ => Task.CompletedTask);

        Assert.Multiple(() =>
        {
            Assert.That(row.Display, Is.Not.Empty);
            Assert.That(row.Path, Is.EqualTo(gone), "the whole path stays available for the tooltip");
        });
    }

    /// <summary>The tooltip keeps the whole path whatever the header shows, so two worlds sharing a name
    /// are still tellable apart.</summary>
    [Test]
    public void TheWholePath_IsAlwaysAvailable()
    {
        WriteManifest(new WorldManifest { Name = "Live" });

        Assert.That(Row().Path, Is.EqualTo(_dir));
    }

    /// <summary>
    /// 🔴 A path written with the OTHER platform's separator still gives up its folder. **Do not reduce this
    /// to one case.**
    ///
    /// <para>The recent-worlds list is a settings file that travels, so the machine reading it is no guide to
    /// which slash wrote it. `Path.DirectorySeparatorChar` and `AltDirectorySeparatorChar` are BOTH `/` on
    /// Linux and macOS, so anything keyed on them is blind to a backslash there and the whole path reads as
    /// the folder name.</para>
    ///
    /// <para>Both shapes are asserted on every platform rather than only the local one — that is what makes
    /// this catchable here instead of on a CI leg. Neither folder exists, which is the case the menu has to
    /// survive anyway, and is why one fixture covers both.</para>
    /// </summary>
    [TestCase(@"D:\worlds\Brightwater", Description = "written on Windows")]
    [TestCase("/srv/worlds/Brightwater", Description = "written on Linux or macOS")]
    public void AForeignSeparator_StillYieldsTheFolder(string path)
    {
        var row = new RecentWorldViewModel(path, _ => Task.CompletedTask);

        Assert.That(row.Display, Does.Contain("Brightwater"));
    }

    [TestCase(@"D:\worlds\Brightwater\", Description = "written on Windows")]
    [TestCase("/srv/worlds/Brightwater/", Description = "written on Linux or macOS")]
    public void AForeignTrailingSeparator_DoesNotCostTheFolder(string path)
    {
        var row = new RecentWorldViewModel(path, _ => Task.CompletedTask);

        Assert.That(row.Display, Does.Contain("Brightwater"));
    }
}
