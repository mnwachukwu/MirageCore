using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Mirage.Editor.Controls;

/// <summary>Renders a single 32×32 tile from the shared tileset bitmap.</summary>
public sealed class SingleTileControl : Control
{
    private const int TileW = 32;
    private const int TileH = 32;

    public static readonly StyledProperty<Bitmap?> TileBitmapProperty =
        AvaloniaProperty.Register<SingleTileControl, Bitmap?>(nameof(TileBitmap));

    public static readonly StyledProperty<int> TileIndexProperty =
        AvaloniaProperty.Register<SingleTileControl, int>(nameof(TileIndex));

    public Bitmap? TileBitmap
    {
        get => GetValue(TileBitmapProperty);
        set => SetValue(TileBitmapProperty, value);
    }
    public int TileIndex
    {
        get => GetValue(TileIndexProperty);
        set => SetValue(TileIndexProperty, value);
    }

    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1);

    static SingleTileControl()
    {
        AffectsRender<SingleTileControl>(TileBitmapProperty, TileIndexProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        var dst = new Rect(0, 0, TileW, TileH);
        ctx.FillRectangle(EmptyBrush, dst);

        var bmp = TileBitmap;
        int idx = TileIndex;
        if (idx > 0 && bmp is not null)
        {
            int cols = (int)(bmp.Size.Width / TileW);
            if (cols > 0)
            {
                int col = (idx - 1) % cols;
                int row = (idx - 1) / cols;
                ctx.DrawImage(bmp, new Rect(col * TileW, row * TileH, TileW, TileH), dst);
            }
        }

        ctx.DrawRectangle(null, BorderPen, dst);
    }

    protected override Size MeasureOverride(Size _) => new(TileW, TileH);
}
