using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Models;
using Mirage.Shared.Extensibility;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// A record's editing form, built from what the server said the family looks like.
///
/// <para><b>The schema exists for this.</b> One editor build authors any world: it learns a
/// family's fields from the server holding it rather than from what it was compiled against, so a game
/// that declares its records gets an authoring page without shipping a view.</para>
///
/// <para><b>The kind field swaps the rows below it.</b> A family naming a
/// <see cref="RecordFamily.KindFieldKey"/> shows that field's own rows plus the ones the chosen
/// <see cref="KindDescriptor"/> activates — so an author who picked one kind of thing is not looking at
/// boxes belonging to another. Changing the choice rebuilds the tail of the form and nothing else.</para>
///
/// <para><b>The form only ever writes keys; it never removes one.</b> A record holds what it holds —
/// including keys no row is showing, which is the ordinary state of one authored against a kind other
/// than the one currently chosen, or against a newer build of the game.</para>
/// </summary>
public sealed partial class SchemaFormViewModel : ObservableObject
{
    private readonly RecordFamily _family;
    private readonly RecordSchema _schema;
    private readonly Func<string, IReadOnlyList<NamedEntry>> _references;

    /// <param name="references">Given a family id, the records an author may point at. Empty for a
    /// family whose records this editor cannot list.</param>
    public SchemaFormViewModel(RecordFamily family, AttributeBag record, RecordSchema schema,
                               Func<string, IReadOnlyList<NamedEntry>>? references = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(record);

        _family = family;
        _schema = schema ?? RecordSchema.Empty;
        _references = references ?? (_ => []);
        Record = record;

        Rebuild();
    }

    /// <summary>The record being edited. Every row writes straight into it.</summary>
    public AttributeBag Record { get; }

    /// <summary>The rows on screen, in declared order: the family's own fields, then the ones the
    /// chosen kind activates.</summary>
    public ObservableCollection<SchemaFieldViewModel> Fields { get; } = [];

    /// <summary>Raised after any row is edited, so whatever owns the record can mark it dirty.</summary>
    public event Action? Changed;

    /// <summary>Required fields that are still empty, by key. Empty when the record is complete.</summary>
    public IReadOnlyList<string> MissingRequired =>
        [.. Fields.Where(f => f.IsMissing).Select(f => f.Key)];

    /// <summary>True when nothing required is missing.</summary>
    public bool IsComplete => MissingRequired.Count == 0;

    /// <summary>Re-read every row's caption after a language change. Called by whatever holds this form:
    /// one exists per record being edited, so a subscription here would outlive every form an author
    /// opened.</summary>
    public void NotifyLabelsChanged()
    {
        foreach (var row in Fields) row.NotifyLabelsChanged();
    }

    private void Rebuild()
    {
        foreach (var row in Fields) row.Edited -= OnFieldEdited;
        Fields.Clear();

        foreach (var descriptor in _family.Fields) Fields.Add(Row(descriptor));

        // The rows the chosen kind brings with it. A family that names no kind field has none, and a
        // choice naming a member this schema does not declare simply activates nothing — authored data
        // may name a kind a newer build of the game added.
        foreach (var descriptor in ActiveKindFields()) Fields.Add(Row(descriptor));

        OnPropertyChanged(nameof(MissingRequired));
        OnPropertyChanged(nameof(IsComplete));
    }

    private IReadOnlyList<FieldDescriptor> ActiveKindFields()
    {
        if (string.IsNullOrEmpty(_family.KindFieldKey)) return [];

        var kindField = _family.Fields.FirstOrDefault(f => f.Key == _family.KindFieldKey);
        if (kindField?.ChoiceSetId is null) return [];

        string chosen = Record.TryGet(_family.KindFieldKey, out var v) ? v.AsText() : "";
        return _schema.Choices(kindField.ChoiceSetId)?.Find(chosen)?.Fields ?? [];
    }

    private SchemaFieldViewModel Row(FieldDescriptor descriptor)
    {
        var row = new SchemaFieldViewModel(
            descriptor,
            Record,
            _schema.Choices(descriptor.ChoiceSetId),
            descriptor.RecordFamilyId is null ? [] : _references(descriptor.RecordFamilyId));

        row.Edited += OnFieldEdited;
        return row;
    }

    private void OnFieldEdited(SchemaFieldViewModel row)
    {
        // Only the kind field changes which rows exist. Rebuilding on every edit would replace the row
        // the author is typing in, which drops focus mid-word.
        if (row.Key == _family.KindFieldKey) Rebuild();

        OnPropertyChanged(nameof(MissingRequired));
        OnPropertyChanged(nameof(IsComplete));
        Changed?.Invoke();
    }
}
