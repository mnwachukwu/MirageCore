using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>What a field holds, and therefore what control edits it.</summary>
public enum FieldKind : byte
{
    /// <summary>A whole number, bounded by <see cref="FieldDescriptor.Min"/> and
    /// <see cref="FieldDescriptor.Max"/>. A spinner.</summary>
    Integer = 0,

    /// <summary>A number that may have a fraction. A text box that parses.</summary>
    Real = 1,

    /// <summary>Yes or no. A checkbox.</summary>
    Flag = 2,

    /// <summary>Free text, at most <see cref="FieldDescriptor.MaxLength"/> characters.</summary>
    Text = 3,

    /// <summary>One of a closed set named by <see cref="FieldDescriptor.ChoiceSetId"/>. A drop-down.</summary>
    Choice = 4,

    /// <summary>A slot number in the family named by <see cref="FieldDescriptor.RecordFamilyId"/>. A
    /// picker that lists that family's records by name, so an author chooses "Iron Key" rather than
    /// typing 214.</summary>
    RecordRef = 5,
}

/// <summary>
/// One editable value on a record: what it is called, what it holds, and what may be put in it.
///
/// <para><b>This is a description, not a control.</b> It travels from the server to the editor, which
/// builds the row for it. A game that declares its records in terms of these gets an authoring page
/// without writing a view — and one editor build authors any world, because it learns the shape of
/// that world's records from the server holding them rather than from what it was compiled
/// against.</para>
///
/// <para>Fields whose value lands in an <see cref="AttributeBag"/> name the bag key in
/// <see cref="Key"/>. Nothing here says where the value is stored; that is the family's business.</para>
/// </summary>
public sealed record FieldDescriptor
{
    /// <summary>What this field is called in data — the attribute-bag key or property name. Stable:
    /// authored files and saved records carry it.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>Localization key for the caption beside the control.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    [JsonPropertyName("kind")] public FieldKind Kind { get; init; }

    /// <summary>Localization key for a longer explanation, shown on hover. Null for a field whose
    /// caption says enough.</summary>
    [JsonPropertyName("hintKey")] public string? HintKey { get; init; }

    /// <summary>Inclusive bounds for <see cref="FieldKind.Integer"/> and <see cref="FieldKind.Real"/>.
    /// Equal values mean unbounded, which a field that declares neither gets.</summary>
    [JsonPropertyName("min")] public double Min { get; init; }

    /// <inheritdoc cref="Min"/>
    [JsonPropertyName("max")] public double Max { get; init; }

    /// <summary>Character limit for <see cref="FieldKind.Text"/>. Zero means unbounded.</summary>
    [JsonPropertyName("maxLength")] public int MaxLength { get; init; }

    /// <summary>For <see cref="FieldKind.Choice"/>: which set of <see cref="KindDescriptor"/> rows the
    /// drop-down lists.</summary>
    [JsonPropertyName("choiceSetId")] public string? ChoiceSetId { get; init; }

    /// <summary>For <see cref="FieldKind.RecordRef"/>: which <see cref="RecordFamily"/> the picker
    /// lists.</summary>
    [JsonPropertyName("recordFamilyId")] public string? RecordFamilyId { get; init; }

    /// <summary>True when the field must be filled before a record may be saved.</summary>
    [JsonPropertyName("required")] public bool Required { get; init; }

    /// <summary>True when both bounds are set, so a caller knows whether to clamp.</summary>
    [JsonIgnore] public bool IsBounded => Min != Max;

    /// <summary>Brings <paramref name="value"/> inside this field's bounds. Returns it unchanged for a
    /// field that is not bounded or not numeric.</summary>
    public double Clamp(double value)
        => IsBounded && Kind is FieldKind.Integer or FieldKind.Real ? Math.Clamp(value, Min, Max) : value;
}
