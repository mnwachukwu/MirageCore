using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One interval the combo offers, with its own caption so "5 min" localizes.</summary>
public sealed class AutoSaveIntervalOption(int minutes)
{
    public int Minutes { get; } = minutes;
    public string Label => EditorStrings.Format(EditorStrings.AutoSave_IntervalMinutes, ("Minutes", Minutes));
}

/// <summary>One reach the combo offers.</summary>
public sealed class AutoSaveReachOption(AutoSaveReach reach, string labelKey)
{
    public AutoSaveReach Reach { get; } = reach;
    public string Label => EditorStrings.Get(labelKey);
}

/// <summary>One editor's row in the configuration window. Edits a COPY of the stored setting: the dialog
/// only writes back when it is confirmed, so backing out leaves the schedule exactly as it was.</summary>
public sealed partial class AutoSaveRowViewModel : ObservableObject
{
    public string Section { get; }
    public string DisplayName => EditorStrings.Get(MainWindowViewModel.SectionLabelKey(Section));

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private AutoSaveIntervalOption _interval;
    [ObservableProperty] private AutoSaveReachOption _reach;

    public IReadOnlyList<AutoSaveIntervalOption> Intervals { get; }
    public IReadOnlyList<AutoSaveReachOption> Reaches { get; }

    public AutoSaveRowViewModel(string section, AutoSaveSetting setting,
        IReadOnlyList<AutoSaveIntervalOption> intervals, IReadOnlyList<AutoSaveReachOption> reaches)
    {
        Section = section;
        Intervals = intervals;
        Reaches = reaches;
        _enabled = setting.Enabled;
        // A hand-edited interval outside the offered set still runs; the combo just shows the nearest.
        _interval = intervals.FirstOrDefault(i => i.Minutes == setting.IntervalMinutes) ?? intervals[0];
        _reach = reaches.First(r => r.Reach == setting.Reach);
    }

    public AutoSaveSetting ToSetting() => new()
    {
        Enabled = Enabled,
        IntervalMinutes = Interval.Minutes,
        Reach = Reach.Reach,
    };
}

/// <summary>
/// The Auto-Save configuration window: one row per editor, each with its own switch, interval and reach.
///
/// <para>Every control is disabled while the editor is connected to a server, with the reason stated
/// above them — auto-save is offline only, and a setting that silently would not run is worse than one
/// you cannot reach.</para>
/// </summary>
public sealed partial class AutoSaveDialogViewModel : ObservableObject
{
    public ObservableCollection<AutoSaveRowViewModel> Rows { get; } = [];

    /// <summary>False while connected — bound to the whole grid, so nothing here can be set to run when
    /// it would not.</summary>
    public bool IsConfigurable { get; }

    /// <summary>Shown whenever the controls are disabled, saying why.</summary>
    public string OfflineOnlyNotice => EditorStrings.Get(EditorStrings.AutoSave_OfflineOnlyNotice);

    public event Action? Confirmed;
    public event Action? Canceled;

    public AutoSaveDialogViewModel(bool isOnline)
    {
        IsConfigurable = !isOnline;

        var intervals = AutoSaveSetting.Intervals.Select(m => new AutoSaveIntervalOption(m)).ToList();
        var reaches = new List<AutoSaveReachOption>
        {
            new(AutoSaveReach.OpenRecord, EditorStrings.AutoSave_ReachOpenRecord),
            new(AutoSaveReach.AllDirty, EditorStrings.AutoSave_ReachAllDirty),
        };

        foreach (string section in MainWindowViewModel.AutoSaveSections)
            Rows.Add(new AutoSaveRowViewModel(section, MainWindowViewModel.SettingFor(section), intervals, reaches));
    }

    /// <summary>Write every row back into the settings file.</summary>
    [RelayCommand]
    private void Confirm()
    {
        foreach (var row in Rows)
            AppSettings.Current.AutoSave[row.Section] = row.ToSetting();
        AppSettings.Current.Save();
        Confirmed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
