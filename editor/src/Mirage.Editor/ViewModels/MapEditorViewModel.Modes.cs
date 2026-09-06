using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>Editor mode and action selection: the bool facades the radio buttons bind to, the
/// attribute tool list, and the brush-size visibility that follows from both.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    public IEnumerable<AttributeTool> Attributes { get; } = Enum.GetValues<AttributeTool>();

    // Convenience bool properties for RadioButton bindings
    public bool IsTileMode
    {
        get => SelectedMode == EditorMode.Tile;
        set { if (value) SelectedMode = EditorMode.Tile; }
    }

    public bool IsAttributeMode
    {
        get => SelectedMode == EditorMode.Attribute;
        set { if (value) SelectedMode = EditorMode.Attribute; }
    }

    public bool IsLightMode
    {
        get => SelectedMode == EditorMode.Light;
        set { if (value) SelectedMode = EditorMode.Light; }
    }

    // The Ground/Fringe logical-layer selector is shown in BOTH Attribute and Light modes (each authors on a
    // chosen plane — attributes / lights); Tile mode uses the visual-stack (LayerType) selector instead.
    public bool IsLayerAuthoringMode => IsAttributeMode || IsLightMode;

    partial void OnSelectedModeChanged(EditorMode value)
    {
        OnPropertyChanged(nameof(IsTileMode));
        OnPropertyChanged(nameof(IsAttributeMode));
        OnPropertyChanged(nameof(IsLightMode));
        OnPropertyChanged(nameof(IsLayerAuthoringMode));
        OnPropertyChanged(nameof(BrushSizeVisible));
        OnPropertyChanged(nameof(StatusModeText));
    }

    public bool IsPlaceAction
    {
        get => SelectedAction == EditorAction.Place;
        set { if (value) SelectedAction = EditorAction.Place; }
    }

    public bool IsSelectAction
    {
        get => SelectedAction == EditorAction.Select;
        set { if (value) SelectedAction = EditorAction.Select; }
    }

    public bool IsDeleteAction
    {
        get => SelectedAction == EditorAction.Delete;
        set { if (value) SelectedAction = EditorAction.Delete; }
    }

    // The brush-size control shows in Attribute mode (attribute brush) AND whenever the Delete action is active
    // (its erase brush works in every mode).
    public bool BrushSizeVisible => IsAttributeMode || IsDeleteAction;

    partial void OnSelectedActionChanged(EditorAction value)
    {
        OnPropertyChanged(nameof(IsPlaceAction));
        OnPropertyChanged(nameof(IsSelectAction));
        OnPropertyChanged(nameof(IsDeleteAction));
        OnPropertyChanged(nameof(BrushSizeVisible));
        OnPropertyChanged(nameof(StatusActionText));
    }
}
