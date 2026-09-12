using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;
using System.Reflection;

using Mirage.Editor.Services;
namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// The nav rail is built from the record-family table plus accounts, so a family registered there gets
/// a section without the shell being edited.
///
/// <para>Two things are worth asserting rather than assuming. The section ids are persisted — the
/// per-section auto-save schedule is keyed by them — so a changed id reads as a section nobody has
/// configured rather than as an error. And every section needs a label key that actually resolves,
/// because a key with no string behind it throws on the first paint in DEBUG and renders as
/// <c>[Key_Name]</c> in a shipped build.</para>
/// </summary>
[TestFixture]
public class SectionCompositionTests
{
    private static string[] Sections() => Sections(WorldFamilies.All);

    private static string[] Sections(IReadOnlyList<RecordFamily> families)
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("SectionNamesFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string[])method.Invoke(null, [families])!;
    }

    private static string LabelKey(string id)
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("SectionLabelKey", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [id])!;
    }

    /// <summary>The ids and their order, written out rather than derived — deriving them would only
    /// prove the derivation agrees with itself, and these strings are what an author's settings are
    /// filed under.</summary>
    [Test]
    public void TheRailListsTheWorldFamiliesInOrderThenAccounts()
    {
        Assert.That(Sections(), Is.EqualTo(new[]
        {
            "Maps", "MapGroups", "Items", "NPCs", "Shops", "Spells", "Quests",
            "Conversations", "Accounts",
        }));
    }

    [Test]
    public void EveryWorldFamilyHasASection()
    {
        Assert.That(Sections(), Is.SupersetOf(CoreRecordFamilies.World.Select(f => f.Id)));
    }

    /// <summary>Accounts belong to the installation, not the world: they are authored here but never
    /// travel with a world folder.</summary>
    [Test]
    public void AccountsIsASectionButNotAWorldFamily()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Sections(), Does.Contain(MainWindowViewModel.AccountsSection));
            Assert.That(CoreRecordFamilies.Find(MainWindowViewModel.AccountsSection), Is.Null);
        });
    }

    // ── A family a module declared ────────────────────────────────────────────

    /// <summary>The rail is the SERVER's family list, not this build's. A family the editor was never
    /// compiled against still gets a row, or nobody can see that the world has it.</summary>
    [Test]
    public void AFamilyThisBuildNeverHeardOf_StillGetsARow()
    {
        var families = new List<RecordFamily>(WorldFamilies.All)
        {
            new() { Id = "Species", Directory = "species", LabelKey = "Pocket_Section_Species" },
        };

        var rail = Sections(families);

        Assert.Multiple(() =>
        {
            Assert.That(rail, Does.Contain("Species"));
            Assert.That(rail[^1], Is.EqualTo(MainWindowViewModel.AccountsSection), "Accounts stays last");
            Assert.That(Array.IndexOf(rail, "Species"), Is.LessThan(Array.IndexOf(rail, MainWindowViewModel.AccountsSection)));
        });
    }

    /// <summary>🔴 A module's label key is not in any of this editor's language files, by definition.
    /// <c>EditorStrings.Get</c> throws on an unknown key in DEBUG, so resolving one the normal way would
    /// crash the editor the moment it connected to a game that declares a family.</summary>
    [Test]
    public void AModulesLabelKey_FallsBackInsteadOfThrowing()
    {
        EditorStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));

        Assert.Multiple(() =>
        {
            Assert.That(() => EditorStrings.Get("Pocket_Section_Species"), Throws.Exception,
                        "the editor's own text still fails loudly on a missing key");
            Assert.That(EditorStrings.GetOrFallback("Pocket_Section_Species", "Species"), Is.EqualTo("Species"));
        });
    }

    [Test]
    public void EverySectionLabelResolvesToRealText()
    {
        EditorStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));

        Assert.Multiple(() =>
        {
            foreach (string id in Sections())
            {
                string key = LabelKey(id);
                Assert.That(key, Is.Not.Empty, id);
                Assert.That(EditorStrings.Get(key), Is.Not.Empty, $"{id} -> {key}");
            }
        });
    }

    /// <summary>Each family's label comes off its own row, which is what lets a game name a section the
    /// editor has never heard of.</summary>
    [Test]
    public void AFamilysLabelComesFromItsRow()
    {
        Assert.Multiple(() =>
        {
            foreach (var family in CoreRecordFamilies.World)
            {
                Assert.That(LabelKey(family.Id), Is.EqualTo(family.LabelKey), family.Id);
            }
        });
    }
}
