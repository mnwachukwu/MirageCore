using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared.Extensibility;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One row of a form the editor built rather than compiled: a <see cref="FieldDescriptor"/> bound to the
/// value it edits.
///
/// <para><b>The descriptor decides which control shows.</b> The view binds visibility to the
/// <c>Is…</c> flags below rather than switching on <see cref="Kind"/> in code-behind, so adding a field
/// kind is a template rather than an edit in two places.</para>
///
/// <para><b>Every edit lands in the bag immediately.</b> There is no separate commit step, because the
/// form does not own the record — it is a view onto one, and the thing holding the bag decides when a
/// record is saved.</para>
/// </summary>
public sealed partial class SchemaFieldViewModel : ObservableObject
{
    private readonly AttributeBag _bag;
    private readonly ChoiceSet? _choices;

    public SchemaFieldViewModel(FieldDescriptor descriptor, AttributeBag bag, ChoiceSet? choices = null,
                                IReadOnlyList<NamedEntry>? references = null)
    {
        Descriptor = descriptor;
        _bag = bag;
        _choices = choices;
        References = references ?? [];
    }

    public FieldDescriptor Descriptor { get; }

    public string Key => Descriptor.Key;
    public FieldKind Kind => Descriptor.Kind;

    /// <summary>The caption. Falls back to the field's key: a module's label key is not in this build's
    /// language files, and a form that throws on connect is worse than one that shows a raw key.</summary>
    public string Label => EditorStrings.GameLabel(Descriptor.LabelKey, Descriptor.Key);

    /// <summary>The hover text, or null for a field whose caption says enough.</summary>
    public string? Hint => string.IsNullOrEmpty(Descriptor.HintKey)
        ? null
        : EditorStrings.GetOrFallback(Descriptor.HintKey, "");

    public bool IsInteger => Kind == FieldKind.Integer;
    public bool IsReal => Kind == FieldKind.Real;
    public bool IsFlag => Kind == FieldKind.Flag;
    public bool IsText => Kind == FieldKind.Text;
    public bool IsChoice => Kind == FieldKind.Choice;
    public bool IsRecordRef => Kind == FieldKind.RecordRef;

    /// <summary>True when a numeric field declares bounds, so the spinner can apply them.</summary>
    public bool IsBounded => Descriptor.IsBounded;
    public double Minimum => Descriptor.IsBounded ? Descriptor.Min : double.MinValue;
    public double Maximum => Descriptor.IsBounded ? Descriptor.Max : double.MaxValue;

    /// <summary>What a <see cref="FieldKind.Choice"/> field offers, in declared order.</summary>
    public IReadOnlyList<ChoiceOption> Options => _options ??=
        [.. (_choices?.Members ?? []).Select(m => new ChoiceOption(m.Id, EditorStrings.GameLabel(m.LabelKey, m.Id)))];
    private IReadOnlyList<ChoiceOption>? _options;

    /// <summary>What a <see cref="FieldKind.RecordRef"/> field offers: the named records of the family
    /// it points at, so an author picks a name rather than typing a slot number.</summary>
    public IReadOnlyList<NamedEntry> References { get; }

    // ── The value, in each shape a control binds to ───────────────────────────
    //
    // ONE of these belongs to any given row, and every setter refuses a write that is not its kind's. The
    // view stacks a control per kind and shows the one that applies, so all six are constructed and all six
    // bind — and a two-way control writes its value back as it initializes. Unguarded, opening a record
    // would stamp a blank onto every row whose control is hidden.

    public double NumberValue
    {
        get => _bag.TryGet(Key, out var v) ? v.AsDouble() : Descriptor.Clamp(0);
        set
        {
            if (Kind is not (FieldKind.Integer or FieldKind.Real)) return;
            Write(Kind == FieldKind.Integer
                ? AttributeValue.From((long)Descriptor.Clamp(Math.Round(value)))
                : AttributeValue.From(Descriptor.Clamp(value)));
        }
    }

    public bool FlagValue
    {
        get => _bag.TryGet(Key, out var v) && v.AsBool();
        set
        {
            if (Kind != FieldKind.Flag) return;
            Write(AttributeValue.From(value));
        }
    }

    public string TextValue
    {
        get => _bag.TryGet(Key, out var v) ? v.AsText() : "";
        set
        {
            if (Kind != FieldKind.Text) return;
            string text = value ?? "";
            // A declared limit is the record's rule, not the control's: a value pasted past it would
            // otherwise be refused by whatever validates on save, one screen away from the paste.
            if (Descriptor.MaxLength > 0 && text.Length > Descriptor.MaxLength)
                text = text[..Descriptor.MaxLength];
            Write(AttributeValue.From(text));
        }
    }

    /// <summary>The chosen member's id, or "" when nothing is chosen.</summary>
    public string ChoiceValue
    {
        get => _bag.TryGet(Key, out var v) ? v.AsText() : "";
        set
        {
            if (Kind != FieldKind.Choice) return;
            Write(AttributeValue.From(value ?? ""));
        }
    }

    /// <summary>The referenced record's slot number; 0 means none.</summary>
    public long ReferenceValue
    {
        get => _bag.TryGet(Key, out var v) ? v.AsLong() : 0;
        set
        {
            if (Kind != FieldKind.RecordRef) return;
            Write(AttributeValue.From(Math.Max(0, value)));
        }
    }

    /// <summary>True when this field is required and holds nothing. The form reports it; nothing here
    /// refuses the edit, because a half-filled record is an ordinary state to be in while typing.</summary>
    public bool IsMissing => Descriptor.Required && Kind switch
    {
        FieldKind.Text => string.IsNullOrWhiteSpace(TextValue),
        FieldKind.Choice => string.IsNullOrEmpty(ChoiceValue),
        FieldKind.RecordRef => ReferenceValue <= 0,
        _ => false,
    };

    /// <summary>Raised after any edit, so the form can re-check the kind field and the missing set.</summary>
    public event Action<SchemaFieldViewModel>? Edited;

    /// <summary>Re-read every localized part of this row. Called by <see cref="SchemaFormViewModel"/>,
    /// which is told about a language change; a row holds no subscription of its own because one exists
    /// per field of every record an author opens.</summary>
    public void NotifyLabelsChanged()
    {
        _options = null;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(Options));
    }

    /// <summary>What this row currently READS as, whether or not the key is there. Writing this back is
    /// not an edit, and saying so is what stops a control from creating a key as it initializes.</summary>
    private AttributeValue Displayed => Kind switch
    {
        FieldKind.Integer => AttributeValue.From((long)Descriptor.Clamp(Math.Round(NumberValue))),
        FieldKind.Real => AttributeValue.From(Descriptor.Clamp(NumberValue)),
        FieldKind.Flag => AttributeValue.From(FlagValue),
        FieldKind.RecordRef => AttributeValue.From(ReferenceValue),
        _ => AttributeValue.From(TextValue),
    };

    private void Write(AttributeValue value)
    {
        // A two-way control writes its value back as it is created, and an absent key reads as a zero or
        // a blank — so without this, a record an author merely LOOKED at gains a key per row and reads as
        // edited, with a dirty marker beside it and a Save button lit.
        if (Displayed.Equals(value)) return;
        if (_bag.TryGet(Key, out var existing) && existing.Equals(value)) return;

        _bag.Set(Key, value);
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(FlagValue));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(ChoiceValue));
        OnPropertyChanged(nameof(ReferenceValue));
        OnPropertyChanged(nameof(IsMissing));
        Edited?.Invoke(this);
    }
}

/// <summary>One row of a <see cref="FieldKind.Choice"/> drop-down: the id that is stored, and the text
/// an author reads.</summary>
public sealed record ChoiceOption(string Id, string Label);
