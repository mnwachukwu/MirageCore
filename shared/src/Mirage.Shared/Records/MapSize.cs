namespace Mirage.Shared.Records;

/// <summary>
/// A map's dimensions in tiles.
///
/// <para>The floor is 1x1 — a map is at least one tile. <see cref="HardMax"/> is the largest a map can be
/// and is a format limit; <see cref="SoftCap"/> is only where the editor starts warning, and costs nothing
/// but memory and load time to exceed.</para>
/// </summary>
public readonly record struct MapSize(int Width, int Height)
{
    /// <summary>The largest a map can be on either axis, and a real limit rather than a preference.
    ///
    /// <para>A warp and a KeyOpen name their destination tile as a 16-bit coordinate — on disk, on the wire
    /// and in the editor's own records. A map wider than this could hold tiles that no door could ever point
    /// at, so this is where the format stops rather than where the advice does.</para></summary>
    public const int HardMax = ushort.MaxValue;   // 65,535

    /// <summary>Past this on either axis the editor warns. Both axes are judged separately: 129x100 and
    /// 127x200 each draw it, while 128x128 does not.
    ///
    /// <para>Advisory, not a limit — it marks where the two costs that grow with a map start to be felt,
    /// neither of which is rendering (that is bounded by the viewport and flat at every size). Crossing a
    /// map seam loads three maps, and an NPC that loses its path floods the whole nine-map area before it
    /// gives up. At 128x128 each is roughly 40 ms — a few frames, and under a tenth of an AI tick. At
    /// 256x256 both are about 180 ms, which is a visible stall and a third of the tick.</para></summary>
    public const int SoftCap = 128;

    /// <summary>A new world's default, and the fallback wherever a size is unstated: the camera's own
    /// window, so a map created without a thought fills the screen exactly and scrolls nowhere.</summary>
    public static MapSize Default => new(Constants.ViewportTilesX, Constants.ViewportTilesY);

    /// <summary>Pulled onto the legal range. A hand-edited file can ask for neither a zero-width map nor
    /// one whose far tiles no warp could address.</summary>
    public MapSize Clamped() => new(Math.Clamp(Width, 1, HardMax), Math.Clamp(Height, 1, HardMax));

    /// <summary>True when either axis is past <see cref="SoftCap"/> — worth saying, never refused.</summary>
    public bool IsPastSoftCap => Width > SoftCap || Height > SoftCap;

    public override string ToString() => $"{Width}x{Height}";
}
