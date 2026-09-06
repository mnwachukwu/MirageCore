using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared;

namespace Mirage.Editor.ViewModels;

/// <summary>One numbered layer's box. Checked means visible, so a full column of ticks is the normal
/// state and anything unticked is something the author chose to put away.</summary>
public sealed partial class LayerVisibilityRowViewModel : ObservableObject
{
    private readonly Action<bool> _apply;
    private bool _suppress;

    public LayerVisibilityRowViewModel(int number, bool visible, Action<bool> apply)
    {
        Label = number.ToString();
        _isVisible = visible;
        _apply = apply;
    }

    public string Label { get; }

    [ObservableProperty] private bool _isVisible;

    partial void OnIsVisibleChanged(bool value)
    {
        if (_suppress) return;
        _apply(value);
    }

    /// <summary>Writes the box without running the click handler, for a state change that came from
    /// somewhere other than this box — the parent, Show all, or the paint prompt.</summary>
    public void SetWithoutApplying(bool visible)
    {
        _suppress = true;
        try { IsVisible = visible; }
        finally { _suppress = false; }
    }
}

/// <summary>One stack's box and its numbered children. The parent reads three-state: all shown, all
/// hidden, or a mix.</summary>
public sealed partial class LayerVisibilityStackViewModel : ObservableObject
{
    private readonly Action<bool> _apply;
    private bool _suppress;

    public LayerVisibilityStackViewModel(LayerType type, IReadOnlyList<LayerVisibilityRowViewModel> children,
        Action<bool> apply)
    {
        Type = type;
        // The stack word is vocabulary, not prose: Ground/Fringe/Canopy name the same stacks the map files
        // and the layer picker name, so they read identically in every language.
        Label = EditorVocabulary.NameOf(type);
        Children = children;
        _apply = apply;
    }

    public LayerType Type { get; }
    public string Label { get; }
    public IReadOnlyList<LayerVisibilityRowViewModel> Children { get; }

    /// <summary>Three-state: true all children shown, false all hidden, null a mix.</summary>
    [ObservableProperty] private bool? _isVisible = true;

    partial void OnIsVisibleChanged(bool? value)
    {
        if (_suppress || value is not { } set) return;
        _apply(set);
    }

    public void SetWithoutApplying(bool? state)
    {
        _suppress = true;
        try { IsVisible = state; }
        finally { _suppress = false; }
    }
}

/// <summary>
/// The Layer Visibility picker: which of the map canvas's layers are drawn.
///
/// <para>A tree, because the two useful moves are different sizes. Hiding a whole stack to see what is
/// under it is one click on the parent; isolating one numbered layer is a click on a child. The
/// Show all / Hide all button covers the third: "put everything away, then bring back the one I want"
/// is the fastest route to a single layer, and doing it fifteen clicks at a time is not.</para>
///
/// <para>Visibility is view state and never reaches the map. The window's open state is remembered
/// across restarts; the layers are NOT — a layer left hidden from a previous session is exactly the trap
/// this window otherwise prevents, and the author has no reason to suspect it.</para>
/// </summary>
public sealed partial class LayerVisibilityViewModel : ObservableObject, IDisposable
{
    private readonly MapEditorViewModel _maps;

    public LayerVisibilityViewModel(MapEditorViewModel maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps;

        Stacks =
        [
            .. Enum.GetValues<LayerType>().Select(type =>
            {
                var children = Enumerable
                    .Range(0, MapEditorViewModel.LayerCountOf(type))
                    .Select(i => new LayerVisibilityRowViewModel(
                        i + 1,
                        _maps.LayerVisibility.IsVisible(type, i),
                        visible => Apply(type, i, visible)))
                    .ToArray();
                return new LayerVisibilityStackViewModel(type, children, visible => ApplyStack(type, visible));
            }),
        ];

        _maps.LayerVisibilityChangedExternally += Sync;
        // The window is modeless and long-lived, so it outlives a language switch and has to re-read its
        // own captions rather than resolving them once at construction the way a dialog does.
        EditorStrings.LanguageChanged += OnLanguageChanged;
        Sync();
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(ToggleAllLabel));
        OnPropertyChanged(nameof(Status));
    }

    public IReadOnlyList<LayerVisibilityStackViewModel> Stacks { get; }

    /// <summary>Label for the one button: it offers whichever move is not already true.</summary>
    public string ToggleAllLabel => EditorStrings.Get(_maps.LayerVisibility.AllVisible
        ? EditorStrings.LayerVisibility_HideAll
        : EditorStrings.LayerVisibility_ShowAll);

    public string Status => EditorStrings.Get(_maps.LayerVisibility.AnyHidden
        ? EditorStrings.LayerVisibility_SomeHidden
        : EditorStrings.LayerVisibility_AllShown);

    [RelayCommand]
    private void ToggleAll()
    {
        _maps.SetAllLayersVisible(!_maps.LayerVisibility.AllVisible);
        Sync();
    }

    private void Apply(LayerType type, int index, bool visible)
    {
        _maps.SetLayerVisible(type, index, visible);
        Sync();
    }

    private void ApplyStack(LayerType type, bool visible)
    {
        _maps.SetStackVisible(type, visible);
        Sync();
    }

    // Every box is written from the one source of truth after any change, whoever made it — a child click,
    // a parent click, the button, or the paint prompt turning a layer back on from the map canvas.
    private void Sync()
    {
        var visibility = _maps.LayerVisibility;
        foreach (var stack in Stacks)
        {
            for (int i = 0; i < stack.Children.Count; i++)
                stack.Children[i].SetWithoutApplying(visibility.IsVisible(stack.Type, i));
            stack.SetWithoutApplying(visibility.StackState(stack.Type));
        }
        OnPropertyChanged(nameof(ToggleAllLabel));
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Closing the window puts every layer back: view state that outlives what you can see is
    /// how a hidden layer becomes a mystery.</summary>
    public void Dispose()
    {
        _maps.LayerVisibilityChangedExternally -= Sync;
        EditorStrings.LanguageChanged -= OnLanguageChanged;
        _maps.SetAllLayersVisible(true);
    }
}
