using Mirage.Shared;
using System.Text.Json;

namespace Mirage.Editor.Services;

/// <summary>The ways a file in a sheet folder can fail to be a sheet, each of which the loaders pass over
/// without a word.</summary>
public enum SheetProblemKind
{
    /// <summary>Two or more files claim the same index. The loader keeps whichever the filesystem happened
    /// to hand it last.</summary>
    DuplicateIndex,

    /// <summary>An image with no leading digits. Not a sheet at all, and silently skipped.</summary>
    NoIndexPrefix,

    /// <summary>An index at or past the ceiling. Dropped.</summary>
    IndexOutOfRange,

    /// <summary>Width or height is not a whole number of tiles, so the remainder holds cells nothing can
    /// address.</summary>
    NotTileAligned,

    /// <summary>A PNG with no transparency of any kind. Nothing keys a PNG, so it renders as a solid
    /// block.</summary>
    PngWithoutTransparency,

    /// <summary>A sprite sheet number present at one footprint size and absent at another. NPCs of the
    /// missing size draw nothing.</summary>
    MissingSizeVariant,

    /// <summary>A sprite sheet whose size variants hold different numbers of sprites, so one number is
    /// two different characters depending on the footprint.</summary>
    SizeVariantRowMismatch,
}

/// <summary>Where a sheet's transparency comes from.</summary>
public enum SheetTransparency
{
    /// <summary>The top-left pixel names the transparent color.</summary>
    ColorKey,

    /// <summary>The file carries its own alpha.</summary>
    Alpha,

    /// <summary>A PNG carrying neither, which draws as a solid rectangle.</summary>
    None,
}

/// <summary>One sheet file on disk.</summary>
/// <param name="Index">The number in its filename, which is the number maps store.</param>
/// <param name="Path">Absolute path.</param>
/// <param name="DisplayName">The filename's label half.</param>
/// <param name="Bytes">Size on disk.</param>
/// <param name="PixelWidth">0 when the header could not be read.</param>
/// <param name="PixelHeight">0 when the header could not be read.</param>
/// <param name="IsBundled">True when a file of this name ships with the editor, so deleting it has to
/// outlive the seeder.</param>
/// <param name="Transparency">Which model this file's transparency comes from.</param>
/// <param name="CellSize">Pixels per grid cell in this folder. Tiles and items are always 32; a sprite
/// sheet is 32, 64 or 96 according to the footprint size its folder holds.</param>
public sealed record SheetEntry(
    int Index, string Path, string DisplayName, long Bytes,
    int PixelWidth, int PixelHeight, bool IsBundled, SheetTransparency Transparency,
    int CellSize = Constants.PicX)
{
    /// <summary>Whole cells across and down; 0 when the size is unknown.</summary>
    public (int Cols, int Rows) TileGrid =>
        CellSize <= 0 ? (0, 0) : (PixelWidth / CellSize, PixelHeight / CellSize);

    /// <summary>Whether both dimensions are a whole number of cells.</summary>
    public bool IsTileAligned =>
        CellSize > 0 && PixelWidth > 0 && PixelHeight > 0 &&
        PixelWidth % CellSize == 0 && PixelHeight % CellSize == 0;
}

/// <summary>Something wrong with a file in the folder, and the paths involved.</summary>
public sealed record SheetProblem(SheetProblemKind Kind, int Index, IReadOnlyList<string> Paths);

/// <summary>What one sheet folder holds.</summary>
public sealed record SheetScan(IReadOnlyList<SheetEntry> Sheets, IReadOnlyList<SheetProblem> Problems)
{
    public static readonly SheetScan Empty = new([], []);
}

/// <summary>
/// Reads and edits a folder of numbered graphics sheets.
///
/// <para>Every operation is a file operation, and the whole type is deliberately free of Avalonia and of
/// view-models: what makes a sheet folder correct is a question about filenames and image headers, and it
/// should be answerable without a UI toolkit loaded.</para>
///
/// <para>It reports the failures the loaders swallow. A file with no index, two files claiming one index,
/// an index past the ceiling — each of those means a sheet is simply absent, with no error anywhere, and
/// finding out means noticing that tiles you painted are blank.</para>
/// </summary>
public static class SheetLibrary
{
    /// <summary>Folder deleted sheets are moved to, beside the sheet folders rather than inside one. The
    /// leading underscore sorts it above them, so the folders holding live art stay together.</summary>
    public const string RecycleFolder = "_recycle_bin";

    private const string TombstoneFile = "deleted.json";

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>Everything in a sheet folder, sorted by index, with whatever is wrong with it.</summary>
    /// <param name="dir">The folder to read; a missing one is empty rather than an error.</param>
    /// <param name="maxIndex">One past the highest usable index.</param>
    /// <param name="bundledDir">The bundled folder mirroring <paramref name="dir"/> (not the bundled
    /// root), for <see cref="SheetEntry.IsBundled"/>.</param>
    /// <param name="cellSize">Pixels per grid cell in this folder (32 for tiles and items; 32/64/96 for
    /// the sprite size folders).</param>
    public static SheetScan Scan(string dir, int maxIndex, string? bundledDir = null,
        int cellSize = Constants.PicX)
    {
        if (!Directory.Exists(dir)) return SheetScan.Empty;

        var byIndex = new Dictionary<int, List<string>>();
        var problems = new List<SheetProblem>();

        foreach (string path in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!SheetFile.IsSupported(path)) continue;
            int index = SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(path));

            if (index < 0)
            {
                problems.Add(new SheetProblem(SheetProblemKind.NoIndexPrefix, -1, [path]));
                continue;
            }
            if (index >= maxIndex)
            {
                problems.Add(new SheetProblem(SheetProblemKind.IndexOutOfRange, index, [path]));
                continue;
            }
            if (!byIndex.TryGetValue(index, out var list)) byIndex[index] = list = [];
            list.Add(path);
        }

        var sheets = new List<SheetEntry>();
        foreach (var (index, paths) in byIndex.OrderBy(kv => kv.Key))
        {
            // Every claimant is named. Which one the loader picks is decided by directory enumeration
            // order, so singling one out as "the winner" would be reporting an accident as a rule.
            if (paths.Count > 1)
                problems.Add(new SheetProblem(SheetProblemKind.DuplicateIndex, index, paths));

            var entry = Describe(index, paths[0], dir, bundledDir, cellSize);
            sheets.Add(entry);
            if (!entry.IsTileAligned && entry.PixelWidth > 0)
                problems.Add(new SheetProblem(SheetProblemKind.NotTileAligned, index, [entry.Path]));
            if (entry.Transparency == SheetTransparency.None)
                problems.Add(new SheetProblem(SheetProblemKind.PngWithoutTransparency, index, [entry.Path]));
        }

        return new SheetScan(sheets, problems);
    }

    private static SheetEntry Describe(int index, string path, string dir, string? bundledDir, int cellSize)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        var (w, h) = ImageHeader.TryReadSize(path);
        long bytes = 0;
        try { bytes = new FileInfo(path).Length; } catch { /* an unreadable size is 0, not a failure */ }

        bool bundled = bundledDir is not null
            && File.Exists(Path.Combine(bundledDir, Path.GetFileName(path)));

        var transparency = SheetFile.UsesColorKey(path)
            ? SheetTransparency.ColorKey
            : ImageHeader.PngHasTransparency(path) ? SheetTransparency.Alpha : SheetTransparency.None;

        return new SheetEntry(index, path, SheetFile.DisplayName(stem), bytes, w, h, bundled, transparency, cellSize);
    }

    /// <summary>
    /// What the sprite size folders disagree about.
    ///
    /// <para>A sprite sheet number is one character at every footprint size, so <c>1_*</c> in 32x32,
    /// 64x64 and 96x96 have to be the same roster in the same order. Nothing enforces that at load: a
    /// size that is missing simply draws nothing, and a size holding a different number of rows quietly
    /// makes one sprite number two different creatures. Both are reported here.</para>
    /// </summary>
    /// <param name="scans">One scan per size folder, keyed by cell size. The smallest is the baseline.</param>
    public static IReadOnlyList<SheetProblem> ScanSizeVariants(IReadOnlyDictionary<int, SheetScan> scans)
    {
        ArgumentNullException.ThrowIfNull(scans);
        var problems = new List<SheetProblem>();
        if (scans.Count < 2) return problems;

        int baseCell = scans.Keys.Min();
        var baseline = scans[baseCell].Sheets.ToDictionary(s => s.Index);

        foreach (int cell in scans.Keys.Where(c => c != baseCell).Order())
        {
            var other = scans[cell].Sheets.ToDictionary(s => s.Index);
            foreach (var (index, baseSheet) in baseline.OrderBy(kv => kv.Key))
            {
                if (!other.TryGetValue(index, out var variant))
                {
                    problems.Add(new SheetProblem(SheetProblemKind.MissingSizeVariant, index, [baseSheet.Path]));
                    continue;
                }
                // Only rows are compared. A sheet's column count is its animation frames, which the atlas
                // reads by position, so a wider sheet is padding rather than a different roster.
                if (baseSheet.TileGrid.Rows > 0 && variant.TileGrid.Rows != baseSheet.TileGrid.Rows)
                    problems.Add(new SheetProblem(SheetProblemKind.SizeVariantRowMismatch, index, [variant.Path]));
            }
        }
        return problems;
    }

    /// <summary>The lowest index nothing is using, or -1 when the folder is full.</summary>
    /// <remarks>Lowest rather than next-highest, so an index freed by a delete is taken again and the
    /// numbering stays dense. The consequence is deliberate and worth knowing: a map still painted with
    /// the deleted sheet's tiles will draw the new sheet's art.</remarks>
    public static int NextFreeIndex(SheetScan scan, int maxIndex)
    {
        var used = scan.Sheets.Select(s => s.Index).ToHashSet();
        for (int i = 0; i < maxIndex; i++)
            if (!used.Contains(i)) return i;
        return -1;
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    /// <summary>Renames a sheet's label, keeping its index and extension.</summary>
    /// <returns>The new path.</returns>
    /// <exception cref="ArgumentException">The name is not usable as a filename on every platform.</exception>
    public static string Rename(SheetEntry sheet, string newDisplayName)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        string label = (newDisplayName ?? "").Trim();
        string ext = Path.GetExtension(sheet.Path);
        string fileName = SheetFile.FileName(sheet.Index, label, ext);

        // The whole filename is validated, not the label, and asked of PortableFileName rather than of this
        // machine — a name Windows accepts and Linux does not would author a world that cannot be opened
        // there. Validating the assembled name is also what makes "NUL" a perfectly good label: the index
        // prefix means the stem is "0_NUL", and DOS only ever objected to the bare word.
        if (label.Length == 0 || !PortableFileName.IsValid(fileName))
            throw new ArgumentException($"'{label}' cannot be used as a sheet name.", nameof(newDisplayName));

        string target = Path.Combine(Path.GetDirectoryName(sheet.Path)!, fileName);
        if (!PathComparison.SameLocation(target, sheet.Path)) File.Move(sheet.Path, target);
        return target;
    }

    /// <summary>Copies a file into the folder as a new sheet at <paramref name="index"/>.</summary>
    /// <returns>The new path.</returns>
    public static string Import(string sourcePath, string dir, int index)
    {
        if (!SheetFile.IsSupported(sourcePath))
            throw new ArgumentException($"'{sourcePath}' is not a sheet file.", nameof(sourcePath));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "No index is free.");

        Directory.CreateDirectory(dir);
        string label = SheetFile.DisplayName(Path.GetFileNameWithoutExtension(sourcePath));
        string ext = Path.GetExtension(sourcePath);
        string fileName = SheetFile.FileName(index, PortableFileName.Sanitize(label), ext);
        string target = Path.Combine(dir, fileName);
        File.Copy(sourcePath, target, overwrite: false);
        return target;
    }

    /// <summary>Replaces a sheet's image, keeping its index so every map that used it still does.</summary>
    /// <returns>The path now holding the art, which changes when the new file is a different format.</returns>
    public static string Replace(SheetEntry sheet, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (!SheetFile.IsSupported(sourcePath))
            throw new ArgumentException($"'{sourcePath}' is not a sheet file.", nameof(sourcePath));

        string dir = Path.GetDirectoryName(sheet.Path)!;
        string ext = Path.GetExtension(sourcePath);
        string target = Path.Combine(dir, SheetFile.FileName(sheet.Index, sheet.DisplayName, ext));

        File.Copy(sourcePath, target, overwrite: true);
        // A BMP replaced by a PNG leaves the old file behind still claiming the index, which would be a
        // duplicate the moment the folder is read again.
        if (!PathComparison.SameLocation(target, sheet.Path)) File.Delete(sheet.Path);
        return target;
    }

    /// <summary>Moves a sheet to the recycle bin, freeing its index.</summary>
    /// <param name="sheet">The sheet to remove.</param>
    /// <param name="recycleDir">The bin folder.</param>
    /// <param name="assetRelativePath">Its path relative to the assets root, recorded when the sheet is a
    /// bundled one so the seeder does not put it back.</param>
    /// <param name="tombstoneDir">Where the deleted list lives, when that is not the bin folder itself —
    /// the manager gives each asset folder its own bin under one shared root, and the seeder reads one
    /// list at that root.</param>
    public static void Delete(SheetEntry sheet, string recycleDir, string? assetRelativePath = null,
        string? tombstoneDir = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Directory.CreateDirectory(recycleDir);

        string target = UniquePath(recycleDir, Path.GetFileName(sheet.Path));
        File.Move(sheet.Path, target);

        if (sheet.IsBundled && assetRelativePath is not null)
            AddTombstone(tombstoneDir ?? recycleDir, assetRelativePath);
    }

    /// <summary>Files sitting in the recycle bin, newest last.</summary>
    public static IReadOnlyList<string> ListRecycled(string recycleDir)
    {
        if (!Directory.Exists(recycleDir)) return [];
        return [.. Directory.EnumerateFiles(recycleDir)
            .Where(SheetFile.IsSupported)
            .OrderBy(p => p, StringComparer.Ordinal)];
    }

    /// <summary>Puts a recycled sheet back at <paramref name="index"/>.</summary>
    /// <returns>The restored path.</returns>
    /// <remarks>The index is supplied rather than read off the filename because the sheet's own number may
    /// have been taken while it sat in the bin.</remarks>
    public static string Restore(string recycledPath, string dir, int index, string? assetRelativePath = null,
        string? tombstoneDir = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "No index is free.");
        Directory.CreateDirectory(dir);

        string label = SheetFile.DisplayName(Path.GetFileNameWithoutExtension(recycledPath));
        string ext = Path.GetExtension(recycledPath);
        string target = Path.Combine(dir, SheetFile.FileName(index, label, ext));
        File.Move(recycledPath, target);

        if (assetRelativePath is not null)
            RemoveTombstone(tombstoneDir ?? Path.GetDirectoryName(recycledPath)!, assetRelativePath);
        return target;
    }

    // ── Tombstones ────────────────────────────────────────────────────────────

    /// <summary>Bundled files that were deliberately deleted, as asset-relative paths.
    ///
    /// <para>Without this the seeder undoes every delete: it copies each bundled file that is missing on
    /// every startup, so a shipped sheet moved to the bin is back before anyone sees it gone.</para></summary>
    public static IReadOnlySet<string> ReadTombstones(string recycleDir)
    {
        string path = Path.Combine(recycleDir, TombstoneFile);
        if (!File.Exists(path)) return new HashSet<string>(PathComparison.Comparer);
        try
        {
            var list = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
            return new HashSet<string>(list ?? [], PathComparison.Comparer);
        }
        catch
        {
            // An unreadable list means nothing is tombstoned, so the seeder restores. Losing a deletion is
            // recoverable; refusing to start the editor over a stray file is not.
            return new HashSet<string>(PathComparison.Comparer);
        }
    }

    private static void AddTombstone(string recycleDir, string assetRelativePath)
    {
        var set = new HashSet<string>(ReadTombstones(recycleDir), PathComparison.Comparer) { assetRelativePath };
        WriteTombstones(recycleDir, set);
    }

    private static void RemoveTombstone(string recycleDir, string assetRelativePath)
    {
        var set = new HashSet<string>(ReadTombstones(recycleDir), PathComparison.Comparer);
        if (set.Remove(assetRelativePath)) WriteTombstones(recycleDir, set);
    }

    private static void WriteTombstones(string recycleDir, IEnumerable<string> paths)
    {
        Directory.CreateDirectory(recycleDir);
        string json = JsonSerializer.Serialize(
            paths.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(recycleDir, TombstoneFile), json);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // A name already in the bin gets a counter rather than overwriting: two sheets deleted from different
    // indexes can share a label, and the second must not erase the first.
    private static string UniquePath(string dir, string fileName)
    {
        string candidate = Path.Combine(dir, fileName);
        if (!File.Exists(candidate)) return candidate;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int n = 2; ; n++)
        {
            candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
