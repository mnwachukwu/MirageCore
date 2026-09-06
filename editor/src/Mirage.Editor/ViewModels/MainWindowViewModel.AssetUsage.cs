using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;

namespace Mirage.Editor.ViewModels;

/// <summary>What is holding each art sheet up, for the asset manager's delete warning.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Who uses each sheet of one class of art, already worded for the row it appears on.
    ///
    /// <para>What counts as use differs per class and so does where the answer lives: tiles are painted
    /// onto maps, while sprites and item pictures are named by records the editor already holds in
    /// memory. Empty slots are skipped by the same rule the lists use — an unused record has no name,
    /// and counting one would report every blank NPC as a user of sheet 0.</para>
    /// </summary>
    public SheetUsageSummary DescribeSheetUsage(AssetCategoryKind kind) => kind switch
    {
        AssetCategoryKind.Sprites => SpriteUsage(),
        AssetCategoryKind.Items => ItemUsage(),
        _ => TileUsage(),
    };

    private SheetUsageSummary TileUsage()
    {
        var usage = MapEditor.ScanSheetUsage(out int readable, out int total);
        bool partial = readable < total;

        var text = usage.ToDictionary(
            kv => kv.Key,
            kv => EditorStrings.Format(
                partial ? EditorStrings.AssetManager_UsagePartial : EditorStrings.AssetManager_Usage,
                ("Maps", kv.Value.Maps), ("Tiles", kv.Value.Tiles)));

        return new SheetUsageSummary(text, EditorStrings.Get(partial
            ? EditorStrings.AssetManager_UsageNonePartial
            : EditorStrings.AssetManager_UsageNone));
    }

    private SheetUsageSummary SpriteUsage()
    {
        var npcs = new Dictionary<int, int>();
        foreach (var row in NpcEditor.Npcs)
        {
            if (!row.IsLoaded || string.IsNullOrWhiteSpace(row.Name)) continue;
            npcs[row.SpriteSheet] = npcs.GetValueOrDefault(row.SpriteSheet) + 1;
        }

        var classes = new Dictionary<int, int>();
        foreach (var row in ClassEditor.Classes)
        {
            if (!row.IsLoaded || string.IsNullOrWhiteSpace(row.Name)) continue;
            // One count per sprite, so a class on one sheet counts twice against it.
            classes[row.SpriteSheetMale] = classes.GetValueOrDefault(row.SpriteSheetMale) + 1;
            classes[row.SpriteSheetFemale] = classes.GetValueOrDefault(row.SpriteSheetFemale) + 1;
        }

        var text = new Dictionary<int, string>();
        foreach (int sheet in npcs.Keys.Concat(classes.Keys).Distinct())
            text[sheet] = EditorStrings.Format(EditorStrings.AssetManager_UsageSprites,
                ("Npcs", npcs.GetValueOrDefault(sheet)), ("Classes", classes.GetValueOrDefault(sheet)));

        return new SheetUsageSummary(text, EditorStrings.Get(EditorStrings.AssetManager_UsageNoneRecords));
    }

    private SheetUsageSummary ItemUsage()
    {
        var items = new Dictionary<int, int>();
        foreach (var row in ItemEditor.Items)
        {
            if (!row.IsLoaded || string.IsNullOrWhiteSpace(row.Name)) continue;
            items[row.ItemSheet] = items.GetValueOrDefault(row.ItemSheet) + 1;
        }

        var text = items.ToDictionary(
            kv => kv.Key,
            kv => EditorStrings.Format(EditorStrings.AssetManager_UsageItems, ("Items", kv.Value)));

        return new SheetUsageSummary(text, EditorStrings.Get(EditorStrings.AssetManager_UsageNoneRecords));
    }

    /// <summary>The loaded sheets of one asset folder, for the manager's row thumbnails.</summary>
    public IReadOnlyList<Bitmap?> SheetsOf(AssetFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder.Kind switch
        {
            AssetCategoryKind.Items => _bitmaps.Items,
            AssetCategoryKind.Sprites => folder.CellSize switch
            {
                >= 96 => _bitmaps.Sprites96,
                64 => _bitmaps.Sprites64,
                _ => _bitmaps.Sprites,
            },
            _ => _bitmaps.Tilesets,
        };
    }
}
