namespace Mirage.Editor.ViewModels;

/// <summary>
/// When each editor is next due to auto-save. One of these for the whole app, holding a last-saved
/// stamp per section — which is what lets a single ticker serve every editor, including the ones whose
/// section is not currently showing.
/// </summary>
public sealed class AutoSaveSchedule
{
    private readonly Dictionary<string, DateTime> _lastSaved = [];

    /// <summary>Whether this section is due now, advancing its stamp when it is.
    ///
    /// <para>The stamp advances whether or not anything actually needed writing. An editor with nothing
    /// dirty otherwise re-asks on every tick for the rest of the session, and the first tick after the
    /// interval passes would fire the moment a single field changed rather than a full interval later.</para>
    ///
    /// <para>A section seen for the first time starts its clock instead of firing: an editor enabled
    /// mid-session, or one whose settings were just confirmed, waits a full interval rather than saving
    /// on the next tick.</para></summary>
    public bool IsDue(string section, AutoSaveSetting setting, DateTime now)
    {
        if (!setting.Enabled) return false;

        if (!_lastSaved.TryGetValue(section, out var last))
        {
            _lastSaved[section] = now;
            return false;
        }

        if (now - last < TimeSpan.FromMinutes(setting.IntervalMinutes)) return false;

        _lastSaved[section] = now;
        return true;
    }

    /// <summary>Forget every stamp, so each section waits a full interval from here.</summary>
    public void Reset() => _lastSaved.Clear();
}
