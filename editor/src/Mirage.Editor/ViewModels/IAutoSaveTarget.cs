namespace Mirage.Editor.ViewModels;

/// <summary>
/// What the auto-save ticker needs from an editor. The map editor keeps its own dirty model and the
/// other eight share <see cref="EditorViewModelBase{TRow}"/>, so this is the one shape both answer to.
/// </summary>
public interface IAutoSaveTarget
{
    /// <summary>How many records hold unsaved edits.</summary>
    int DirtyCount { get; }

    /// <summary>The open record's name, for the status line. Empty when nothing is open.</summary>
    string OpenRecordName { get; }

    /// <summary>Write to disk and report how many records were saved. Zero means there was nothing to
    /// do, and the caller leaves the status line alone rather than announcing a save that never happened.
    /// <para>Always the OFFLINE path: auto-save never runs while connected, so this cannot reach a
    /// live server.</para></summary>
    Task<int> AutoSaveAsync(AutoSaveReach reach);

    /// <summary>Where the outcome is reported — the editor's own status line, so the message appears
    /// where the author is already looking.</summary>
    string StatusMessage { get; set; }
}
