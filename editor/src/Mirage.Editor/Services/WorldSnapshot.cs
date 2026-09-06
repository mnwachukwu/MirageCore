using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.Services;

/// <summary>What one side of a transfer holds: every record family, indexed by slot number.</summary>
public sealed class WorldSnapshot
{
    /// <summary>The ceilings this side runs on. A folder states them in its manifest; a server states them
    /// in the hello.</summary>
    public RecordLimits Limits { get; init; } = RecordLimits.Default;

    public ItemRecord[] Items { get; init; } = [];
    public NpcRecord[] Npcs { get; init; } = [];
    public ShopRecord[] Shops { get; init; } = [];
    public SpellRecord[] Spells { get; init; } = [];
    public ClassRecord[] Classes { get; init; } = [];
    public QuestRecord[] Quests { get; init; } = [];
    public ConversationRecord[] Conversations { get; init; } = [];
    public MapGroupRecord[] MapGroups { get; init; } = [];
    public MapRecord[] Maps { get; init; } = [];

    /// <summary>The section ids the transfer walks, in the order a reader sees them. The same ids the lock
    /// table and the nav rail use, so labels and log lines come from one place.</summary>
    public static readonly string[] Sections =
        ["Maps", "MapGroups", "Items", "NPCs", "Shops", "Spells", "Classes", "Quests", "Conversations"];

    /// <summary>How many slots this side holds for <paramref name="section"/>.</summary>
    public int CountOf(string section) => section switch
    {
        "Maps" => Limits.Maps,
        "MapGroups" => Limits.MapGroups,
        "Items" => Limits.Items,
        "NPCs" => Limits.Npcs,
        "Shops" => Limits.Shops,
        "Spells" => Limits.Spells,
        "Classes" => Constants.MaxClasses,
        "Quests" => Limits.Quests,
        "Conversations" => Limits.Conversations,
        _ => 0,
    };

    /// <summary>The record in a slot, or null when the slot is past this side's ceiling.</summary>
    public object? At(string section, int num)
    {
        if (num < 1) return null;
        return section switch
        {
            "Maps" => num < Maps.Length ? Maps[num] : null,
            "MapGroups" => num < MapGroups.Length ? MapGroups[num] : null,
            "Items" => num < Items.Length ? Items[num] : null,
            "NPCs" => num < Npcs.Length ? Npcs[num] : null,
            "Shops" => num < Shops.Length ? Shops[num] : null,
            "Spells" => num < Spells.Length ? Spells[num] : null,
            "Classes" => num < Classes.Length ? Classes[num] : null,
            "Quests" => num < Quests.Length ? Quests[num] : null,
            "Conversations" => num < Conversations.Length ? Conversations[num] : null,
            _ => null,
        };
    }

    /// <summary>The record's own name, for the diff list.</summary>
    public static string NameOf(object? record) => record switch
    {
        MapRecord m => m.Name.Length > 0 ? m.Name : m.DisplayName,
        MapGroupRecord g => g.Name.Length > 0 ? g.Name : g.DisplayName,
        ItemRecord i => i.Name,
        NpcRecord n => n.Name,
        ShopRecord s => s.Name,
        SpellRecord s => s.Name,
        ClassRecord c => c.Name,
        QuestRecord q => q.Name,
        ConversationRecord c => c.Name,
        _ => "",
    };
}

/// <summary>What uploading one record would do to the server's copy.</summary>
public enum WorldChangeKind
{
    /// <summary>Blank on the server, authored in the folder.</summary>
    Added,
    /// <summary>Authored on both sides, and not the same.</summary>
    Changed,
    /// <summary>Authored on the server, blank in the folder. Uploading it blanks the server's copy.</summary>
    Removed,
}

/// <summary>One record the upload would touch.</summary>
public sealed record WorldChange(string Section, int Num, string Name, WorldChangeKind Kind);

/// <summary>
/// Everything an upload would do, and the one thing it cannot.
///
/// <para><see cref="OverCeiling"/> counts authored folder records sitting above the server's ceiling for
/// their family. They are not changes and cannot become any: the server has no slot to put them in. Stated
/// rather than dropped, since a silently skipped record reads as a successful upload.</para>
/// </summary>
public sealed record WorldDiff(IReadOnlyList<WorldChange> Changes, int OverCeiling)
{
    public static readonly WorldDiff Empty = new([], 0);

    public IEnumerable<WorldChange> Of(WorldChangeKind kind) => Changes.Where(c => c.Kind == kind);
    public int Count(WorldChangeKind kind) => Changes.Count(c => c.Kind == kind);

    /// <summary>Nothing to do: no change either way, and nothing left behind.</summary>
    public bool IsEmpty => Changes.Count == 0 && OverCeiling == 0;
}
