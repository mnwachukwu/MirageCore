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
    /// <para>It never reaches a player. A player sees the GAME's name (<see cref="Constants.GameName"/>),
    /// which is a different thing: one identifies a set of records an operator is working on, the other
    /// identifies the game they are all part of. An operator running a live world and a test copy of it
    /// tells them apart by this, and by nothing else.</para>
    ///
    /// <para>Blank is the stored form of "unnamed", and stays blank: what to CALL an unnamed world is a
    /// question for whoever is showing it, and each app answers it in its own language.</para></summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when this world has a name of its own rather than running on the stock answer.</summary>
    [JsonIgnore]
    public bool IsNamed => !string.IsNullOrWhiteSpace(Name);

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
}
