namespace Mirage.Editor;

/// <summary>How much of an editor an auto-save tick writes.</summary>
public enum AutoSaveReach
{
    /// <summary>Only the record currently open, and only if it has unsaved edits.</summary>
    OpenRecord,
    /// <summary>Every record in that editor holding unsaved edits.</summary>
    AllDirty,
}

/// <summary>One editor's auto-save configuration. Off by default: an editor that starts writing to disk
/// on its own without being asked is a surprise, and the copy feature deliberately leaves new records
/// unsaved.</summary>
public sealed class AutoSaveSetting
{
    public bool Enabled { get; set; }

    /// <summary>Minutes between saves. Constrained to <see cref="Intervals"/> by the configuration
    /// window; a hand-edited value outside it still works and is simply honoured.</summary>
    public int IntervalMinutes { get; set; } = 5;

    public AutoSaveReach Reach { get; set; } = AutoSaveReach.AllDirty;

    /// <summary>The intervals the configuration window offers.</summary>
    public static readonly int[] Intervals = [5, 10, 15, 30, 60];

    public AutoSaveSetting Clone() => new()
    {
        Enabled = Enabled,
        IntervalMinutes = IntervalMinutes,
        Reach = Reach,
    };
}
