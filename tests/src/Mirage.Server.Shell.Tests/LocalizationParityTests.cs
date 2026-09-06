using Mirage.Server.Shell.Localization;
using Mirage.Shared.Localization;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// Guards <c>lang/shell/*.json</c> against silent rot. A key written into en.json alone goes unnoticed
/// until somebody runs the shell in Spanish; nothing else in the suite loads a translation.
///
/// <para>No SpriteFont check, unlike the server's copy — these are drawn by Avalonia with real system
/// fonts, not the client's atlas.</para>
/// </summary>
[TestFixture]
public class LocalizationParityTests
{
    /// <summary>lang/shell/, NOT lang/ — the server's own table lives there.</summary>
    private static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang", "shell");

    private static string[] LangFiles() =>
        Directory.GetFiles(LangDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private static string LangCode(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Every key the shell can ask for. Declared as <c>public const string X = nameof(X)</c>,
    /// so reflecting over the literals yields exactly the list the code can pass to <c>Get</c>.</summary>
    private static string[] DeclaredKeys() => typeof(ShellStrings)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToArray();

    // Tripwire: if content copying ever breaks, the lang dir would hold only en.json (or nothing) and
    // every assertion below would pass vacuously.
    [Test]
    public void LangDir_ContainsEnglishAndAtLeastOneTranslation()
    {
        Assert.That(Directory.Exists(LangDir), Is.True, $"No lang/shell directory at {LangDir}");
        Assert.That(LangFiles().Select(LangCode), Does.Contain("en"));
        Assert.That(LangFiles(), Has.Length.GreaterThan(1),
            "Only en.json was found - translations are not reaching the test output.");
    }

    [Test]
    public void EveryDeclaredKey_IsPresentInEveryLanguage()
    {
        var declared = DeclaredKeys();
        Assert.That(declared, Is.Not.Empty, "Reflection found no string consts on ShellStrings.");

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
    public void NoLanguageFile_HasKeysTheShellNeverAsksFor()
    {
        // The other direction: a key renamed in code and left behind in the JSON. Harmless at runtime,
        // which is exactly why it accumulates, and then nobody can tell which entries a translator still
        // needs to keep current.
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

    [Test]
    public void EveryTranslation_CarriesTheSamePlaceholdersAsEnglish()
    {
        // A {Path} dropped or misspelled in translation renders the brace literally to the operator.
        // Compared as SETS: order is a translator's business, presence is not.
        var english = StringLoader.Load(Path.Combine(LangDir, "en.json"));
        var problems = new List<string>();
        foreach (string file in LangFiles().Where(f => LangCode(f) != "en"))
        {
            var translation = StringLoader.Load(file);
            foreach (var (key, englishValue) in english)
            {
                if (!translation.TryGetValue(key, out var translated)) continue;   // covered above
                var expected = Placeholders(englishValue);
                var actual = Placeholders(translated);
                if (!expected.SetEquals(actual))
                {
                    problems.Add($"{Path.GetFileName(file)}: {key} has [{string.Join(", ", actual)}], " +
                                 $"English has [{string.Join(", ", expected)}]");
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }

    [Test]
    public void EveryLanguage_NamesItselfInItsOwnLanguage()
    {
        // LanguageName is what the picker lists, so it is the one value that must NOT be translated into
        // the reader's language — somebody hunting for Spanish is looking for "Espanol", not "Spanish".
        foreach (string file in LangFiles())
        {
            var dict = StringLoader.Load(file);
            Assert.That(dict.TryGetValue(ShellStrings.LanguageName, out var name) && name.Length > 0,
                Is.True, $"{Path.GetFileName(file)} does not name itself.");
        }
    }

    private static HashSet<string> Placeholders(string value)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '{') continue;
            int close = value.IndexOf('}', i + 1);
            if (close < 0) break;
            found.Add(value[(i + 1)..close]);
            i = close;
        }
        return found;
    }
}
