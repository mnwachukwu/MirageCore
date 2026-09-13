using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;

namespace Mirage.Editor.Services;

/// <summary>What one side of a transfer holds: every record family, indexed by slot number.
///
/// <para><b>Which families those are is a property of the side, not of the build.</b> A folder states them
/// in its manifest and a server states them on the login response, so two sides can disagree about what
/// families exist at all — which is exactly the case a transfer has to report rather than skip.</para>
/// </summary>
public sealed class WorldSnapshot
{
    /// <summary>The ceilings this side runs on. A folder states them in its manifest; a server states them
    /// in the hello.</summary>
    public RecordLimits Limits { get; init; } = RecordLimits.Default;

    public ItemRecord[] Items { get; init; } = [];
    public NpcRecord[] Npcs { get; init; } = [];
    public ShopRecord[] Shops { get; init; } = [];
    public ConversationRecord[] Conversations { get; init; } = [];
    public MapGroupRecord[] MapGroups { get; init; } = [];
    public MapRecord[] Maps { get; init; } = [];

    /// <summary>The records of every family a MODULE declared, by family id and 1-based slot. Empty for a
    /// world made of Core's own families and nothing else.</summary>
    public IReadOnlyDictionary<string, AttributeBag[]> ModuleRecords { get; init; } =
        new Dictionary<string, AttributeBag[]>(StringComparer.Ordinal);

    /// <summary>Every family this side holds, in the order a reader sees them. Core's own unless a game
    /// declared more.</summary>
    public IReadOnlyList<RecordFamily> Families { get; init; } = CoreRecordFamilies.World;

    /// <summary>The section ids the transfer walks. The same ids the lock table and the nav rail use, so
    /// labels and log lines come from one place.</summary>
    public IEnumerable<string> Sections => Families.Select(f => f.Id);

    /// <summary>The family behind a section id, or null for one this side does not hold.</summary>
    public RecordFamily? FamilyOf(string section) =>
        Families.FirstOrDefault(f => string.Equals(f.Id, section, StringComparison.Ordinal));

    /// <summary>How many slots this side holds for <paramref name="section"/>. A section this side does
    /// not know holds none, so a transfer skips it rather than walking a ceiling it cannot fill.</summary>
    public int CountOf(string section) => FamilyOf(section) is { } family ? Limits.For(family) : 0;

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
            "Conversations" => num < Conversations.Length ? Conversations[num] : null,
            _ => ModuleRecords.TryGetValue(section, out var records) && num < records.Length
                ? records[num]
                : null,
        };
    }

    /// <summary>The record's own name, for the diff list. A game's record is named by whichever field its
    /// family nominated, which is the only thing the engine reads out of one.</summary>
    public static string NameOf(RecordFamily? family, object? record) => record switch
    {
        MapRecord m => m.Name.Length > 0 ? m.Name : m.DisplayName,
        MapGroupRecord g => g.Name.Length > 0 ? g.Name : g.DisplayName,
        ItemRecord i => i.Name,
        NpcRecord n => n.Name,
        ShopRecord s => s.Name,
        ConversationRecord c => c.Name,
        AttributeBag bag when family is { NameFieldKey.Length: > 0 } => bag[family.NameFieldKey].AsText(),
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
/// <para><see cref="OverCeiling"/> counts authored folder records the server has nowhere to put: above its
/// ceiling for their family, or belonging to a family it does not have at all. They are not changes and
/// cannot become any. Stated rather than dropped, since a silently skipped record reads as a successful
/// upload.</para>
/// </summary>
public sealed record WorldDiff(IReadOnlyList<WorldChange> Changes, int OverCeiling)
{
    public static readonly WorldDiff Empty = new([], 0);

    public IEnumerable<WorldChange> Of(WorldChangeKind kind) => Changes.Where(c => c.Kind == kind);
    public int Count(WorldChangeKind kind) => Changes.Count(c => c.Kind == kind);

    /// <summary>Nothing to do: no change either way, and nothing left behind.</summary>
    public bool IsEmpty => Changes.Count == 0 && OverCeiling == 0;
}
