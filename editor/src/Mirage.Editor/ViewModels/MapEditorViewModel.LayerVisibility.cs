using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Which art layers the map canvas draws, and what happens when a tile is painted onto one that is put away.
///
/// <para>Visibility is view state: it never reaches a <see cref="Mirage.Shared.Records.MapRecord"/>, a save
/// or a packet. It also does not reach the World Preview or the PNG export, both of which show what the
/// world looks like rather than what you are working on.</para>
/// </summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    /// <summary>Which layers are drawn. The default value is everything visible.</summary>
    [ObservableProperty] private LayerVisibility _layerVisibility;

    partial void OnLayerVisibilityChanged(LayerVisibility value)
    {
        OnPropertyChanged(nameof(HoveredGroundLayers));
        OnPropertyChanged(nameof(HoveredFringeLayers));
        OnPropertyChanged(nameof(HoveredCanopyLayers));
    }

    /// <summary>Raised when a layer is turned back on from the paint prompt, so an open Layer Visibility
    /// window re-ticks its boxes.</summary>
    public event Action? LayerVisibilityChangedExternally;

    // ── Painting onto a layer you cannot see ──────────────────────────────────

    // The tile the prompt is holding, and the stamp that was going to land on it. A stroke asks once: the
    // pointer is still down while the prompt is up, and a question per cell would be unusable.
    private (int X, int Y)? _hiddenLayerPaint;
    private bool _hiddenLayerAsked;

    [ObservableProperty] private bool _showHiddenLayerDialog;

    /// <summary>Names the layer being painted onto, so the prompt says which one is put away.</summary>
    public string HiddenLayerPrompt => EditorStrings.Format(
        EditorStrings.MapEditor_HiddenLayerPrompt, ("Layer", SelectedLayerLabel));

    /// <summary>True when the layer paint would land on is hidden.</summary>
    public bool IsSelectedLayerHidden =>
        !LayerVisibility.IsVisible(SelectedLayerType, SelectedLayerArrayIndex);

    // The gate itself. A tile that would land on a hidden layer is not placed — it would land correctly and
    // be invisible, which reads as the editor ignoring the click. The first such cell in a stroke raises the
    // prompt and the rest are dropped; answering it is what decides whether anything is painted.
    private bool BlockedByHiddenLayer(int x, int y)
    {
        if (!IsSelectedLayerHidden) return false;
        if (_hiddenLayerAsked) return true;

        _hiddenLayerAsked = true;
        _hiddenLayerPaint = (x, y);
        OnPropertyChanged(nameof(HiddenLayerPrompt));
        ShowHiddenLayerDialog = true;
        return true;
    }

    /// <summary>Turn the layer back on and lay the tile that was refused.</summary>
    [RelayCommand]
    private void ConfirmHiddenLayer()
    {
        ShowHiddenLayerDialog = false;
        var pending = _hiddenLayerPaint;
        _hiddenLayerPaint = null;

        SetLayerVisible(SelectedLayerType, SelectedLayerArrayIndex, visible: true);
        if (pending is not { } at) return;

        // Replayed as its own batch: the stroke that raised the prompt has long since ended, and the
        // author answered a question about one tile.
        BeginBatch();
        PaintTileAt(at.X, at.Y);
        CommitBatch();
    }

    [RelayCommand]
    private void CancelHiddenLayer()
    {
        ShowHiddenLayerDialog = false;
        _hiddenLayerPaint = null;
    }

    // ── The picker's operations ───────────────────────────────────────────────

    public void SetLayerVisible(LayerType type, int index, bool visible)
    {
        LayerVisibility = LayerVisibility.With(type, index, visible);
        LayerVisibilityChangedExternally?.Invoke();
    }

    public void SetStackVisible(LayerType type, bool visible) =>
        LayerVisibility = LayerVisibility.WithStack(type, visible);

    public void SetAllLayersVisible(bool visible) => LayerVisibility = LayerVisibility.ForAll(visible);

    /// <summary>Layer counts by stack, for a picker building one row per layer.</summary>
    public static int LayerCountOf(LayerType type) => MaxLayersOf(type);
}
