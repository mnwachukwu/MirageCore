using Mirage.Editor.Localization;
using System.Globalization;

namespace Mirage.Editor.ViewModels;

/// <summary>The status line an auto-save leaves behind. Kept apart from the scheduling so the wording
/// can be pinned by a test without standing up the whole shell.</summary>
public static class AutoSaveMessages
{
    // A log line rather than prose: fixed, invariant, and comparable between two of them at a glance.
    private static string Stamp(DateTime at) =>
        at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>"Auto-Saved (Rusty Sword) at 2026-08-21 09:14:02."</summary>
    public static string ForOneRecord(string recordName, DateTime at) =>
        EditorStrings.Format(EditorStrings.AutoSave_Saved,
            ("Subject", recordName), ("Timestamp", Stamp(at)));

    /// <summary>"Auto-Saved (7 records) at 2026-08-21 09:14:02."</summary>
    public static string ForManyRecords(int count, DateTime at) =>
        EditorStrings.Format(EditorStrings.AutoSave_Saved,
            ("Subject", EditorStrings.Format(EditorStrings.AutoSave_RecordCount, ("Count", count))),
            ("Timestamp", Stamp(at)));

    /// <summary>Names the record when exactly one was written and it has a name, and falls back to the
    /// count otherwise — "Auto-Saved ()" says nothing at all.</summary>
    public static string For(int saved, string openRecordName, DateTime at) =>
        saved == 1 && !string.IsNullOrWhiteSpace(openRecordName)
            ? ForOneRecord(openRecordName, at)
            : ForManyRecords(saved, at);
}
