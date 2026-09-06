using Mirage.Server.Core.Localization;
using Mirage.Shared;
using Mirage.Shared.Localization;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Guards <c>lang/*.json</c> against the ways it rots silently. The rest of the suite only ever
/// loads English, so a translation can be missing a key, carry a stale placeholder, or contain a
/// character the client's SpriteFont atlas cannot draw, and nothing else notices:
/// <list type="bullet">
/// <item>a missing key only throws (DEBUG) if a test happens to call <c>Get</c> on that exact key
/// while that language is loaded — which no test does;</item>
/// <item>an unrenderable character never throws at all, it just draws as a missing glyph.</item>
/// </list>
/// Server strings are held to the same charset as client strings because they are displayed by the
/// client, in the client's chat window, with the client's font.
/// </summary>
[TestFixture]
public class LocalizationParityTests
{
    private static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    private static string[] LangFiles() =>
        Directory.GetFiles(LangDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private static string LangCode(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Every key the server can ask for. The keys are declared as
    /// <c>public const string X = nameof(X)</c> across the ServerStrings partials, so reflecting
    /// over the literals is the same list the code can pass to <c>Get</c>.</summary>
    private static string[] DeclaredKeys() => typeof(ServerStrings)
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
        Assert.That(declared, Is.Not.Empty, "Reflection found no string consts on ServerStrings.");

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
    public void NoLanguageFile_HasKeysTheServerNeverAsksFor()
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

    /// <summary>The exact check <c>ServerStrings.Load</c> runs at startup for a non-English
    /// language (throwing in DEBUG), applied here to every translation at once.</summary>
    [Test]
    public void EveryTranslation_PassesStringLoaderValidate()
    {
        var english = StringLoader.Load(Path.Combine(LangDir, "en.json"));
        var problems = new List<string>();
        foreach (string file in LangFiles().Where(f => LangCode(f) != "en"))
            problems.AddRange(StringLoader.Validate(english, StringLoader.Load(file), LangCode(file)));
        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }

    [Test]
    public void EveryValue_IsRenderableByTheSpriteFont()
    {
        var problems = new List<string>();
        foreach (string file in LangFiles())
        {
            foreach (var (key, value) in StringLoader.Load(file))
            {
                // Console_* is printed to stdout and never reaches a client, so the client's font atlas
                // is not its constraint — the grouped /help listing is deliberately multi-line.
                if (key.StartsWith("Console_", StringComparison.Ordinal)) continue;
                if (TextValidation.IsValidText(value)) continue;
                string bad = string.Join(" ", value.Where(c => !TextValidation.IsValidChar(c))
                                                   .Distinct()
                                                   .Select(c => $"'{c}' U+{(int)c:X4}"));
                problems.Add($"{Path.GetFileName(file)} [{key}]: {bad}");
            }
        }

        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }
}
