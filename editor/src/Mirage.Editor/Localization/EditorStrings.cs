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

    /// <summary>Extra catalog folders, searched after the editor's own.
    ///
    /// <para><b>This is how a game layer names the things it adds.</b> The editor's own catalog is a
    /// closed set — every key it holds has a constant, and every constant has a key, in all four
    /// languages, asserted both ways. A game cannot add a key to it without failing that assertion, and
    /// cannot declare a constant in it at all. So a game ships its own <c>lang</c> folder and registers
    /// it here, and its record families, sections and fields carry keys that resolve out of it.</para>
    ///
    /// <para>Later folders win, so a game may also replace a string the editor ships.</para></summary>
    private static readonly List<string> _extraCatalogs = new();

    /// <summary>Adds a catalog folder to search after the editor's own, and reloads so its strings take
    /// effect immediately. Registering the same folder twice does nothing.</summary>
    public static void AddCatalog(string langDir)
    {
        if (string.IsNullOrWhiteSpace(langDir)) return;
        if (_extraCatalogs.Contains(langDir, StringComparer.OrdinalIgnoreCase)) return;

        _extraCatalogs.Add(langDir);
        if (!string.IsNullOrEmpty(LangDir)) Load(LangDir, _langCode);
    }

    /// <summary>Folders currently searched for strings, the editor's own first.</summary>
    public static IReadOnlyList<string> Catalogs =>
        string.IsNullOrEmpty(LangDir) ? _extraCatalogs : [LangDir, .. _extraCatalogs];

    // Which language the last Load resolved, so adding a catalog can reload the same one.
    private static string _langCode = "en";

    /// <summary>Reads <paramref name="langCode"/> out of every registered extra catalog and lays the
    /// results over <paramref name="baseStrings"/>.
    ///
    /// <para>A catalog with no file for this language contributes nothing rather than failing: a game
    /// translated into fewer languages than the editor should still run, showing the editor's own
    /// language for its own strings and English for the game's.</para></summary>
    private static IReadOnlyDictionary<string, string> WithExtraCatalogs(
        IReadOnlyDictionary<string, string> baseStrings, string langCode)
    {
        if (_extraCatalogs.Count == 0) return baseStrings;

        var merged = new Dictionary<string, string>(baseStrings, StringComparer.Ordinal);
        foreach (string dir in _extraCatalogs)
        {
            foreach (string file in CatalogFiles(dir, langCode))
            {
                try
                {
                    foreach (var (key, value) in StringLoader.Load(file)) merged[key] = value;
                }
                catch
                {
                    // A game's catalog is not the editor's to validate. A malformed or unreadable file
                    // costs that catalog's strings, not the editor's ability to start.
                }
            }
        }

        return merged;
    }

    // The requested language, then English as the fallback within that same catalog.
    private static IEnumerable<string> CatalogFiles(string dir, string langCode)
    {
        string english = Path.Combine(dir, "en.json");
        if (langCode != "en" && File.Exists(english)) yield return english;

        string wanted = Path.Combine(dir, $"{langCode}.json");
        if (File.Exists(wanted)) yield return wanted;
    }

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
        _langCode = langCode;
        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        if (langCode == "en")
        {
            _current = WithExtraCatalogs(english, langCode);
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
            _current = WithExtraCatalogs(merged, langCode);
            LanguageChanged?.Invoke();
            return;
#endif
        }
        _current = WithExtraCatalogs(translation, langCode);
        LanguageChanged?.Invoke();
    }

    /// <summary>Returns the localized string for <paramref name="key"/>.
    /// In DEBUG, throws on missing key. In Release, returns a bracketed placeholder.</summary>
    /// <summary>Looks up <paramref name="key"/>, or returns <paramref name="fallback"/> when this build
    /// has never heard of it.
    ///
    /// <para>For text whose key comes from OUTSIDE the editor — a record family declared by a module the
    /// editor was not compiled against. <see cref="Get"/> throws on an unknown key on purpose, because a
    /// missing key for the editor's OWN text is a bug; a module's key being absent is ordinary, and
    /// crashing on connect would be the wrong answer to it.</para></summary>
    public static string GetOrFallback(string key, string fallback)
        => !string.IsNullOrEmpty(key) && _current.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>A caption a GAME supplied, shown as written when this editor has no translation for it.
    ///
    /// <para>🔴 A module cannot ship a translation — there is nowhere to put one — so a label it
    /// declares will never be in this table, and looking it up always misses. Falling back to the
    /// field's id turns "Common name" into "name" and "Field notes" into "notes", which is what the
    /// editor drew for the first module ever connected to it.</para>
    ///
    /// <para>So a miss shows the label itself, and only a blank label falls back to the id. A game with
    /// its players' language shipped alongside still gets the translation, because the lookup is tried
    /// first.</para></summary>
    public static string GameLabel(string labelKey, string id)
        => !string.IsNullOrEmpty(labelKey) ? GetOrFallback(labelKey, labelKey) : id;

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
