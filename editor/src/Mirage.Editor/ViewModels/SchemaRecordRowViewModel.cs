using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One record of a family this build was never compiled against: a slot number and a bag of keys.
///
/// <para><b>The row holds the bag; the form is a view onto it.</b> Every other row view model declares a
/// property per field, because the editor knows what those fields are. Here the fields arrived at runtime
/// on a <see cref="RecordFamily"/>, so what the row can offer is the record itself and a
/// <see cref="SchemaFormViewModel"/> built over it.</para>
///
/// <para><b>Dirty is a comparison, not a flag set by each setter.</b> The form writes straight into the
/// bag and there is no list of properties to hang notifications off, so the row keeps the bag it was last
/// saved with and compares — which is exactly what <see cref="AttributeBag"/>'s value equality is
/// for.</para>
/// </summary>
public sealed partial class SchemaRecordRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    private readonly RecordFamily _family;
    private readonly RecordSchema _schema;
    private readonly Func<string, IReadOnlyList<NamedEntry>> _references;
    private AttributeBag _saved;

    public SchemaRecordRowViewModel(int index, RecordFamily family, AttributeBag record, RecordSchema schema,
                                    Func<string, IReadOnlyList<NamedEntry>>? references = null,
                                    bool isLoaded = true)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(record);

        Index = index;
        IsLoaded = isLoaded;
        _family = family;
        _schema = schema ?? RecordSchema.Empty;
        _references = references ?? (_ => []);

        Record = record.Clone();
        _saved = Record.Clone();
        Form = BuildForm();
    }

    /// <summary>1-based slot number within its family.</summary>
    public int Index { get; }

    /// <summary>The family this record belongs to. Carried on the row because the push-changes prompt
    /// holds a flat list of dirty rows from every section at once and has no other way to tell which
    /// family one came from.</summary>
    public RecordFamily Family => _family;

    /// <summary>Whether the full record has been fetched. Always true offline; false for a placeholder
    /// row waiting on the server.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The record being edited. The form writes into this one.</summary>
    public AttributeBag Record { get; private set; }

    /// <summary>The rows on screen, built from the family's fields.</summary>
    [ObservableProperty] private SchemaFormViewModel _form;

    /// <summary>The record's name, from whichever field the family nominated. Empty for an unused slot,
    /// and for a family whose records have no name at all.</summary>
    public string Name => string.IsNullOrEmpty(_family.NameFieldKey)
        ? ""
        : Record.TryGet(_family.NameFieldKey, out var value) ? value.AsText() : "";

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    public bool IsDirty => !Record.Equals(_saved);

    /// <summary>Required fields still empty, for the caption under the form.</summary>
    public IReadOnlyList<string> MissingRequired => Form.MissingRequired;

    /// <summary>Replace what this row holds — a server response, or a re-read from disk on discard.</summary>
    public void LoadFromRecord(AttributeBag record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Record = record.Clone();
        _saved = Record.Clone();
        IsLoaded = true;
        Form = BuildForm();
        RaiseRowChanged();
    }

    /// <summary>Take a copy of another slot's record, renamed. Dirty on arrival, like every other copy.</summary>
    public void CopyFrom(SchemaRecordRowViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Record = source.Record.Clone();
        if (!string.IsNullOrEmpty(_family.NameFieldKey))
            Record.Set(_family.NameFieldKey, source.Name + RecordCopy.Suffix);

        IsLoaded = true;
        Form = BuildForm();
        RaiseRowChanged();
    }

    /// <summary>Mark the row clean by taking what it currently holds as the saved state.</summary>
    public void ClearDirty()
    {
        _saved = Record.Clone();
        RaiseRowChanged();
    }

    /// <summary>The bag to send or write. A copy, so an edit made while a save is in flight cannot reach
    /// the record that was sent.</summary>
    public AttributeBag ToRecord() => Record.Clone();

    /// <summary>The save packet. The single source of that mapping — the section's own save and the
    /// push-changes prompt both route through here, so neither can drift from the other.</summary>
    public EditorSaveRecordPacket BuildSavePacket() =>
        new() { Family = _family.Id, Num = Index, Fields = ToRecord() };

    /// <summary>Re-read every caption after a language change, for this row and the form under it.</summary>
    public void NotifyLabelsChanged()
    {
        Form.NotifyLabelsChanged();
        OnPropertyChanged(nameof(DisplayName));
    }

    private SchemaFormViewModel BuildForm()
    {
        var form = new SchemaFormViewModel(_family, Record, _schema, _references);
        form.Changed += RaiseRowChanged;
        return form;
    }

    private void RaiseRowChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(MissingRequired));
    }
}
