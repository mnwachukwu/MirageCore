using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One line of the diff.</summary>
public sealed class WorldChangeRowViewModel(WorldChange change)
{
    public WorldChange Change { get; } = change;
    public string Section => EditorStrings.Get(MainWindowViewModel.SectionLabelKey(Change.Section));
    public string Num => Change.Num.ToString();
    public string Name => Change.Name.Length > 0 ? Change.Name : EditorStrings.Get(EditorStrings.WorldTransfer_Unnamed);
}

/// <summary>
/// What uploading a world folder would do to the connected server, shown before anything is sent.
///
/// <para>Additions and changes are the ordinary case and go up together. Removals are their own decision:
/// a record blank in the folder and authored on the server is either one an author deleted or one the
/// folder never had, and nothing in the two folders tells those apart. So they are listed, counted, and
/// left switched off, and the reader is told which reading would cost them.</para>
/// </summary>
public sealed partial class WorldTransferDialogViewModel : ObservableObject
{
    private readonly WorldDiff _diff;

    public ObservableCollection<WorldChangeRowViewModel> Added { get; } = [];
    public ObservableCollection<WorldChangeRowViewModel> Changed { get; } = [];
    public ObservableCollection<WorldChangeRowViewModel> Removed { get; } = [];

    /// <summary>Off unless the reader turns it on. Everything else about this dialog follows from that.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyCount))]
    private bool _includeRemovals;

    public string FolderPath { get; }
    public string ServerName { get; }

    public string Summary => EditorStrings.Format(EditorStrings.WorldTransfer_Summary,
        ("Added", Added.Count), ("Changed", Changed.Count), ("Removed", Removed.Count));

    public string BackupAdvice => EditorStrings.Get(EditorStrings.WorldTransfer_BackupAdvice);

    public string RemovalsWarning =>
        EditorStrings.Format(EditorStrings.WorldTransfer_RemovalsWarning, ("Path", FolderPath));

    public string IncludeRemovalsLabel =>
        EditorStrings.Format(EditorStrings.WorldTransfer_IncludeRemovals, ("Count", Removed.Count));

    public bool HasRemovals => Removed.Count > 0;
    public bool HasAdded => Added.Count > 0;
    public bool HasChanged => Changed.Count > 0;

    public bool HasOverCeiling => _diff.OverCeiling > 0;
    public string OverCeilingNotice =>
        EditorStrings.Format(EditorStrings.WorldTransfer_OverCeiling, ("Count", _diff.OverCeiling));

    public string AddedHeader => $"{EditorStrings.Get(EditorStrings.WorldTransfer_KindAdded)} ({Added.Count})";
    public string ChangedHeader => $"{EditorStrings.Get(EditorStrings.WorldTransfer_KindChanged)} ({Changed.Count})";
    public string RemovedHeader => $"{EditorStrings.Get(EditorStrings.WorldTransfer_KindRemoved)} ({Removed.Count})";

    /// <summary>How many records the Upload button would actually send.</summary>
    public int ApplyCount => Added.Count + Changed.Count + (IncludeRemovals ? Removed.Count : 0);

    /// <summary>The changes the reader agreed to, in the order the diff found them.</summary>
    public IReadOnlyList<WorldChange> Approved =>
        [.. _diff.Changes.Where(c => c.Kind != WorldChangeKind.Removed || IncludeRemovals)];

    public event Action? Confirmed;
    public event Action? Canceled;

    public WorldTransferDialogViewModel(string folderPath, string serverName, WorldDiff diff)
    {
        FolderPath = folderPath;
        ServerName = serverName;
        _diff = diff;
        foreach (var c in diff.Of(WorldChangeKind.Added)) Added.Add(new WorldChangeRowViewModel(c));
        foreach (var c in diff.Of(WorldChangeKind.Changed)) Changed.Add(new WorldChangeRowViewModel(c));
        foreach (var c in diff.Of(WorldChangeKind.Removed)) Removed.Add(new WorldChangeRowViewModel(c));
    }

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke();

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
