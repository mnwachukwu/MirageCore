using Avalonia;
using Avalonia.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor.Views;

/// <summary>
/// The World Preview: a modeless, always-on-top canvas of the maps around the open one.
///
/// <para>The editor's only non-modal window. Everything else here is <c>ShowDialog</c>, which owns the app
/// until it is dismissed; this one has to sit beside the map editor while that keeps taking input, so it is
/// shown rather than dialogued and it never returns a result.</para>
///
/// <para>It is not a child of the main window in the visual sense, so <c>MainWindow.SaveWindowState</c>
/// cannot reach it and it carries its own geometry.</para>
/// </summary>
public partial class WorldPreviewWindow : Window
{
    private WorldPreviewViewModel? _vm;

    public WorldPreviewWindow()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.WorldPreview_Title);

        _hint.Text = EditorStrings.Get(EditorStrings.WorldPreview_Hint);
        _emptyNote.Text = EditorStrings.Get(EditorStrings.WorldPreview_NoMaps);

        var settings = AppSettings.Current;
        if (settings.WorldPreviewWidth is { } w && w > 0) Width = w;
        if (settings.WorldPreviewHeight is { } h && h > 0) Height = h;

        _canvas.MapClicked += OnMapClicked;
        _canvas.WarpsClicked += mapNum => _ = _vm?.ShowWarpsForAsync(mapNum);
        _canvas.PanRequested += delta => _scroll.Offset -= delta;
        _canvas.ScrollRequested += delta => _scroll.Offset -= delta;
        _canvas.ZoomRequested += OnZoomRequested;

        // The canvas holds a surface per map, so it needs to know what is actually on screen to decide how
        // many of those are worth keeping.
        _scroll.PropertyChanged += (_, args) =>
        {
            if (args.Property == ScrollViewer.OffsetProperty || args.Property == ScrollViewer.ViewportProperty)
                PushViewport();
        };

        Opened += OnOpened;
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Position lands here rather than in the constructor: before the window is shown the platform can
        // still move it, and the restored point would be overwritten.
        var settings = AppSettings.Current;
        if (settings.WorldPreviewX is { } x && settings.WorldPreviewY is { } y)
            Position = new PixelPoint((int)x, (int)y);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as WorldPreviewViewModel;
        if (_vm is null) return;

        ApplyTilesets();
        _canvas.Zoom = _vm.Zoom;
        ApplyLayout();

        _vm.LayoutChanged += ApplyLayout;
        _vm.MapInvalidated += _canvas.Invalidate;
        _vm.TilesetsChanged += ApplyTilesets;
        // Modal against this window rather than the main one: the preview is what raised it, and it is the
        // window the reader is looking at.
        _vm.ShowWarpTargetsAsync = async targets =>
        {
            var dlg = new WarpTargetsDialog { DataContext = targets };
            // Picking a destination opens it behind the dialog, so the dialog gets out of the way.
            dlg.CloseWhen(h => targets.Chosen += h);
            await dlg.ShowDialog(this);
        };
        _ = _vm.RefreshAsync();
    }

    private void ApplyTilesets()
    {
        if (_vm is not null) _canvas.Tilesets = _vm.Tilesets;
    }

    private void ApplyLayout()
    {
        if (_vm is null) return;
        _canvas.OriginMap = _vm.OriginMap;
        _canvas.OriginGroup = _vm.OriginGroup;
        _canvas.Layout = _vm.Layout;
    }

    private void OnMapClicked(int mapNum) => _vm?.OpenMap(mapNum);

    private void PushViewport() =>
        _canvas.Viewport = new Rect(_scroll.Offset.X, _scroll.Offset.Y,
            _scroll.Viewport.Width, _scroll.Viewport.Height);

    // Keeps the canvas point under the cursor still across a zoom step, so scrolling in on a corner of a
    // large region does not throw away where you were looking.
    private void OnZoomRequested(double zoom, Point atCanvasPoint)
    {
        if (_vm is null) return;
        double before = _canvas.Zoom;
        if (before <= 0 || Math.Abs(zoom - before) < 0.0001) return;

        var offset = _scroll.Offset;
        double viewX = atCanvasPoint.X - offset.X;
        double viewY = atCanvasPoint.Y - offset.Y;
        double scale = zoom / before;

        _vm.Zoom = zoom;
        _canvas.Zoom = zoom;
        _canvas.InvalidateMeasure();
        _canvas.UpdateLayout();

        _scroll.Offset = new Vector(
            Math.Max(0, atCanvasPoint.X * scale - viewX),
            Math.Max(0, atCanvasPoint.Y * scale - viewY));
        PushViewport();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            settings.WorldPreviewX = Position.X;
            settings.WorldPreviewY = Position.Y;
            settings.WorldPreviewWidth = Width;
            settings.WorldPreviewHeight = Height;
        }
        settings.Save();

        if (_vm is not null)
        {
            _vm.LayoutChanged -= ApplyLayout;
            _vm.MapInvalidated -= _canvas.Invalidate;
            _vm.TilesetsChanged -= ApplyTilesets;
            _vm.Dispose();
            _vm = null;
        }
    }
}
