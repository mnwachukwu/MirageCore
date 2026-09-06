using Mirage.Shared;

namespace Mirage.Editor.Services;

/// <summary>The classes of art the manager handles. Each is a separate numbering: sheet 1 of the tiles
/// is unrelated to sheet 1 of the items.</summary>
public enum AssetCategoryKind
{
    Tiles,
    Sprites,
    Items,
}

/// <summary>
/// One folder of numbered sheets under the assets root.
///
/// <para>Sprites are three folders rather than one, because a footprint size is a different grid: the
/// same sheet number is the same character at 32, 64 and 96 px, but each size is its own file. Every
/// other class is a single folder.</para>
/// </summary>
/// <param name="Kind">Which class of art this folder holds.</param>
/// <param name="Parts">The folder's path under the assets root, one segment per element.</param>
/// <param name="CellSize">Pixels per grid cell in this folder.</param>
public sealed record AssetFolder(AssetCategoryKind Kind, IReadOnlyList<string> Parts, int CellSize)
{
    /// <summary>Every folder the manager can be pointed at, in the order it offers them.</summary>
    public static IReadOnlyList<AssetFolder> All { get; } =
    [
        new(AssetCategoryKind.Tiles, [Constants.TilesAssetSubfolder], Constants.PicX),
        .. SpriteSizes.Select(cell => new AssetFolder(
            AssetCategoryKind.Sprites,
            [Constants.SpritesAssetSubfolder, $"{cell}x{cell}"],
            cell)),
        new(AssetCategoryKind.Items, [Constants.ItemsAssetSubfolder], Constants.PicX),
    ];

    /// <summary>The sprite cell sizes, smallest first. The smallest is the baseline the others are
    /// checked against.</summary>
    public static IEnumerable<int> SpriteSizes =>
        Enumerable.Range(1, Constants.MaxNpcSize).Select(size => size * Constants.PicX);

    /// <summary>The folders holding one class of art.</summary>
    public static IReadOnlyList<AssetFolder> For(AssetCategoryKind kind) =>
        [.. All.Where(f => f.Kind == kind)];

    /// <summary>Absolute path under an assets root.</summary>
    public string Under(string assetsDir) => Path.Combine([assetsDir, .. Parts]);

    /// <summary>The path a file in this folder has relative to the assets root, in the '/' form the
    /// seeder's tombstone list is written in.</summary>
    public string RelativeTo(string fileName) => string.Join('/', [.. Parts, fileName]);

    /// <summary>How the size selector labels this folder ("32x32").</summary>
    public string SizeLabel => $"{CellSize}x{CellSize}";
}
