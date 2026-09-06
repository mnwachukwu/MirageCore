using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>
/// Central repository of all client-side UI string keys and the runtime accessor.
/// Keys use the nameof trick so the const value exactly matches the JSON key — renaming a const
/// breaks the lookup intentionally, prompting you to update the JSON file too.
/// Call <see cref="Load"/> once at startup before any drawing occurs.
/// </summary>
public static partial class ClientStrings
{
    // ── Runtime accessor ──────────────────────────────────────────────────────
    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    /// <summary>Incremented each time <see cref="Load"/> is called. Panels compare against
    /// a stored copy and re-fetch labels when the value differs, enabling hot language switching.</summary>
    public static int Generation { get; private set; }

    /// <summary>Scans <paramref name="langDir"/> for *.json files and reads the <c>LanguageName</c>
    /// key from each. Returns a list of (locale code, display name) pairs suitable for a language
    /// picker dropdown. No full load — only the single key is read per file.</summary>
    public static IReadOnlyList<(string Locale, string DisplayName)> GetAvailableLanguages(string langDir)
    {
        var result = new List<(string Locale, string DisplayName)>();
        if (!Directory.Exists(langDir)) return result;
        foreach (string file in Directory.GetFiles(langDir, "*.json"))
        {
            string locale = Path.GetFileNameWithoutExtension(file);
            try
            {
                var dict = StringLoader.Load(file);
                string displayName = dict.TryGetValue(LanguageName, out var n) ? n : locale;
                result.Add((locale, displayName));
            }
            catch { /* skip malformed files */ }
        }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return result;
    }

    /// <summary>
    /// Loads the string file for <paramref name="langCode"/> from <paramref name="langDir"/>.
    /// Non-English codes are validated against en.json; mismatches throw in DEBUG, log-and-merge in Release.
    /// Call once at startup before any UI is drawn, and again when the user switches language.
    /// </summary>
    public static void Load(string langDir, string langCode = "en")
    {
        Generation++;
        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        if (langCode == "en")
        {
            _current = english;
            return;
        }

        var translation = StringLoader.Load(Path.Combine(langDir, $"{langCode}.json"));
        var errors = StringLoader.Validate(english, translation, langCode);
        if (errors.Count > 0)
        {
#if DEBUG
            throw new InvalidOperationException(
                "Translation errors:\n" + string.Join("\n", errors));
#else
            foreach (var e in errors)
                System.Diagnostics.Debug.WriteLine(e);
            // Merge: English as base, overlay valid translations on top.
            var merged = new Dictionary<string, string>(english);
            foreach (var (k, v) in translation) merged[k] = v;
            _current = merged;
            return;
#endif
        }
        _current = translation;
    }

    /// <summary>Returns the localized string for <paramref name="key"/>.
    /// In DEBUG, throws on missing key so gaps are caught immediately during testing.
    /// In Release, returns a bracketed placeholder so gaps are visible in QA builds.</summary>
    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var v)) return v;
#if DEBUG
        throw new InvalidOperationException($"[ClientStrings] Missing key: \"{key}\"");
#else
        return $"[{key}]";
#endif
    }

    /// <summary>Looks up <paramref name="key"/> then substitutes named placeholders.
    /// E.g. <c>Format(InnPanel_CostLabel, ("Cost", 500))</c> → <c>"Cost: 500"</c>.</summary>
    public static string Format(string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(Get(key), args);
}
