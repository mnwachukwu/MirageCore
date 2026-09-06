using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;

namespace Mirage.Editor.ViewModels;

/// <summary>One sheet in the manager's list.</summary>
public sealed partial class AssetSheetRowViewModel : ObservableObject
{
    private readonly Func<AssetSheetRowViewModel, Task> _rename;
    private readonly Func<AssetSheetRowViewModel, Task> _replace;
    private readonly Func<AssetSheetRowViewModel, Task> _delete;

    public AssetSheetRowViewModel(
        SheetEntry sheet, string usageText, Bitmap? preview,
        Func<AssetSheetRowViewModel, Task> rename,
        Func<AssetSheetRowViewModel, Task> replace,
        Func<AssetSheetRowViewModel, Task> delete)
    {
        Sheet = sheet;
        Preview = preview;
        _name = sheet.DisplayName;
        _rename = rename;
        _replace = replace;
        _delete = delete;
        UsageText = usageText;

        Title = $"#{sheet.Index}";
        Detail = sheet.PixelWidth > 0
            ? EditorStrings.Format(EditorStrings.AssetManager_SheetDetail,
                ("Width", sheet.PixelWidth), ("Height", sheet.PixelHeight),
                ("Cols", sheet.TileGrid.Cols), ("Rows", sheet.TileGrid.Rows),
                ("Size", Kb(sheet.Bytes)))
            : EditorStrings.Format(EditorStrings.AssetManager_SheetDetailUnknown, ("Size", Kb(sheet.Bytes)));

        // Which transparency model this file uses, stated per sheet: the rule is decided by the extension,
        // and a reader should not have to remember which of their sheets is which.
        TransparencyText = EditorStrings.Get(sheet.Transparency switch
        {
            SheetTransparency.ColorKey => EditorStrings.AssetManager_TransparencyKey,
            SheetTransparency.Alpha => EditorStrings.AssetManager_TransparencyAlpha,
            _ => EditorStrings.AssetManager_TransparencyNone,
        });
    }

    public SheetEntry Sheet { get; }
    public Bitmap? Preview { get; }
    public string Title { get; }
    public string Detail { get; }
    public string UsageText { get; }
    public string TransparencyText { get; }

    /// <summary>The label half of the filename, edited in place.</summary>
    [ObservableProperty] private string _name;

    public string RenameLabel => EditorStrings.Get(EditorStrings.AssetManager_Rename);
    public string ReplaceLabel => EditorStrings.Get(EditorStrings.AssetManager_Replace);
    public string DeleteLabel => EditorStrings.Get(EditorStrings.AssetManager_Delete);

    [RelayCommand] private Task Rename() => _rename(this);
    [RelayCommand] private Task Replace() => _replace(this);
    [RelayCommand] private Task Delete() => _delete(this);

    private static string Kb(long bytes) => $"{Math.Max(1, bytes / 1024):N0} KB";
}

/// <summary>One thing wrong with the folder, and the button that puts it right.</summary>
public sealed partial class AssetProblemRowViewModel(
    SheetProblem problem, string text, string fixLabel, Func<SheetProblem, Task> repair) : ObservableObject
{
    public string Text { get; } = text;
    public string FixLabel { get; } = fixLabel;
    // A repair here means giving the file a free index. The two that are about the image itself have no
    // filename to fix, so they are reported and left to the artist.
    public bool CanFix { get; } = problem.Kind is not (SheetProblemKind.NotTileAligned or SheetProblemKind.PngWithoutTransparency);

    [RelayCommand] private Task Fix() => repair(problem);
}

/// <summary>One class of art, as the category selector shows it.</summary>
public sealed record AssetCategoryOption(AssetCategoryKind Kind)
{
    public string Label => EditorStrings.Get(Kind switch
    {
        AssetCategoryKind.Sprites => EditorStrings.AssetManager_CategorySprites,
        AssetCategoryKind.Items => EditorStrings.AssetManager_CategoryItems,
        _ => EditorStrings.AssetManager_CategoryTiles,
    });

    public override string ToString() => Label;
}

/// <summary>What uses each sheet of one category, already worded.</summary>
/// <param name="ByIndex">Usage line per sheet number; an absent number is unused.</param>
/// <param name="NoneText">What to say for a sheet nothing uses.</param>
public sealed record SheetUsageSummary(IReadOnlyDictionary<int, string> ByIndex, string NoneText);

/// <summary>A sheet in the recycle bin.</summary>
public sealed partial class RecycledSheetRowViewModel(
    string path, string restoreLabel, Func<string, Task> restore) : ObservableObject
{
    public string FileName { get; } = Path.GetFileName(path);
    public string RestoreLabel { get; } = restoreLabel;

    [RelayCommand] private Task Restore() => restore(path);
}

/// <summary>
/// The asset manager: everything in a sheet folder, and the operations that change it.
///
/// <para>Built around one fact — a sheet's number is data and its name is not. Renaming is free and cannot
/// break a world; anything that moves a number silently repoints art, and there is no cross-map validation
/// anywhere in the codebase to catch it. So renaming is inline and immediate, while deleting counts what it
/// would cost first and asks.</para>
///
/// <para>It also surfaces what the loaders swallow. A file with no index, two files claiming one index, an
/// index past the ceiling: each means a sheet is absent with no error anywhere, and today the only way to
/// find out is to notice that tiles you painted are blank.</para>
/// </summary>
public sealed partial class AssetManagerDialogViewModel : ObservableObject
{
    private readonly string _bundledDir;

    public AssetManagerDialogViewModel(string assetsDir, string bundledDir)
    {
        AssetsDir = assetsDir;
        _bundledDir = bundledDir;
        _categories = [.. Enum.GetValues<AssetCategoryKind>().Select(k => new AssetCategoryOption(k))];
        _category = _categories[0];
        _spriteSize = SpriteSizes[0];
    }

    /// <summary>The assets root being managed. Shown in full, because the AssetsDir setting means this is
    /// not necessarily the folder the game reads.</summary>
    public string AssetsDir { get; }

    // ── What is being managed ─────────────────────────────────────────────────

    /// <summary>The classes of art, as the category selector offers them.</summary>
    public IReadOnlyList<AssetCategoryOption> Categories => _categories;
    private readonly IReadOnlyList<AssetCategoryOption> _categories;

    [ObservableProperty] private AssetCategoryOption _category;

    /// <summary>
    /// The footprint sizes a sprite sheet is split across. A FIXED list: built once, and the same list
    /// whatever category is showing.
    ///
    /// <para>🔴 It must stay fixed. A ComboBox whose ItemsSource stops containing its SelectedItem clears
    /// that selection and writes the null back through the two-way binding, and the generated setter
    /// stores the null before any hook can refuse it — leaving the backing field empty underneath code
    /// that is still running. Neither ordering the notifications nor guarding the hook can prevent that,
    /// because by then the null is already in the field. A list that never changes cannot clear a
    /// selection, which is what keeps the failure impossible rather than merely handled.</para>
    /// </summary>
    public IReadOnlyList<AssetFolder> SpriteSizes { get; } = AssetFolder.For(AssetCategoryKind.Sprites);

    [ObservableProperty] private AssetFolder _spriteSize;

    /// <summary>Sprites are the one class split across more than one folder, so they are the only
    /// category that offers the size selector.</summary>
    public bool HasSizes => Category.Kind == AssetCategoryKind.Sprites;

    partial void OnCategoryChanged(AssetCategoryOption value)
    {
        OnPropertyChanged(nameof(HasSizes));
        Status = "";
        Refresh();
    }

    partial void OnSpriteSizeChanged(AssetFolder value)
    {
        Status = "";
        Refresh();
    }

    /// <summary>Names the folder on screen. The path is absolute, because the AssetsDir setting means
    /// this is not necessarily the folder the game reads.</summary>
    public string Intro => EditorStrings.Format(EditorStrings.AssetManager_Intro,
        ("Category", Category.Label), ("Path", SheetDir));

    // The folder being managed is DERIVED rather than selected: a category has exactly one, unless it is
    // sprites, where the size picks one of three. Nothing binds to this, so nothing can clear it.
    private AssetFolder Folder => Category.Kind == AssetCategoryKind.Sprites
        ? SpriteSize ?? SpriteSizes[0]
        : AssetFolder.For(Category.Kind)[0];
    private string SheetDir => Folder.Under(AssetsDir);
    private string BundledSheetDir => Folder.Under(_bundledDir);

    // The bin sits beside the graphics folder rather than inside it, and each asset folder keeps its own
    // subfolder of it, so a restore knows which folder the sheet came out of. One shared bin could only
    // guess, and would put a deleted tile back among the sprites.
    private string RecycleRoot => EditorPaths.RecycleBinFor(AssetsDir);
    private string RecycleDir => Folder.Under(RecycleRoot);

    /// <summary>Set by the shell: the usage line for every sheet in one category, counted once per
    /// refresh. What "uses" a sheet differs per category — maps paint tiles, records name sprites and
    /// item pictures — so the counting belongs where those lists live.</summary>
    public Func<AssetCategoryKind, SheetUsageSummary>? UsageProvider { get; set; }

    /// <summary>Set by the shell: the loaded sheets of one folder, for the row thumbnails.</summary>
    public Func<AssetFolder, IReadOnlyList<Bitmap?>>? PreviewProvider { get; set; }

    /// <summary>Set by the view: picks one image file to bring in, or null when cancelled.</summary>
    public Func<Task<string?>>? PickSheetFileAsync { get; set; }

    /// <summary>Set by the view: asks a yes/no question.</summary>
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Set by the view: opens a folder in the desktop's file browser.</summary>
    public Func<string, Task>? RevealFolderAsync { get; set; }

    /// <summary>Raised when something on disk changed and the editor should re-read its assets.</summary>
    public event Action? AssetsChanged;

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? Closed;

    public IReadOnlyList<AssetSheetRowViewModel> Sheets { get; private set; } = [];
    public IReadOnlyList<AssetProblemRowViewModel> Problems { get; private set; } = [];
    public IReadOnlyList<RecycledSheetRowViewModel> Recycled { get; private set; } = [];

    public bool HasSheets => Sheets.Count > 0;
    public bool HasProblems => Problems.Count > 0;
    public bool HasRecycled => Recycled.Count > 0;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "";

    /// <summary>Reads the folder and rebuilds every list.</summary>
    public void Refresh()
    {
        var scan = ScanCurrent();
        var usage = UsageProvider?.Invoke(Category.Kind)
            ?? new SheetUsageSummary(new Dictionary<int, string>(), "");
        var previews = PreviewProvider?.Invoke(Folder) ?? [];

        Sheets = [.. scan.Sheets.Select(s => new AssetSheetRowViewModel(
            s,
            usage.ByIndex.TryGetValue(s.Index, out var text) ? text : usage.NoneText,
            s.Index < previews.Count ? previews[s.Index] : null,
            DoRenameAsync, DoReplaceAsync, DoDeleteAsync))];

        // Cross-size checks belong to the class rather than the folder: an index missing from 96x96 is a
        // fact about that sprite sheet, and is worth seeing whichever size is on screen.
        var problems = Category.Kind == AssetCategoryKind.Sprites
            ? [.. scan.Problems, .. SheetLibrary.ScanSizeVariants(ScanSpriteSizes())]
            : scan.Problems;

        Problems = [.. problems.Select(p => new AssetProblemRowViewModel(
            p, DescribeProblem(p), EditorStrings.Get(EditorStrings.AssetManager_Repair), DoRepairAsync))];

        Recycled = [.. SheetLibrary.ListRecycled(RecycleDir).Select(p => new RecycledSheetRowViewModel(
            p, EditorStrings.Get(EditorStrings.AssetManager_Restore), DoRestoreAsync))];

        int free = Constants.MaxTilesets - scan.Sheets.Count;
        long bytes = scan.Sheets.Sum(s => s.Bytes);
        Summary = EditorStrings.Format(EditorStrings.AssetManager_Summary,
            ("Count", scan.Sheets.Count), ("Free", free), ("Max", Constants.MaxTilesets),
            ("Size", $"{Math.Max(1, bytes / 1024):N0} KB"));

        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(Sheets));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(Recycled));
        OnPropertyChanged(nameof(HasSheets));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasRecycled));
    }

    private SheetScan ScanCurrent() =>
        SheetLibrary.Scan(SheetDir, Constants.MaxTilesets, BundledSheetDir, Folder.CellSize);

    private Dictionary<int, SheetScan> ScanSpriteSizes() =>
        AssetFolder.For(AssetCategoryKind.Sprites).ToDictionary(
            f => f.CellSize,
            f => SheetLibrary.Scan(f.Under(AssetsDir), Constants.MaxTilesets, f.Under(_bundledDir), f.CellSize));

    private string DescribeProblem(SheetProblem p) => p.Kind switch
    {
        SheetProblemKind.DuplicateIndex => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemDuplicate,
            ("Index", p.Index), ("Files", string.Join(", ", p.Paths.Select(Path.GetFileName)))),
        SheetProblemKind.NoIndexPrefix => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemNoIndex, ("File", Path.GetFileName(p.Paths[0]))),
        SheetProblemKind.IndexOutOfRange => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemOutOfRange,
            ("File", Path.GetFileName(p.Paths[0])), ("Index", p.Index), ("Max", Constants.MaxTilesets - 1)),
        SheetProblemKind.PngWithoutTransparency => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemNoAlpha, ("File", Path.GetFileName(p.Paths[0]))),
        SheetProblemKind.MissingSizeVariant => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemMissingSize,
            ("Index", p.Index), ("File", Path.GetFileName(p.Paths[0]))),
        SheetProblemKind.SizeVariantRowMismatch => EditorStrings.Format(
            EditorStrings.AssetManager_ProblemSizeRows,
            ("Index", p.Index), ("File", Path.GetFileName(p.Paths[0]))),
        _ => EditorStrings.Format(EditorStrings.AssetManager_ProblemNotAligned,
            ("File", Path.GetFileName(p.Paths[0])), ("Size", Folder.CellSize)),
    };

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (PickSheetFileAsync is null) return;
        string? source = await PickSheetFileAsync();
        if (source is null) return;

        var scan = ScanCurrent();
        int index = SheetLibrary.NextFreeIndex(scan, Constants.MaxTilesets);
        if (index < 0)
        {
            Status = EditorStrings.Format(EditorStrings.AssetManager_FullNoIndex, ("Max", Constants.MaxTilesets));
            return;
        }

        Run(() =>
        {
            string landed = SheetLibrary.Import(source, SheetDir, index);
            return EditorStrings.Format(EditorStrings.AssetManager_Imported,
                ("File", Path.GetFileName(landed)), ("Index", index));
        });
    }

    private Task DoRenameAsync(AssetSheetRowViewModel row)
    {
        if (row.Name.Trim() == row.Sheet.DisplayName) return Task.CompletedTask;
        Run(() =>
        {
            string path = SheetLibrary.Rename(row.Sheet, row.Name);
            return EditorStrings.Format(EditorStrings.AssetManager_Renamed, ("File", Path.GetFileName(path)));
        });
        return Task.CompletedTask;
    }

    private async Task DoReplaceAsync(AssetSheetRowViewModel row)
    {
        if (PickSheetFileAsync is null) return;
        string? source = await PickSheetFileAsync();
        if (source is null) return;

        Run(() =>
        {
            SheetLibrary.Replace(row.Sheet, source);
            return EditorStrings.Format(EditorStrings.AssetManager_Replaced, ("Index", row.Sheet.Index));
        });
    }

    private async Task DoDeleteAsync(AssetSheetRowViewModel row)
    {
        if (ConfirmAsync is not null && !await ConfirmAsync(DeleteWarning(row))) return;

        Run(() =>
        {
            string relative = Folder.RelativeTo(Path.GetFileName(row.Sheet.Path));
            SheetLibrary.Delete(row.Sheet, RecycleDir, relative, RecycleRoot);
            return EditorStrings.Format(EditorStrings.AssetManager_Deleted, ("Index", row.Sheet.Index));
        });
    }

    // The warning names the cost rather than gesturing at it: a sheet nothing uses is a clean delete, and
    // one that is in use takes the art with it. What "the art" means differs per class, so the
    // consequence sentence does too.
    private string DeleteWarning(AssetSheetRowViewModel row) =>
        EditorStrings.Format(EditorStrings.AssetManager_ConfirmDelete,
            ("Index", row.Sheet.Index), ("Name", row.Sheet.DisplayName), ("Usage", row.UsageText),
            ("Consequence", EditorStrings.Get(Category.Kind switch
            {
                AssetCategoryKind.Sprites => EditorStrings.AssetManager_ConsequenceSprites,
                AssetCategoryKind.Items => EditorStrings.AssetManager_ConsequenceItems,
                _ => EditorStrings.AssetManager_ConsequenceTiles,
            })));

    private async Task DoRestoreAsync(string recycledPath)
    {
        var scan = ScanCurrent();
        int index = SheetLibrary.NextFreeIndex(scan, Constants.MaxTilesets);
        if (index < 0)
        {
            Status = EditorStrings.Format(EditorStrings.AssetManager_FullNoIndex, ("Max", Constants.MaxTilesets));
            return;
        }

        // The sheet's own number may have been taken while it sat in the bin, so the restore is told which
        // index to use rather than reading one off the filename.
        int wanted = SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(recycledPath));
        if (wanted >= 0 && wanted != index && ConfirmAsync is not null &&
            !await ConfirmAsync(EditorStrings.Format(EditorStrings.AssetManager_ConfirmRestoreMoved,
                ("Was", wanted), ("Now", index))))
        {
            return;
        }

        Run(() =>
        {
            string relative = Folder.RelativeTo(Path.GetFileName(recycledPath));
            string path = SheetLibrary.Restore(recycledPath, SheetDir, index, relative, RecycleRoot);
            return EditorStrings.Format(EditorStrings.AssetManager_Restored,
                ("File", Path.GetFileName(path)), ("Index", index));
        });
    }

    private Task DoRepairAsync(SheetProblem problem)
    {
        var scan = ScanCurrent();
        int index = SheetLibrary.NextFreeIndex(scan, Constants.MaxTilesets);
        if (index < 0)
        {
            Status = EditorStrings.Format(EditorStrings.AssetManager_FullNoIndex, ("Max", Constants.MaxTilesets));
            return Task.CompletedTask;
        }

        // Every repair is the same move: give the offending file a number nothing else is using. A duplicate
        // renumbers its second claimant, an unindexed file is adopted, an out-of-range one is brought in.
        string offender = problem.Kind == SheetProblemKind.DuplicateIndex ? problem.Paths[^1] : problem.Paths[0];
        Run(() =>
        {
            string label = SheetFile.DisplayName(Path.GetFileNameWithoutExtension(offender));
            string target = Path.Combine(SheetDir,
                SheetFile.FileName(index, PortableFileName.Sanitize(label), Path.GetExtension(offender)));
            File.Move(offender, target);
            return EditorStrings.Format(EditorStrings.AssetManager_Repaired,
                ("File", Path.GetFileName(target)), ("Index", index));
        });
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (RevealFolderAsync is null) return;
        Directory.CreateDirectory(SheetDir);
        await RevealFolderAsync(SheetDir);
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke();

    // Every file operation lands here: do it, say what happened, re-read the folder, and tell the editor to
    // reload. A failure becomes a status line rather than an exception, because the dialog is the only place
    // the author can see what state the folder is actually in.
    private void Run(Func<string> operation)
    {
        try
        {
            Status = operation();
            EditorLog.Info("Asset manager: {Status}", Status);
            AssetsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Status = EditorStrings.Format(EditorStrings.AssetManager_Failed, ("Error", ex.Message));
            EditorLog.Warn("Asset manager operation failed: {Error}", ex.Message);
        }
        Refresh();
    }
}
