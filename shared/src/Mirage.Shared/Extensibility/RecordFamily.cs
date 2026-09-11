using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One numbered collection of authored records — what it is called, where it is kept, how many there
/// may be, and what one of them looks like to an author.
///
/// <para><b>Everything that treats a family as a family reads this.</b> The folder the records are kept
/// in, the filename each one takes, how far the world is padded, which section the editor shows in its
/// rail, what the world check counts, and what a world transfer carries. Those are one set of facts,
/// held once, so adding a family is a registration rather than an edit in seven places — and so a game
/// can add one Core has never heard of.</para>
///
/// <para><b>It describes; it does not construct.</b> There is no record type named here, because this
/// travels to a process that does not have that type compiled in. A family's records reach the editor
/// as data shaped by <see cref="Fields"/>, and the server side pairs this id with the concrete type
/// through its own registration.</para>
/// </summary>
public sealed record RecordFamily
{
    /// <summary>Stable identifier. The folder name under the world directory, the token on the wire,
    /// and the editor's section id. Lower-case and plural by convention — <c>items</c>,
    /// <c>npcs</c>.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for the editor's rail label, plural — "Items".</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Localization key for one of them, singular — "Item". Used wherever a single record is
    /// named: a form title, a picker's empty row, a validation message.</summary>
    [JsonPropertyName("singularLabelKey")] public string SingularLabelKey { get; init; } = string.Empty;

    /// <summary>What each file is called before its number — <c>item</c> gives <c>item1.json</c>.
    /// Defaults to <see cref="Id"/> with a trailing <c>s</c> removed when left blank.</summary>
    [JsonPropertyName("filePrefix")] public string FilePrefix { get; init; } = string.Empty;

    /// <summary>How many records this family has room for when an operator configures nothing.</summary>
    [JsonPropertyName("defaultLimit")] public int DefaultLimit { get; init; } = 1000;

    /// <summary>Fields shown for every record in the family, in display order.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = Array.Empty<FieldDescriptor>();

    /// <summary>Which of <see cref="Fields"/> selects the record's kind, or null for a family whose
    /// records all have the same shape. Naming one is what makes the form swap its lower rows as an
    /// author changes that field.</summary>
    [JsonPropertyName("kindFieldKey")] public string? KindFieldKey { get; init; }

    /// <summary>Whether the editor offers this family its own section. False for a family a game keeps
    /// but does not want authored by hand.</summary>
    [JsonPropertyName("authorable")] public bool Authorable { get; init; } = true;

    /// <summary>The file <paramref name="number"/> is stored in, without a directory.</summary>
    public string FileNameFor(int number) => $"{EffectiveFilePrefix}{number}.json";

    /// <summary><see cref="FilePrefix"/>, or <see cref="Id"/> singularized when it is blank.</summary>
    [JsonIgnore]
    public string EffectiveFilePrefix => string.IsNullOrWhiteSpace(FilePrefix)
        ? Id.EndsWith('s') ? Id[..^1] : Id
        : FilePrefix;

    /// <summary>The field with this key, across the family's own fields and every kind's, or null.</summary>
    public FieldDescriptor? FindField(string key, ChoiceSet? kinds = null)
    {
        var own = Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
        if (own is not null || kinds is null) return own;

        return kinds.Members
            .SelectMany(m => m.Fields)
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
    }
}

/// <summary>
/// Every record family a world has, and the choice sets their fields draw from.
///
/// <para>What a server sends an editor on connect, and the whole of what an editor needs to author a
/// world it was not compiled against.</para>
/// </summary>
public sealed record RecordSchema
{
    /// <summary>A world with no families — what Core describes before a game registers any.</summary>
    public static readonly RecordSchema Empty = new();

    [JsonPropertyName("families")]
    public IReadOnlyList<RecordFamily> Families { get; init; } = Array.Empty<RecordFamily>();

    [JsonPropertyName("choiceSets")]
    public IReadOnlyList<ChoiceSet> ChoiceSets { get; init; } = Array.Empty<ChoiceSet>();

    public RecordFamily? Family(string id)
        => Families.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    public ChoiceSet? Choices(string? id)
        => id is null ? null : ChoiceSets.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
