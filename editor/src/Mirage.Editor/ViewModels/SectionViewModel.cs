using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;

namespace Mirage.Editor.ViewModels;

public sealed partial class SectionViewModel : ObservableObject
{
    /// <summary>Stable internal id (e.g. "Maps") used for section lookup/switching — never localized.</summary>
    public string Name { get; }

    /// <summary>The glyph the family named, from Core's vocabulary. Blank draws the default.</summary>
    public string Icon { get; }

    // Holds the KEY, not the resolved text: a section row outlives a language switch, so resolving
    // once in the constructor would freeze the nav list in whatever language was active at startup.
    private readonly string _labelKey;
    private readonly string _fallbackLabel;

    /// <summary>Localized label shown in the section nav; decoupled from <see cref="Name"/> so the id stays stable.</summary>
    // A section a GAME added carries a caption this build has no translation for and never will, so
    // a miss shows the caption rather than the family's id — "Species", not "species".
    public string DisplayName => EditorStrings.GameLabel(_labelKey, _fallbackLabel);

    [ObservableProperty] private bool _hasDirty;

    /// <summary>False while the rail is collapsed to icons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private bool _isLabelVisible = true;

    /// <summary>Null while the label is on screen — a tooltip that repeats a visible label is noise.
    /// Collapsed, the icon is the only thing naming the section, so the name has to be reachable.</summary>
    public string? TooltipText => IsLabelVisible ? null : DisplayName;

    /// <param name="fallbackLabel">Shown when this build has no text for <paramref name="labelKey"/> — a
    /// family a module declared names a key the editor has never heard of. Defaults to the key.</param>
    /// <param name="icon">A name from <c>GameIcon.Offered</c>. One this build cannot draw falls back
    /// rather than failing, so a world from a newer engine still opens.</param>
    public SectionViewModel(string name, string labelKey, string? fallbackLabel = null, string icon = "")
    {
        Name = name;
        _labelKey = labelKey;
        _fallbackLabel = fallbackLabel ?? labelKey;
        Icon = icon;
    }

    /// <summary>Re-read <see cref="DisplayName"/> after a language change. Raised by
    /// <see cref="MainWindowViewModel"/>, which owns the section list.</summary>
    public void NotifyDisplayNameChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(TooltipText));
    }
}
