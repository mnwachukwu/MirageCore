using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;
using System.Reflection;

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
    private static string[] Sections()
    {
        var field = typeof(MainWindowViewModel)
            .GetField("AllSectionNames", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string[])field.GetValue(null)!;
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
            "Maps", "MapGroups", "Items", "NPCs", "Shops", "Spells", "Classes", "Quests",
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
