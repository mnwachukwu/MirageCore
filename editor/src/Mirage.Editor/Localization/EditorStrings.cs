using Mirage.Shared;
using Mirage.Shared.Localization;

namespace Mirage.Editor.Localization;

/// <summary>
/// Central repository of all editor UI string keys and the runtime accessor.
/// Keys use the nameof trick so the const value exactly matches the JSON key — renaming a const
/// breaks the lookup intentionally, prompting you to update the JSON file too.
/// Call <see cref="Load"/> once at startup before any UI is shown.
/// </summary>
public static partial class EditorStrings
{
    // ── Runtime accessor ──────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    /// <summary>Increments on each <see cref="Load"/> call so consumers can detect language changes.</summary>
    public static int Generation { get; private set; }

    /// <summary>The directory from which language files were last loaded.</summary>
    public static string LangDir { get; private set; } = string.Empty;

    /// <summary>Fires after <see cref="Load"/> swaps the active dictionary. Views subscribe to
    /// re-run their ApplyStrings() so labels refresh without restarting the editor.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Scans <paramref name="langDir"/> for *.json files and reads the <c>LanguageName</c>
    /// key from each. Returns (locale, displayName) pairs for a language picker.</summary>
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
    /// Call once at startup before any UI is shown.
    /// </summary>
    public static void Load(string langDir, string langCode = "en")
    {
        Generation++;
        LangDir = langDir;
        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        if (langCode == "en")
        {
            _current = english;
            LanguageChanged?.Invoke();
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
            var merged = new Dictionary<string, string>(english);
            foreach (var (k, v) in translation) merged[k] = v;
            _current = merged;
            LanguageChanged?.Invoke();
            return;
#endif
        }
        _current = translation;
        LanguageChanged?.Invoke();
    }

    /// <summary>Returns the localized string for <paramref name="key"/>.
    /// In DEBUG, throws on missing key. In Release, returns a bracketed placeholder.</summary>
    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var v)) return v;
#if DEBUG
        throw new InvalidOperationException($"[EditorStrings] Missing key: \"{key}\"");
#else
        return $"[{key}]";
#endif
    }

    /// <summary>Looks up <paramref name="key"/> then substitutes named placeholders.</summary>
    public static string Format(string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(Get(key), args);

    /// <summary>A window caption: the app's name, then what the window is for. Every dialog title goes
    /// through here so a taskbar entry or an alt-tab thumbnail names the app that raised it.</summary>
    public static string WindowTitle(string text) => $"{Constants.GameName} — {text}";

    /// <summary>The same caption from a string key.</summary>
    public static string TitleFor(string key) => WindowTitle(Get(key));
}
