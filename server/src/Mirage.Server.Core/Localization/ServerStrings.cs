using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>
/// Central repository of all server-side string keys (player-facing messages + operator console / logs)
/// and the runtime accessor. Keys use the nameof trick so the const value exactly matches the JSON key —
/// renaming a const breaks the lookup intentionally, prompting you to update the JSON file too.
/// Call <see cref="Load"/> once at startup before the game loop begins.
/// </summary>
public static partial class ServerStrings
{
    // ── Runtime accessor ──────────────────────────────────────────────────────

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> _byLocale = new();
    // The server operator's language — used by Get/Format/GetTemplate (operator-bound audiences)
    // and as the fallback inside Lookup when a player's locale is unknown.
    private static string _operatorLocale = "en";
    // Set at startup so per-player ForPlayer(index, …) can resolve a player's session language
    // without ServerStrings holding a direct reference to PlayerManager.
    private static Func<int, string>? _resolver;

    /// <summary>
    /// Loads every <c>*.json</c> in <paramref name="langDir"/> into a per-locale cache and remembers
    /// <paramref name="operatorLangCode"/> as the server-operator language (the fallback used by
    /// <see cref="Lookup"/> and the value backing <see cref="Get"/>). en.json is the validation
    /// baseline; non-English files are validated against it (DEBUG throws, Release merges).
    /// </summary>
    public static void Load(string langDir, string operatorLangCode = "en")
    {
        _operatorLocale = operatorLangCode;
        _byLocale.Clear();

        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        _byLocale["en"] = english;

        foreach (var path in Directory.EnumerateFiles(langDir, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path);
            if (code == "en") continue;

            var translation = StringLoader.Load(path);
            var errors = StringLoader.Validate(english, translation, code);
            if (errors.Count > 0)
            {
#if DEBUG
                throw new InvalidOperationException(
                    $"ServerStrings translation errors ({code}):\n" + string.Join("\n", errors));
#else
                foreach (var e in errors)
                    System.Diagnostics.Debug.WriteLine(e);
                var merged = new Dictionary<string, string>(english);
                foreach (var (k, v) in translation) merged[k] = v;
                _byLocale[code] = merged;
                continue;
#endif
            }
            _byLocale[code] = translation;
        }
    }

    /// <summary>True iff a <paramref name="locale"/>.json was discovered and loaded at startup.
    /// Use to gate accepting a client-supplied locale before storing it on a session.</summary>
    public static bool IsLoaded(string locale) => _byLocale.ContainsKey(locale);

    /// <summary>
    /// Wires the per-player locale lookup that <see cref="ForPlayer"/> calls. Typically called once
    /// at startup with <c>index => playerManager[index].Language</c>.
    /// </summary>
    public static void SetPlayerLocaleResolver(Func<int, string> resolver) => _resolver = resolver;

    /// <summary>Operator-language lookup — for console output, logs, and broadcasts without a
    /// target player. Player-facing sends should use <see cref="ForPlayer"/> instead so each
    /// recipient sees the string in their own session locale.</summary>
    public static string Get(string key) => Lookup(_operatorLocale, key);

    public static string Format(string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(Get(key), args);

    /// <summary>
    /// Returns the localized template (placeholders intact) plus the values in template-order.
    /// Pass both to a logger that uses <c>{Name}</c> templates (e.g. Serilog) so the sink can
    /// colorize substituted values and capture them as structured properties — see
    /// <see cref="LocalizedLog"/> for the typical call pattern. Operator-language only.
    /// </summary>
    public static (string Template, object?[] Values) GetTemplate(string key, params (string Key, object? Value)[] args)
    {
        var template = Get(key);
        return (template, StringLoader.ValuesInTemplateOrder(template, args));
    }

    /// <summary>Resolves <paramref name="index"/>'s session locale (via the resolver) and returns
    /// the localized string. Falls back to the operator language when the resolver is unset or the
    /// session's locale is unknown.</summary>
    public static string ForPlayer(int index, string key)
        => Lookup(_resolver?.Invoke(index) ?? _operatorLocale, key);

    public static string ForPlayer(int index, string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(ForPlayer(index, key), args);

    /// <summary>Stateless one-off lookup for audiences without a session-managed Language field
    /// (e.g. editor login responses, where the locale arrives on the request packet and the
    /// editor never establishes session-bound language state).</summary>
    public static string ForLocale(string locale, string key) => Lookup(locale, key);

    public static string ForLocale(string locale, string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(ForLocale(locale, key), args);

    private static string Lookup(string locale, string key)
    {
        if (_byLocale.TryGetValue(locale, out var d) && d.TryGetValue(key, out var v)) return v;
        // Fall back to the server operator's language — not hard-coded "en".
        if (_byLocale.TryGetValue(_operatorLocale, out var op) && op.TryGetValue(key, out var v2)) return v2;
#if DEBUG
        throw new InvalidOperationException($"[ServerStrings] Missing key: \"{key}\"");
#else
        return $"[{key}]";
#endif
    }
}
