using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The game's own fields on one of the engine's records, as a form under the record's own screen.
///
/// <para><b>A record the engine owns has two halves and they are authored by different paths.</b> An
/// item's power and its type are properties Core acts on, and they travel on the item's own packet,
/// which normalizes what it is sent. A game's fields on that same item are keys the engine has never
/// heard of, so they travel on the generic record packet, which moves them without reading them. This
/// holds the second half.</para>
///
/// <para><b>One form at a time, on the open record.</b> Every other row on these screens carries its own
/// editing state, because the editor was compiled knowing what those rows are. These fields arrived at
/// run time, so what there is to bind is the bag itself — fetched when a record is opened and left
/// behind when the selection moves.</para>
/// </summary>
public sealed partial class GameFieldsViewModel : ObservableObject
{
    private readonly EditorDataService _data;
    private readonly EditorConnection _conn;
    private readonly string _familyId;
    private AttributeBag _saved = new();

    public GameFieldsViewModel(EditorDataService data, EditorConnection conn, string familyId)
    {
        _data = data;
        _conn = conn;
        _familyId = familyId;

        // Which fields a family has is learned from the server on connecting, or from the world folder
        // on opening one, so it can arrive after this screen exists.
        WorldFamilies.Changed += OnFamiliesChanged;

        // One of these exists per screen and lives as long as the editor does, so it carries its own
        // subscription rather than relying on the screen to remember. The forms UNDER it are per-record
        // and do not, which is why they expose NotifyLabelsChanged instead.
        EditorStrings.LanguageChanged += NotifyLabelsChanged;
    }

    private void OnFamiliesChanged()
    {
        OnPropertyChanged(nameof(HasFields));

        // A world whose game declares nothing here leaves nothing on screen; one that declares
        // something rebuilds the form over what is already open.
        if (!HasFields) Close();
        else if (Num >= 1) Show(Num);
        else RaiseState();
    }

    /// <summary>Which record is open, or 0 for none.</summary>
    public int Num { get; private set; }

    /// <summary>The bag being edited. The form writes straight into it.</summary>
    public AttributeBag Record { get; private set; } = new();

    /// <summary>The rows on screen, or null when nothing is open.</summary>
    [ObservableProperty] private SchemaFormViewModel? _form;

    /// <summary>Whether this world's loaded game added anything to this family. False leaves the whole
    /// section off the screen: a world with no game loaded has no fields here and would otherwise show
    /// an empty heading.</summary>
    public bool HasFields => Family is { Fields.Count: > 0 };

    /// <summary>Whether there is a form to show right now — there are fields, and a record is open.</summary>
    public bool IsShown => HasFields && Form is not null;

    /// <summary>The heading above the form, naming the game rather than the engine.</summary>
    public string Heading => EditorStrings.Get(EditorStrings.GameFields_Heading);

    /// <summary>Whether the open record's fields hold unsaved edits.</summary>
    public bool IsDirty => IsShown && !Record.Equals(_saved);

    /// <summary>Required fields still empty, for the caption under the form.</summary>
    public IReadOnlyList<string> MissingRequired => Form?.MissingRequired ?? [];

    /// <summary>Raised when a row is edited, so the screen's Save button lights up.</summary>
    public event Action? Changed;

    private RecordFamily? Family => WorldFamilies.Find(_familyId);

    /// <summary>Open a record's fields, or close the form when <paramref name="num"/> is not a record.
    /// Online this fetches; offline it reads the folder.</summary>
    public void Show(int num)
    {
        if (!HasFields || num < 1)
        {
            Close();
            return;
        }

        Num = num;
        Adopt(_data.IsOnline ? new AttributeBag() : _data.OfflineGameFields(_familyId, num));

        // Online the bag arrives in its own round trip. The form is built now regardless, so the
        // section does not appear a beat after the record it belongs to.
        if (_data.IsOnline) _ = FetchAsync(num);
    }

    /// <summary>Take the record's fields as they now stand on the server or on disk, discarding edits.</summary>
    public void Reload()
    {
        if (Num >= 1) Show(Num);
    }

    /// <summary>Write the open record's fields. A no-op when there is nothing open and when nothing
    /// changed, so it is safe to call after every save of the record above it.</summary>
    public async Task SaveAsync()
    {
        if (!IsDirty) return;

        int num = Num;
        var fields = Record.Clone();

        if (_data.IsOnline)
        {
            await _conn.SendSaveAsync(new EditorSaveRecordPacket
            {
                Family = _familyId,
                Num = num,
                Fields = fields,
            });
        }
        else
        {
            await _data.SaveOfflineGameFieldsAsync(_familyId, num, fields);
        }

        // Only if the selection has not moved on while the save was in flight — otherwise this would
        // mark a different record's fields clean.
        if (Num == num) MarkSaved();
    }

    private async Task FetchAsync(int num)
    {
        var pkt = await _conn.RequestRecordAsync(_familyId, num);

        // Dropped when the selection has moved on, or when the author has typed into the form, since
        // either makes this answer about something other than what is on screen.
        if (pkt is null || Num != num || IsDirty) return;

        Adopt(pkt.Fields);
    }

    private void Adopt(AttributeBag fields)
    {
        Record = fields.Clone();
        _saved = Record.Clone();

        var family = Family;
        if (family is null)
        {
            Close();
            return;
        }

        var form = new SchemaFormViewModel(family, Record, WorldFamilies.Schema, _data.EntriesFor);
        form.Changed += OnEdited;
        Form = form;

        RaiseState();
    }

    private void Close()
    {
        Num = 0;
        Record = new AttributeBag();
        _saved = new AttributeBag();
        Form = null;
        RaiseState();
    }

    private void MarkSaved()
    {
        _saved = Record.Clone();
        RaiseState();
    }

    private void OnEdited()
    {
        RaiseState();
        Changed?.Invoke();
    }

    /// <summary>Re-read every caption after a language change.</summary>
    public void NotifyLabelsChanged()
    {
        Form?.NotifyLabelsChanged();
        OnPropertyChanged(nameof(Heading));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(MissingRequired));
    }
}
