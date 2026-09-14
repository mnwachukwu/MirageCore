namespace Mirage.Shared.Extensibility;

/// <summary>
/// The record families the engine itself ships, as one table.
///
/// <para><b>Everything that treats a family as a family reads this row.</b> The folder its records are
/// kept in, the filename each one takes, how far the world is padded, the section the editor lists it
/// under, and what a world transfer carries are one set of facts about one family — held once, so
/// adding a family is a row rather than an edit in a dozen switches, and so a game can add one the
/// engine has never heard of.</para>
///
/// <para><b>Not every family is the same shape, and the row says how.</b> Maps and map groups are read
/// and written one file at a time; the rest are loaded as a whole numbered set at boot. Accounts are
/// authored in the editor but are not world content and never travel with a world. Reading any single
/// property across all rows would therefore be wrong for at least one consumer — which is why the
/// differences are declared here rather than left as omissions from six separate lists.</para>
/// </summary>
public static class CoreRecordFamilies
{
    // ── Section ids ───────────────────────────────────────────────────────────
    //
    // These strings are persisted: the editor keys its per-section auto-save schedule by them, and a
    // renamed id silently orphans an author's settings for that section rather than failing.

    public const string Maps = "Maps";
    public const string MapGroups = "MapGroups";
    public const string Items = "Items";
    public const string Npcs = "NPCs";
    public const string Shops = "Shops";
    public const string Conversations = "Conversations";

    /// <summary>Every family that is part of a world, in the order the editor lists them.
    ///
    /// <para>A world is what these describe: zip the folders they name and you have handed somebody the
    /// world and nothing else.</para></summary>
    public static IReadOnlyList<RecordFamily> World { get; } =
    [
        new()
        {
            Id = Maps,
            Icon = "grid",
            LabelKey = "MainWindow_Section_Maps",
            Directory = "maps",
            FilePrefix = "map",
            DefaultLimit = 1000,
            LoadsIndividually = true,
        },
        new()
        {
            Id = MapGroups,
            Icon = "quads",
            LabelKey = "MainWindow_Section_MapGroups",
            Directory = "map_groups",
            FilePrefix = "map_group",
            DefaultLimit = 1000,
            LoadsIndividually = true,
        },
        new()
        {
            Id = Items,
            Icon = "bag",
            LabelKey = "MainWindow_Section_Items",
            Directory = "items",
            FilePrefix = "item",
            DefaultLimit = 1000,
        },
        new()
        {
            Id = Npcs,
            Icon = "person",
            LabelKey = "MainWindow_Section_Npcs",
            Directory = "npcs",
            FilePrefix = "npc",
            DefaultLimit = 1000,
        },
        new()
        {
            Id = Shops,
            Icon = "shop",
            LabelKey = "MainWindow_Section_Shops",
            Directory = "shops",
            FilePrefix = "shop",
            DefaultLimit = 1000,
        },
        new()
        {
            Id = Conversations,
            Icon = "bubble",
            LabelKey = "MainWindow_Section_Conversations",
            Directory = "conversations",
            FilePrefix = "conversation",
            DefaultLimit = 1000,
        },
    ];

    /// <summary>The family with this id, or null. Ordinal and case-sensitive, because the id is also a
    /// folder name and a persisted settings key.</summary>
    public static RecordFamily? Find(string id)
        => World.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    /// <summary>The family with this id.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No family has that id.</exception>
    public static RecordFamily Get(string id)
        => Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown record family.");

    /// <summary>Every world family's folder name, for a caller creating them all.</summary>
    public static IEnumerable<string> WorldDirectories => World.Select(f => f.EffectiveDirectory);
}
