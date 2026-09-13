using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// What a world folder says about itself, read from <c>world.json</c> at its root.
///
/// <para>A world's size is a property of the world, not of the program that opens it. A server states its
/// own in the pre-login hello and the editor sizes to match; offline there is nobody to ask, so the folder
/// carries the same answer. Two worlds of different sizes can then sit side by side and each open at its
/// own ceiling.</para>
///
/// <para>The file is optional: absent or unreadable, every default below applies.</para>
/// </summary>
[JsonConverter(typeof(Serialization.WorldManifestConverter))]
public sealed record WorldManifest
{
    /// <summary>The file's name at the root of a world folder.</summary>
    [JsonIgnore]
    public const string FileName = "world.json";

    /// <summary>The game name a world folder declares, read on its own, or blank for a folder that
    /// declares none — including one with no manifest at all, or an unreadable one.
    ///
    /// <para>A server has to answer "what is this game called" before it has built anything that could
    /// load a world, because the answer is settled once and injected everywhere that renders it. This
    /// reads the single string rather than the whole manifest, so it needs no serializer options and
    /// cannot disagree with a full load about a nested setting it never looked at.</para></summary>
    public static string GameNameIn(string worldDir)
    {
        string path = Path.Combine(worldDir, FileName);
        if (!File.Exists(path)) return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("gameName", out var name)
                   && name.ValueKind == System.Text.Json.JsonValueKind.String
                ? name.GetString()?.Trim() ?? ""
                : "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            // A folder that cannot say what it is still runs, under whatever name the next layer gives it.
            return "";
        }
    }

    /// <summary>How many of each record family this world has room for. Clamped on read, so a hand-edited
    /// file cannot ask for a zero-length family or an allocation measured in gigabytes.</summary>
    public RecordLimits Records
    {
        get;
        init => field = (value ?? RecordLimits.Default).Clamped(RecordLimits.Ceiling);
    } = RecordLimits.Default;

    /// <summary>What this world calls itself, for whoever is HOLDING it — the editor's title bar and
    /// recent-worlds list, the server window, the logs.
    ///
    /// <para>It never reaches a player. A player sees <see cref="GameName"/>, which is a different thing:
    /// one identifies a set of records an operator is working on, the other identifies the game they are
    /// all part of. An operator running a live world and a test copy of it tells them apart by this, and
    /// by nothing else.</para>
    ///
    /// <para>Blank is the stored form of "unnamed", and stays blank: what to CALL an unnamed world is a
    /// question for whoever is showing it, and each app answers it in its own language.</para></summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when this world has a name of its own rather than running on the stock answer.</summary>
    [JsonIgnore]
    public bool IsNamed => !string.IsNullOrWhiteSpace(Name);

    /// <summary>What the GAME built on this world is called — the name a player sees, on the menu, in the
    /// window title and in every message the server addresses to them.
    ///
    /// <para>🔴 <b>This is where a game names itself, and it needs no compiler.</b> A game is a world
    /// folder plus the modules that give it rules; both are things somebody authors, so the name belongs
    /// with them rather than in a build property that only a fork rebuilding the engine can reach.</para>
    ///
    /// <para>Blank defers. A server resolves what to announce as the operator's own choice first, then
    /// this, then the engine's name — so a world that says nothing is published under whatever engine is
    /// running it, and an operator can always override a name for their own installation.</para>
    ///
    /// <para><b>Never use it for a file path.</b> Executable names and every per-user folder stay on
    /// <see cref="Constants.GameName"/>: naming a game must not move anybody's files.</para></summary>
    public string GameName { get; init; } = string.Empty;

    /// <summary>The size a NEW map in this world is created at, and what a blank map slot is padded to.
    /// A map may then be any size it likes — this is the starting point, not a rule.
    ///
    /// <para>It lives on the world rather than in a program's settings because it belongs to the records:
    /// open the same world in two places and a new map comes out the same size in both.</para></summary>
    public MapSize DefaultMapSize
    {
        get;
        init => field = value.Clamped();
    } = MapSize.Default;

    /// <summary>The appearances this world offers at character creation, in the order a player sees
    /// them.
    ///
    /// <para>A world that names none offers <see cref="CharacterAppearance.DefaultSet"/> — one look —
    /// so a brand-new world is playable before anybody has authored a roster. Naming even one replaces
    /// that entirely: the list is what the author decided, never a floor the engine adds to.</para></summary>
    public IReadOnlyList<CharacterAppearance> Appearances
    {
        get;
        init => field = value is { Count: > 0 } given ? given : CharacterAppearance.DefaultSet;
    } = CharacterAppearance.DefaultSet;

    /// <summary>What a new character is created holding, in the order the slots are filled.
    ///
    /// <para>Empty is the common answer and a valid one — a character who starts with nothing is a
    /// perfectly good opening. Equipment arrives worn; see <see cref="StartingItem"/>.</para></summary>
    public IReadOnlyList<StartingItem> StartingItems
    {
        get;
        init => field = StartingLoadout.Normalize(value);
    } = [];

    /// <summary>What a stain on the ground looks like, packed 0xRRGGBB. Blood in most worlds, which is
    /// why the default is a dark arterial red; oil, soot, spilled dye or melt-water in others.
    ///
    /// <para>One color for the whole world, not one per stain: the renderer merges every stain on a layer
    /// into one field so overlapping ones union smoothly instead of darkening at their seams, and it tints
    /// that field once. A second color would mean a second field.</para></summary>
    public uint DecalColor { get; init; } = 0x520808;

    /// <summary>The record families this world holds, beyond the ones the engine ships with.
    ///
    /// <para><b>A record of what the world was authored against, not a declaration.</b> A compiled module
    /// is what makes a family exist; this is what a world folder carries so it can be opened by an editor
    /// that does not have that module — which is the difference between a world you can hand somebody and
    /// one that only opens on the machine that built it.</para>
    ///
    /// <para>Empty for a world holding only Core's families, which is most of them.</para></summary>
    public IReadOnlyList<Extensibility.RecordFamily> Families { get; init; } = [];

    /// <summary>The choice sets <see cref="Families"/> draw on. Empty when none of them do.</summary>
    public IReadOnlyList<Extensibility.ChoiceSet> ChoiceSets { get; init; } = [];

    /// <summary>Where a character may wear something in this world.
    ///
    /// <para>A record of what the world was authored against, like <see cref="Families"/>: a compiled
    /// module is what makes a slot exist, and this is what lets an editor open the folder without having
    /// that module. Empty for a world where nothing is worn.</para></summary>
    public IReadOnlyList<Extensibility.EquipSlot> EquipSlots { get; init; } = [];

    /// <summary>This world's families as a schema, Core's first and then its own — the same shape a
    /// server reports, so a folder opened offline and a server connected to answer alike.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Extensibility.RecordSchema Schema => new()
    {
        Families = [.. Extensibility.CoreRecordFamilies.World, .. Families],
        ChoiceSets = ChoiceSets,
    };
}
