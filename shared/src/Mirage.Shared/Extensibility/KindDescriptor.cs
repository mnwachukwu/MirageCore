using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One member of a closed set an author picks from, and the fields that become meaningful once they
/// have picked it.
///
/// <para><b>The extra fields are the point.</b> A set whose members are only labels is a list of
/// strings. What makes this a descriptor is <see cref="Fields"/>: choosing a member changes which rows
/// the form shows, so an author picking one kind of thing is not looking at four boxes belonging to
/// another kind of thing. That relationship — member to the fields it activates — is the applicability
/// table, and holding it here is what keeps it from being restated in every form that reads the
/// set.</para>
///
/// <para>A member with no extra fields is perfectly ordinary and is just a labeled choice.</para>
/// </summary>
public sealed record KindDescriptor
{
    /// <summary>Stable identifier, written to data and carried on the wire. Never displayed.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for what an author sees in the drop-down.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Fields that apply only while this member is chosen, in the order they are shown.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = Array.Empty<FieldDescriptor>();
}

/// <summary>
/// A named closed set of <see cref="KindDescriptor"/> members that a
/// <see cref="FieldKind.Choice"/> field draws from.
///
/// <para>Named rather than inlined so several fields can offer the same set without three copies of it
/// drifting apart, and so the editor can list the members once.</para>
/// </summary>
public sealed record ChoiceSet
{
    /// <summary>What a <see cref="FieldDescriptor.ChoiceSetId"/> names.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("members")]
    public IReadOnlyList<KindDescriptor> Members { get; init; } = Array.Empty<KindDescriptor>();

    /// <summary>The member with this id, or null when the set has none.
    ///
    /// <para>Null is an ordinary answer: authored data may name a kind the loaded game does not
    /// declare. Callers show the raw id rather than failing.</para></summary>
    public KindDescriptor? Find(string id)
        => Members.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
}
