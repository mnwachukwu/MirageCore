namespace Mirage.Editor.ViewModels;

/// <summary>
/// A row that can show who is holding its record.
///
/// <para>Both are set from the server's table, never decided locally. The list grays out
/// <see cref="LockedByOther"/> and the editor refuses it — a lock of your own is your own unsaved
/// work and must never lock you out of it, so this is not simply "is locked".</para>
/// </summary>
public interface ILockableRow
{
    /// <summary>Somebody else has this record open with unsaved changes.</summary>
    bool LockedByOther { get; set; }

    /// <summary>The account holding it, shown on hover; empty when nobody is.</summary>
    string LockHolder { get; set; }
}
