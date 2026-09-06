using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One map slot in the map editor's list. Unlike the other row view-models this one does not mirror
/// each field as an observable property — a map is far too large for that. It holds the whole
/// <see cref="MapRecord"/> and exposes explicit notify helpers the map editor calls after mutating it.
/// </summary>
public sealed partial class MapRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    /// <summary>1-based map number.</summary>
    public int Index { get; }
    /// <summary>Whether the full map has been fetched; false for a placeholder row awaiting lazy load.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The live record. Mutated in place by the map editor, so changes must be announced
    /// through <see cref="UpdateRecord"/>, <see cref="NotifyDisplayName"/>, or <see cref="MarkDirty"/>.</summary>
    public MapRecord Record { get; private set; }

    /// <summary>Whether the map holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    // "{Index}: {internal Name}", with the player-facing DisplayName appended in parens when authored.
    /// <summary>List caption: "index: name", plus the player-facing display name in parentheses when set.</summary>
    public string DisplayName
    {
        get
        {
            string baseName = string.IsNullOrEmpty(Record.Name)
                ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Record.Name;
            return string.IsNullOrWhiteSpace(Record.DisplayName)
                ? $"{Index}: {baseName}"
                : $"{Index}: {baseName} ({Record.DisplayName.Trim()})";
        }
    }

    public MapRowViewModel(int index, MapRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        Record = r;
    }

    /// <summary>Swap in an edited record and mark the row dirty.</summary>
    public void UpdateRecord(MapRecord r)
    {
        Record = r;
        IsDirty = true;
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Take a copied record and leave the row DIRTY and loaded — the copy path, where the new
    /// map exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(MapRecord r)
    {
        UpdateRecord(r);
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
    }

    // Loads full record data without marking dirty (used for lazy fetch).
    /// <summary>Fill in the full record from a lazy fetch, leaving the row clean.</summary>
    public void LoadRecord(MapRecord r)
    {
        Record = r;
        IsLoaded = true;
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    // Fires DisplayName without replacing the record — used when Name is edited in place.
    /// <summary>Re-raise the caption after an in-place name edit, and mark the row dirty.</summary>
    public void NotifyDisplayName()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark dirty after an in-place edit that changes no caption (a tile paint, an attribute).</summary>
    public void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark the row clean after a successful save or discard.</summary>
    public void ClearDirty()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Bump the map's revision counter. Call BEFORE the actual save, in both modes.</summary>
    // Online — the server is the authority and does its own `map.Revision++` in HandleEditorSaveMap,
    //          ignoring the packet's Revision field entirely. We bump locally first so the editor's
    //          Properties display ends up in sync with what the server will hold; the packet still
    //          carries the bumped value (now matches the server's new value) but it's dead data on
    //          the wire — purely cosmetic for logs.
    // Offline — there is no server, so the editor IS the authority. Bumping before the disk write
    //          ensures the persisted JSON carries the new revision, so a later server load picks
    //          it up and any client with the old map cached re-fetches on next observe.
    //
    // Fires Record so MapEditorViewModel.OnMapRowPropertyChanged → NotifyMapProperties picks it up.
    public void BumpRevision()
    {
        Record.Revision++;
        OnPropertyChanged(nameof(Record));
    }
}
