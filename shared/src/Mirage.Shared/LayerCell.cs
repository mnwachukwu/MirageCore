namespace Mirage.Shared;

/// <summary>How a stack of 2+ animated layers traverses its frames. Stored in the packed
/// <see cref="LayerCell"/> (bits 25..26); ignored for a single anim layer (which blinks).</summary>
public enum AnimStyle { Cycle = 0, Pendulum = 1 }

/// <summary>
/// Packs a single map-tile layer cell — the tile graphic, which tileset it came from, and whether
/// it animates — into one 32-bit <see cref="int"/>, for memory, the JSON map files and the SendMap
/// packet alike. A tile carries up to 10 layers, so an object per layer would cost the render hot path,
/// the files and the wire at once.
///
/// <para>Bit layout (least-significant bit first):</para>
/// <code>
///   bits  0..15  Tile   1-based tile index within the sheet.  0 = EMPTY layer (the project-wide
///                       "not defined" sentinel).  Range 0..65535.
///   bits 16..23  Sheet  0-based tileset index — which sheet the tile came from.
///                       Range 0..255 (see <see cref="Constants.MaxTilesets"/>).
///   bit     24   Anim   1 = this layer participates in the tile's animation; 0 = drawn every frame.
///                       Applies to ground and fringe alike. One anim layer in a stack blinks on/off;
///                       two or more are frames traversed per <see cref="AnimStyle"/>.
///   bits 25..26  Style  <see cref="AnimStyle"/> for the stack's traversal (0 = Cycle, 1 = Pendulum),
///                       read from the lowest anim layer. Meaningless unless the layer is animated.
///   bits 27..31  unused (reserved 0).
/// </code>
///
/// <para>0 means "empty, sheet 0, not animated", so a freshly zeroed <c>int[]</c> is already all-empty.
/// Every call site goes through the named helpers below; nothing touches the raw bits. Legacy
/// single-sheet maps are widened into this shape on load by <c>TileRecordConverter</c>.</para>
/// </summary>
public static class LayerCell
{
    // Field widths/positions — kept as named constants (no inline bit literals at call sites).
    private const int TileBits = 16;
    private const int TileMask = (1 << TileBits) - 1;      // 0x0000_FFFF
    private const int SheetShift = TileBits;               // 16
    private const int SheetBits = 8;
    private const int SheetMask = (1 << SheetBits) - 1;    // 0x0000_00FF (pre-shift)
    private const int AnimShift = SheetShift + SheetBits;  // 24
    private const int AnimBit = 1 << AnimShift;            // 0x0100_0000
    private const int StyleShift = AnimShift + 1;          // 25
    private const int StyleBits = 2;                       // bits 25..26 (4 styles possible, 2 used)
    private const int StyleMask = (1 << StyleBits) - 1;    // 0x3 (pre-shift)

    /// <summary>The empty layer value (no tile). Equal to <c>default(int)</c>.</summary>
    public const int Empty = 0;

    /// <summary>Largest tile index that fits in the Tile field (65535).</summary>
    public const int MaxTile = TileMask;

    /// <summary>Pack a tile index, sheet index, and animation flag; style defaults to Cycle.</summary>
    public static int Pack(int tile, int sheet, bool anim) => Pack(tile, sheet, anim, AnimStyle.Cycle);

    /// <summary>Pack a tile index, sheet index, animation flag, and animation style into one layer value.</summary>
    public static int Pack(int tile, int sheet, bool anim, AnimStyle style)
    {
        int packed = (tile & TileMask) | ((sheet & SheetMask) << SheetShift);
        if (anim) packed |= AnimBit;
        packed |= ((int)style & StyleMask) << StyleShift;
        return packed;
    }

    /// <summary>The 1-based tile index within the sheet; 0 means the layer is empty.</summary>
    public static int Tile(int packed) => packed & TileMask;

    /// <summary>The 0-based tileset index the tile came from.</summary>
    public static int Sheet(int packed) => (packed >> SheetShift) & SheetMask;

    /// <summary>True when this layer participates in the tile's animation (a frame).</summary>
    public static bool Anim(int packed) => (packed & AnimBit) != 0;

    /// <summary>The animation style stored on this layer. Callers use the value from a stack's lowest
    /// anim layer as that stack's traversal style; meaningless on non-anim or single-anim stacks.</summary>
    public static AnimStyle Style(int packed) => (AnimStyle)((packed >> StyleShift) & StyleMask);

    /// <summary>True when the layer holds no tile (Tile == 0).</summary>
    public static bool IsEmpty(int packed) => (packed & TileMask) == 0;

    /// <summary>Index of the highest non-empty layer in a stack, or -1 when every layer is empty.
    /// Used by the door-open reveal, which hides this single topmost layer.</summary>
    public static int TopmostNonEmptyIndex(ReadOnlySpan<int> layers)
    {
        for (int i = layers.Length - 1; i >= 0; i--)
            if (!IsEmpty(layers[i])) return i;
        return -1;
    }

    /// <summary>Array index of the anim-flagged layer to draw at animation frame <paramref name="frame"/>,
    /// or -1 to draw no anim layer this frame. 0 anim layers → -1; exactly 1 → on/off blink (shown on
    /// even frames); 2+ → traversed by the lowest anim layer's <see cref="AnimStyle"/> (Cycle = frame mod N,
    /// Pendulum = triangle wave). Non-anim layers are unaffected -- callers only consult this for
    /// Anim-flagged cells. Allocation-free.</summary>
    public static int VisibleAnimIndex(ReadOnlySpan<int> layers, int frame)
    {
        int n = 0, first = -1;
        for (int i = 0; i < layers.Length; i++)
        {
            if (!IsEmpty(layers[i]) && Anim(layers[i]))
            {
                if (first < 0) first = i;
                n++;
            }
        }

        if (n == 0) return -1;
        if (n == 1) return (frame & 1) != 0 ? -1 : first;   // single anim layer blinks on/off
        int pos;
        if (Style(layers[first]) == AnimStyle.Pendulum)
        {
            int period = 2 * (n - 1);
            int p = ((frame % period) + period) % period;
            pos = p < n ? p : period - p;                   // triangle wave: 0..N-1..1
        }
        else
        {
            pos = ((frame % n) + n) % n;                   // cycle: 0..N-1
        }

        int seen = 0;
        for (int i = 0; i < layers.Length; i++)
        {
            if (!IsEmpty(layers[i]) && Anim(layers[i]))
            {
                if (seen == pos) return i;
                seen++;
            }
        }

        return -1;
    }
}
