using System.Text.Json.Serialization;

namespace Mirage.Shared;

/// <summary>
/// How many of each record family a server has room for.
///
/// <para><b>These are per-server, not protocol-wide.</b> Nothing on the wire constrains them — the protocol
/// is JSON, so a record number is a number of whatever width the field declares. A server sizes its own
/// tables from this, states it in the pre-login hello, and the client sizes to match. That is the whole
/// reason it exists: <c>Constants.Max*</c> is <c>const</c>, so a client compiled against 1000 items would
/// REJECT item 1200 as out of range on a server that authored it.</para>
///
/// <para>The defaults match <c>Constants.Max*</c>, so an operator who configures nothing gets the stock
/// world size. Raising costs three things, all local and known: the server pads
/// each family to its ceiling in the RUNTIME data folder on first launch, <c>GameWorld</c> allocates one
/// array per family, and the editor's slot pickers list that many rows.</para>
///
/// <para>Not every ceiling belongs here. The per-character shapes — inventory, bank, hotkeys, spellbook,
/// character slots — are baked into the save format, so changing one is a data migration rather than a
/// setting, and they stay <c>const</c>.</para>
/// </summary>
public sealed record RecordLimits
{
    /// <summary>What a server runs on when nothing is configured, and what every one of these was as a
    /// compile-time constant.</summary>
    public static readonly RecordLimits Default = new();

    public int Items { get; init; } = 1000;
    public int Npcs { get; init; } = 1000;
    public int Shops { get; init; } = 1000;
    public int Spells { get; init; } = 1000;
    public int Quests { get; init; } = 1000;
    public int Conversations { get; init; } = 1000;
    public int Maps { get; init; } = 1000;

    /// <summary>Editor-facing. The server keeps groups in an unbounded dictionary — only files that exist
    /// are loaded — but the pickers use the same 1-based slot model as the other families, so the list
    /// needs a length.</summary>
    public int MapGroups { get; init; } = 1000;

    /// <summary>A copy with every family at least 1 and no larger than <paramref name="ceiling"/>.
    ///
    /// <para>Applied when a config is read and again when a hello is received. A family of 0 would mean a
    /// world with no items in it and every lookup failing; the upper bound is the receiving side's own
    /// sanity limit, so a misconfigured — or hostile — server cannot make a client allocate arbitrarily.</para></summary>
    public RecordLimits Clamped(int ceiling) => new()
    {
        Items = Math.Clamp(Items, 1, ceiling),
        Npcs = Math.Clamp(Npcs, 1, ceiling),
        Shops = Math.Clamp(Shops, 1, ceiling),
        Spells = Math.Clamp(Spells, 1, ceiling),
        Quests = Math.Clamp(Quests, 1, ceiling),
        Conversations = Math.Clamp(Conversations, 1, ceiling),
        Maps = Math.Clamp(Maps, 1, ceiling),
        MapGroups = Math.Clamp(MapGroups, 1, ceiling),
    };

    /// <summary>The largest any one family may be set to. Not a protocol constraint — it is a backstop
    /// against a typo costing gigabytes, since every family allocates an array of this length on both
    /// ends. Raise it if a world genuinely needs more.</summary>
    [JsonIgnore]
    public static int Ceiling => 100_000;
}
