using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Mirage.Editor.Controls;

public enum AtlasMode { ItemStrip, SpriteStrip }

/// <summary>Renders a single frame from an item or sprite atlas bitmap, always at 32×32 on screen.
///
/// <para>The source cell may be larger than that: a size-2 or size-3 NPC is drawn on a 64 or 96 px grid,
/// and <see cref="CellSize"/> says which. The frame is scaled to the same 32×32 box either way, so a
/// picker's rows stay one height whatever size the creature is.</para></summary>
public sealed class AtlasFramePreview : Control
{
    private const int W = 32;
    private const int H = 32;

    // South-facing (Down = dir 1): idle = frame 3 (x=96), walk stride = frame 4 (x=128)
    private const int SouthIdle = 3;
    private const int SouthWalk = 4;

    public static readonly StyledProperty<Bitmap?> AtlasProperty =
        AvaloniaProperty.Register<AtlasFramePreview, Bitmap?>(nameof(Atlas));
    public static readonly StyledProperty<int> FrameIndexProperty =
        AvaloniaProperty.Register<AtlasFramePreview, int>(nameof(FrameIndex));
    public static readonly StyledProperty<AtlasMode> ModeProperty =
        AvaloniaProperty.Register<AtlasFramePreview, AtlasMode>(nameof(Mode));
    public static readonly StyledProperty<bool> AnimatedProperty =
        AvaloniaProperty.Register<AtlasFramePreview, bool>(nameof(Animated));
    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<AtlasFramePreview, bool>(nameof(IsHighlighted));
    public static readonly StyledProperty<int> CellSizeProperty =
        AvaloniaProperty.Register<AtlasFramePreview, int>(nameof(CellSize), defaultValue: W);

    public Bitmap? Atlas
    {
        get => GetValue(AtlasProperty);
        set => SetValue(AtlasProperty, value);
    }
    public int FrameIndex
    {
        get => GetValue(FrameIndexProperty);
        set => SetValue(FrameIndexProperty, value);
    }
    public AtlasMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }
    public bool Animated
    {
        get => GetValue(AnimatedProperty);
        set => SetValue(AnimatedProperty, value);
    }
    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }
    /// <summary>Source cell size in the atlas, in pixels (32/64/96). The frame still draws at 32×32.</summary>
    public int CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1);
    private static readonly Pen HighlightPen = new(Brushes.Cyan, 2);

    private int _animFrameOffset = SouthIdle;
    private DispatcherTimer? _timer;
    private bool _attached;

    static AtlasFramePreview()
    {
        AffectsRender<AtlasFramePreview>(AtlasProperty, FrameIndexProperty, ModeProperty,
            IsHighlightedProperty, CellSizeProperty);
        AnimatedProperty.Changed.AddClassHandler<AtlasFramePreview>((c, _) => c.UpdateTimer());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopTimer();
    }

    private void UpdateTimer()
    {
        if (Animated && _attached)
            StartTimer();
        else
            StopTimer();
    }

    private void StartTimer()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
        _animFrameOffset = SouthIdle;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _animFrameOffset = _animFrameOffset == SouthIdle ? SouthWalk : SouthIdle;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var dst = new Rect(0, 0, W, H);
        ctx.FillRectangle(EmptyBrush, dst);

        var bmp = Atlas;
        int idx = FrameIndex;
        if (idx >= 0 && bmp is not null)
        {
            int cell = CellSize > 0 ? CellSize : W;
            var src = Mode == AtlasMode.ItemStrip
                ? new Rect(0, idx * cell, cell, cell)
                : new Rect(_animFrameOffset * cell, idx * cell, cell, cell);
            ctx.DrawImage(bmp, src, dst);
        }

        ctx.DrawRectangle(null, IsHighlighted ? HighlightPen : BorderPen, dst);
    }

    protected override Size MeasureOverride(Size _) => new(W, H);
}
