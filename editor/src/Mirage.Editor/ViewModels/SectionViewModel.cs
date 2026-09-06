using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;

namespace Mirage.Editor.ViewModels;

public sealed partial class SectionViewModel : ObservableObject
{
    /// <summary>Stable internal id (e.g. "Maps") used for section lookup/switching — never localized.</summary>
    public string Name { get; }

    // Holds the KEY, not the resolved text: a section row outlives a language switch, so resolving
    // once in the constructor would freeze the nav list in whatever language was active at startup.
    private readonly string _labelKey;

    /// <summary>Localized label shown in the section nav; decoupled from <see cref="Name"/> so the id stays stable.</summary>
    public string DisplayName => EditorStrings.Get(_labelKey);

    [ObservableProperty] private bool _hasDirty;

    /// <summary>False while the rail is collapsed to icons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private bool _isLabelVisible = true;

    /// <summary>Null while the label is on screen — a tooltip that repeats a visible label is noise.
    /// Collapsed, the icon is the only thing naming the section, so the name has to be reachable.</summary>
    public string? TooltipText => IsLabelVisible ? null : DisplayName;

    public SectionViewModel(string name, string labelKey)
    {
        Name = name;
        _labelKey = labelKey;
    }

    /// <summary>Re-read <see cref="DisplayName"/> after a language change. Raised by
    /// <see cref="MainWindowViewModel"/>, which owns the section list.</summary>
    public void NotifyDisplayNameChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(TooltipText));
    }
}
