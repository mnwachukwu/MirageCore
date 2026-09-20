using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One descriptive tag a guild leader may apply to their guild.
///
/// <para><b>Core knows a guild carries tags; it does not know what they say.</b> "Hardcore",
/// "Casual", "Weekday evenings", "Sprint decks only" — what a guild advertises about itself is a
/// question about the game it is in, and a list compiled into the engine would be that question
/// answered for every world at once by whoever wrote the engine.</para>
///
/// <para>What Core owns is the part that is the same everywhere: a leader picks up to
/// <see cref="Constants.MaxGuildLabels"/> of them, they are shown beside the guild's name and in the
/// browser a guildless player reads, and one nobody declared is refused rather than stored.</para>
///
/// <para><b>Declare none and a guild has no tags</b>, which is a coherent world: the picker is not
/// offered, and a guild is known by its name.</para>
/// </summary>
public sealed record GuildLabel
{
    /// <summary>Stable identifier, saved on the guild and carried on the wire.
    ///
    /// <para>⚠ Persisted, so it cannot be renamed freely: a guild wearing a tag whose key changed is
    /// wearing one nothing declares, and it stops being shown.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>Localization key for the words a player reads on the button and in the browser. A key
    /// no language file carries falls back to itself, so an unlocalized tag still reads as something.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Where this sits among the others. Lower shows first; equal values keep declaration
    /// order, so a game that does not care may leave them all at zero.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
}

/// <summary>
/// The guild tags a world offers, in display order.
///
/// <para>Indexed once at load: the picker draws it, a leader's choice is checked against it, and the
/// browser resolves a stored key back to something to read. All three ask the same two questions — what
/// tags are there, and is this key one of them.</para>
/// </summary>
public sealed class GuildLabelSet
{
    /// <summary>A world that declared no tags. A guild there is known by its name.</summary>
    public static readonly GuildLabelSet None = new([]);

    private readonly Dictionary<string, GuildLabel> _byKey;

    public GuildLabelSet(IReadOnlyList<GuildLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        Labels = [.. labels.OrderBy(l => l.Ordinal)];
        _byKey = Labels.ToDictionary(l => l.Key, StringComparer.Ordinal);
    }

    /// <summary>Every declared tag, in the order they are shown.</summary>
    public IReadOnlyList<GuildLabel> Labels { get; }

    public int Count => Labels.Count;

    /// <summary>Whether this world declared that tag. A leader's pick is checked against it, so a
    /// client naming something it invented changes nothing.</summary>
    public bool Knows(string key) => !string.IsNullOrEmpty(key) && _byKey.ContainsKey(key);

    /// <summary>The declaration behind a key, or null where nothing declared it.</summary>
    public GuildLabel? Find(string key) =>
        !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var label) ? label : null;
}
