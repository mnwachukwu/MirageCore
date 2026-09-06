using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Localization;

/// <summary>Shared string loading, named-placeholder formatting, and translation validation.</summary>
public static class StringLoader
{
    private static readonly Regex _ph = new(@"\{(\w+)(?::([^}]+))?\}", RegexOptions.Compiled);

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
