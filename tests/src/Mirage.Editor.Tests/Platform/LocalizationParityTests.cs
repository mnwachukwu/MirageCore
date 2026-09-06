using Mirage.Editor.Localization;
using Mirage.Shared.Localization;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Guards <c>lang/*.json</c> against the ways it rots silently. The rest of the suite only ever
/// loads English, so a translation can be missing a key or carry a stale placeholder and nothing
/// notices — a missing key only throws (DEBUG) if a test happens to call <c>Get</c> on that exact
/// key while that language is loaded, which no test does. This is the check that would have caught
/// <c>MapEditor_AnimDialogTitle</c> existing in en.json but not in es/fr/pt.
///
/// <para>Unlike the client and server suites there is no charset assertion here: the editor is an
/// Avalonia app drawing with real system fonts, not the client's SpriteFont atlas, so it is free to
/// use box glyphs, arrows, em dashes, and the Spanish inverted marks.</para>
/// </summary>
[TestFixture]
public class LocalizationParityTests
{
    private static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    private static string[] LangFiles() =>
        Directory.GetFiles(LangDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private static string LangCode(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Every key the editor can ask for. The keys are declared as
    /// <c>public const string X = nameof(X)</c> across the EditorStrings partials, so reflecting
    /// over the literals is the same list the code can pass to <c>Get</c>.</summary>
    private static string[] DeclaredKeys() => typeof(EditorStrings)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToArray();

    // Tripwire: if content copying ever breaks, the lang dir would hold only en.json (or nothing)
    // and every assertion below would pass vacuously.
    [Test]
    public void LangDir_ContainsEnglishAndAtLeastOneTranslation()
    {
        Assert.That(Directory.Exists(LangDir), Is.True, $"No lang directory at {LangDir}");
        Assert.That(LangFiles().Select(LangCode), Does.Contain("en"));
        Assert.That(LangFiles(), Has.Length.GreaterThan(1),
            "Only en.json was found - translations are not reaching the test output.");
    }

    [Test]
    public void EveryDeclaredKey_IsPresentInEveryLanguage()
    {
        var declared = DeclaredKeys();
        Assert.That(declared, Is.Not.Empty, "Reflection found no string consts on EditorStrings.");

        var problems = new List<string>();
        foreach (string file in LangFiles())
        {
            var dict = StringLoader.Load(file);
            problems.AddRange(declared.Where(k => !dict.ContainsKey(k))
                                      .Select(k => $"{Path.GetFileName(file)}: missing key {k}"));
        }
        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }

    [Test]
    public void NoLanguageFile_HasKeysTheEditorNeverAsksFor()
    {
        var declared = DeclaredKeys().ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (string file in LangFiles())
        {
            problems.AddRange(StringLoader.Load(file).Keys
                                          .Where(k => !declared.Contains(k))
                                          .Select(k => $"{Path.GetFileName(file)}: orphan key {k}"));
        }

        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The exact check <c>EditorStrings.Load</c> runs for a non-English language
    /// (throwing in DEBUG), applied here to every translation at once.</summary>
    [Test]
    public void EveryTranslation_PassesStringLoaderValidate()
    {
        var english = StringLoader.Load(Path.Combine(LangDir, "en.json"));
        var problems = new List<string>();
        foreach (string file in LangFiles().Where(f => LangCode(f) != "en"))
            problems.AddRange(StringLoader.Validate(english, StringLoader.Load(file), LangCode(file)));
        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }
}
