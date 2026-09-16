namespace Mirage.Shared.Records;

/// <summary>
/// A map's dimensions in tiles.
///
/// <para><see cref="Floor"/> is the smallest a map may be and <see cref="HardMax"/> the largest, both
/// real limits; <see cref="SoftCap"/> is only where the editor starts warning, and costs nothing but
/// memory and load time to exceed.</para>
/// </summary>
public readonly record struct MapSize(int Width, int Height)
{
    /// <summary>The largest a map can be on either axis, and a real limit rather than a preference.
    ///
    /// <para>A warp and a Plate name their destination tile as a 16-bit coordinate — on disk, on the wire
    /// and in the editor's own records. A map wider than this could hold tiles that no door could ever point
    /// at, so this is where the format stops rather than where the advice does.</para></summary>
    public const int HardMax = ushort.MaxValue;   // 65,535

    /// <summary>The smallest a map may be on either axis: the camera's own window.
    ///
    /// <para>🔴 <b>A map smaller than the view is a map the engine cannot place.</b> The camera scrolls
    /// within the map's bounds, and below this size those bounds CROSS — there is no offset that shows the
    /// map and nothing sensible to draw in the margin around it. The camera settles it by centering rather
    /// than falling over, but that is a guard against a world that should not exist, not a way to build
    /// one, so a size is pulled up to here before it ever reaches a client.</para></summary>
    public static MapSize Floor => new(Constants.ViewportTilesX, Constants.ViewportTilesY);

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

    /// <summary>Pulled onto the legal range. A hand-edited file can ask for neither a map smaller than
    /// the screen nor one whose far tiles no warp could address.</summary>
    public MapSize Clamped() => new(Math.Clamp(Width, Floor.Width, HardMax),
                                    Math.Clamp(Height, Floor.Height, HardMax));

    /// <summary>⚠ True when either axis is under <see cref="Floor"/>, which is worth SAYING rather
    /// than silently correcting: an author who asked for a small room should learn that maps have a floor,
    /// not find their number quietly changed.</summary>
    public bool IsUnderFloor => Width < Floor.Width || Height < Floor.Height;

    /// <summary>True when either axis is past <see cref="SoftCap"/> — worth saying, never refused.</summary>
    public bool IsPastSoftCap => Width > SoftCap || Height > SoftCap;

    public override string ToString() => $"{Width}x{Height}";
}
