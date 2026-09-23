using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Localization;

/// <summary>Shared string loading, named-placeholder formatting, and translation validation.</summary>
public static class StringLoader
{
    private static readonly Regex _ph = new(@"\{(\w+)(?::([^}]+))?\}", RegexOptions.Compiled);

    /// <summary>The ambient placeholders, each standing for a reserved item slot's name: the money,
    /// and whatever else the engine charges or pays in. Their names belong to the world's item data
    /// rather than to any line of prose, so a game binds them with <see cref="SetItemNameToken"/> —
    /// <c>ItemNameTokens.Bind</c> is the one place that says which slots a game reserves.
    ///
    /// <para>AMBIENT rather than arguments, and that is the point: each appears in dozens of strings,
    /// and passing one at every call site would be dozens of chances to forget it — which in DEBUG is
    /// a crash, not a missing word.</para>
    ///
    /// <para>The fallback covers a token bound against a world that has not loaded, and a test host
    /// that never loads one, so a noun's place never prints empty.</para></summary>
    private static readonly Dictionary<string, (Func<string?> Source, string Fallback)> _itemNames = new(StringComparer.Ordinal);

    /// <summary>Binds <c>{<paramref name="key"/>}</c> to a reserved item slot's name. Read live rather
    /// than captured, so renaming that item renames it in every line of every language at once.</summary>
    public static void SetItemNameToken(string key, string fallback, Func<string?> source)
        => _itemNames[key] = (source, fallback);

    /// <summary>Whether <c>{<paramref name="key"/>}</c> resolves on its own, with nothing supplied by the
    /// caller — so a check that every placeholder has a value knows which ones need no call site.</summary>
    public static bool IsAmbient(string key) => _itemNames.ContainsKey(key);

    /// <summary>The name bound to an ambient token, or null when nothing is bound to it.</summary>
    private static string? ItemName(string key)
    {
        if (!_itemNames.TryGetValue(key, out var e)) return null;
        string? n = e.Source();
        return string.IsNullOrWhiteSpace(n) ? e.Fallback : n;
    }

    /// <summary>Substitutes the ambient tokens in a template that takes no arguments of its own.
    /// Cheap on the common path: a template with no placeholder at all is returned untouched, which
    /// matters because <c>Get</c> is called from draw code every frame.</summary>
    public static string Resolve(string template)
    {
        if (_itemNames.Count == 0 || template.IndexOf('{') < 0) return template;
        foreach (var (key, e) in _itemNames)
        {
            string token = "{" + key + "}";
            if (!template.Contains(token, StringComparison.Ordinal)) continue;
            string? n = e.Source();
            template = template.Replace(token, string.IsNullOrWhiteSpace(n) ? e.Fallback : n, StringComparison.Ordinal);
        }
        return template;
    }

    public static Dictionary<string, string> Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? throw new InvalidOperationException($"Failed to parse string file: {path}");
    }

    /// <summary>
    /// Replaces named placeholders in <paramref name="template"/> with supplied values.
    /// Format specs supported: <c>{Gold:N0}</c> applies <c>N0</c> via <c>string.Format</c>.
    /// In DEBUG, throws if a placeholder has no matching arg supplied.
    /// </summary>
    public static string Format(string template, params (string Key, object? Value)[] args)
    {
        return _ph.Replace(template, m =>
        {
            string key = m.Groups[1].Value;
            string? fmt = m.Groups[2].Success ? m.Groups[2].Value : null;
            // Ambient, so it resolves whether or not the caller passed anything — but an explicit
            // argument still wins, which is what lets a test state the name it expects.
            if (!Array.Exists(args, a => a.Key == key) && ItemName(key) is { } ambient) return ambient;
            foreach (var (k, v) in args)
            {
                if (k == key)
                {
                    return fmt is not null
                        ? string.Format("{0:" + fmt + "}", v)
                        : v?.ToString() ?? "";
                }
            }
#if DEBUG
            throw new InvalidOperationException(
                $"[StringLoader] No value supplied for '{{{key}}}' in: \"{template}\"");
#else
            return m.Value;
#endif
        });
    }

    /// <summary>
    /// Returns the values for each <c>{Name}</c> placeholder in <paramref name="template"/>, in the
    /// order they appear. Lets a localized template be passed verbatim to a logger sink that uses the
    /// same <c>{Name}</c> syntax (e.g. Serilog), so the sink can colorize and capture properties
    /// instead of receiving a pre-baked string.
    /// </summary>
    public static object?[] ValuesInTemplateOrder(string template, params (string Key, object? Value)[] args)
    {
        var matches = _ph.Matches(template);
        if (matches.Count == 0) return Array.Empty<object?>();
        var result = new object?[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            string key = matches[i].Groups[1].Value;
            bool found = false;
            foreach (var (k, v) in args)
            {
                if (k == key)
                {
                    result[i] = v;
                    found = true;
                    break;
                }
            }
#if DEBUG
            if (!found)
            {
                throw new InvalidOperationException(
                    $"[StringLoader] No value supplied for '{{{key}}}' in: \"{template}\"");
            }
#else
            if (!found) result[i] = $"{{{key}}}";
#endif
        }
        return result;
    }

    /// <summary>
    /// Validates <paramref name="translation"/> against <paramref name="english"/>: checks for
    /// missing keys, unknown keys, and mismatched placeholder token sets.
    /// Returns a list of error strings (empty list = clean).
    /// </summary>
    public static List<string> Validate(
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> translation,
        string langCode)
    {
        var errors = new List<string>();
        foreach (var (key, translated) in translation)
        {
            if (!english.TryGetValue(key, out var enValue))
            {
                errors.Add($"[{langCode}] Unknown key: {key}");
                continue;
            }
            var missing = TokensIn(enValue).Except(TokensIn(translated));
            var extra = TokensIn(translated).Except(TokensIn(enValue));
            foreach (var t in missing) errors.Add($"[{langCode}] {key}: missing token {{{t}}}");
            foreach (var t in extra) errors.Add($"[{langCode}] {key}: unexpected token {{{t}}}");
        }
        foreach (var key in english.Keys.Except(translation.Keys))
            errors.Add($"[{langCode}] {key}: not translated");
        return errors;
    }

    private static HashSet<string> TokensIn(string s)
        => _ph.Matches(s).Select(m => m.Groups[1].Value).ToHashSet();
}
