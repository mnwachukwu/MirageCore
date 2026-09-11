using Mirage.Editor.Localization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// How a game layer names the things it adds.
///
/// <para>The editor's own catalog is a closed set, asserted both ways in all four languages: a key
/// with no constant fails as an orphan, and a constant with no key fails as missing. A game therefore
/// cannot put its strings there. It ships its own folder and registers it, and its families, sections
/// and fields carry keys that resolve out of it — leaving the editor's own catalog exactly as strict
/// as it was.</para>
/// </summary>
[TestFixture]
public class ExtraCatalogTests
{
    private string _catalog = string.Empty;

    private static string EditorLang => Path.Combine(AppContext.BaseDirectory, "lang");

    [SetUp]
    public void SetUp()
    {
        _catalog = Path.Combine(Path.GetTempPath(), "mirage-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_catalog);
    }

    [TearDown]
    public void TearDown()
    {
        // Registration is process-wide, so put the editor back on its own catalog for the next fixture.
        ResetCatalogs();
        EditorStrings.Load(EditorLang);
        if (Directory.Exists(_catalog)) Directory.Delete(_catalog, recursive: true);
    }

    private static void ResetCatalogs()
    {
        var field = typeof(EditorStrings).GetField("_extraCatalogs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        ((List<string>)field.GetValue(null)!).Clear();
    }

    private void WriteCatalog(string langCode, params (string Key, string Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => e.Value);
        File.WriteAllText(Path.Combine(_catalog, $"{langCode}.json"), JsonSerializer.Serialize(dict));
    }

    [Test]
    public void AGamesStringResolvesWithoutTouchingTheEditorsCatalog()
    {
        WriteCatalog("en", ("Game_Section_Creatures", "Creatures"));
        EditorStrings.Load(EditorLang);

        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Get("Game_Section_Creatures"), Is.EqualTo("Creatures"));
    }

    [Test]
    public void TheEditorsOwnStringsStillResolve()
    {
        WriteCatalog("en", ("Game_Section_Creatures", "Creatures"));
        EditorStrings.Load(EditorLang);
        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Get(EditorStrings.MainWindow_Section_Maps), Is.Not.Empty);
    }

    /// <summary>Later catalogs win, so a game may reword something the editor ships.</summary>
    [Test]
    public void AGameMayReplaceAStringTheEditorShips()
    {
        WriteCatalog("en", (EditorStrings.MainWindow_Section_Maps, "Regions"));
        EditorStrings.Load(EditorLang);

        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Get(EditorStrings.MainWindow_Section_Maps), Is.EqualTo("Regions"));
    }

    [Test]
    public void RegisteringTheSameFolderTwiceChangesNothing()
    {
        WriteCatalog("en", ("Game_Section_Creatures", "Creatures"));
        EditorStrings.Load(EditorLang);

        EditorStrings.AddCatalog(_catalog);
        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Catalogs.Count(c => string.Equals(c, _catalog, StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
    }

    /// <summary>A game translated into fewer languages than the editor still runs: its own strings fall
    /// back to English rather than disappearing.</summary>
    [Test]
    public void AGameWithNoTranslationForThisLanguageFallsBackToItsEnglish()
    {
        WriteCatalog("en", ("Game_Section_Creatures", "Creatures"));
        EditorStrings.Load(EditorLang, "es");

        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Get("Game_Section_Creatures"), Is.EqualTo("Creatures"));
    }

    [Test]
    public void AGamesTranslationIsPreferredOverItsEnglish()
    {
        WriteCatalog("en", ("Game_Section_Creatures", "Creatures"));
        WriteCatalog("es", ("Game_Section_Creatures", "Criaturas"));
        EditorStrings.Load(EditorLang, "es");

        EditorStrings.AddCatalog(_catalog);

        Assert.That(EditorStrings.Get("Game_Section_Creatures"), Is.EqualTo("Criaturas"));
    }

    /// <summary>A game's catalog is not the editor's to validate. A broken one costs that game's
    /// strings, not the editor's ability to start.</summary>
    [Test]
    public void AMalformedCatalogDoesNotStopTheEditorLoading()
    {
        File.WriteAllText(Path.Combine(_catalog, "en.json"), "{ this is not json");
        EditorStrings.Load(EditorLang);

        Assert.That(() => EditorStrings.AddCatalog(_catalog), Throws.Nothing);
        Assert.That(EditorStrings.Get(EditorStrings.MainWindow_Section_Maps), Is.Not.Empty);
    }

    [Test]
    public void ACatalogFolderThatIsNotThereIsIgnored()
    {
        EditorStrings.Load(EditorLang);

        Assert.That(() => EditorStrings.AddCatalog(Path.Combine(_catalog, "nothing-here")), Throws.Nothing);
        Assert.That(EditorStrings.Get(EditorStrings.MainWindow_Section_Maps), Is.Not.Empty);
    }
}
