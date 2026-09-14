using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>A group of maps sharing a <see cref="Name"/> + map-like fallback properties. Index-keyed on disk
/// (<c>map_groups/map_group{Index}.json</c>), with NON-unique names (like maps). A map references its group
/// via <see cref="MapRecord.MapGroup"/>; a map's OWN property always wins and the group fills in only what the
/// map leaves unset (see <see cref="MapGroupResolve"/>).
///
/// <para>Everything here is AUTHORED, and a world folder holds nothing else. Who controls a territory, what it
/// has earned and who is challenging for it belong to one running server rather than to the world, and live
/// apart.</para></summary>
public sealed class MapGroupRecord
{
    /// <summary>Filename stem inside <c>map_groups/</c>; the trailing number is the <see cref="Index"/>.</summary>
    public const string FileStem = "map_group";

    /// <summary>Group id (>= 1). In memory only — the guild, territory and combat code holds a group
    /// detached from any dictionary key and asks it which one it is.
    ///
    /// <para>NOT serialized. The number lives in the filename, <c>map_groups/map_group{Index}.json</c>,
    /// and the loaders fill this in from it; every other record keys off its filename the same way and
    /// stores no id of its own. Writing it as well would let a copied file claim a number that is not its
    /// own, and a group believed to be one it is not scores territory for the wrong guild.</para></summary>
    [JsonIgnore]
    public int Index { get; set; }
    /// <summary>Group name — an identifier (like a Map Name); NON-unique. It does not override map names; it
    /// slots into the display chain between a map's DisplayName and its raw Name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Player-facing group display name, inserted into the map name chain (Map DisplayName → this →
    /// Map Name → "Map N"). Blank falls through to <see cref="Name"/>.</summary>
    public string DisplayName { get; set; } = "";

    // ── Map-like fallbacks (used only where the map leaves its own unset; the map always wins) ──────────
    // The int fields use 0 as the "the group doesn't provide this" sentinel. The bools are NULLABLE so
    // a group can decline to provide one (null) — false is a real value, not a spare sentinel;
    // null on both map + group resolves to the hard default (None / false).
    public int Music { get; set; }
    public bool? Indoors { get; set; }
    public bool? PlayersPassThrough { get; set; }
    // Mutually exclusive with each other, and resolved as a pair — see MapGroupResolve.Lighting.
    public bool? AlwaysLit { get; set; }
    public bool? AlwaysDark { get; set; }
    public int ExitMap { get; set; }
    public int ExitX { get; set; }
    public int ExitY { get; set; }

    // Map-enter/leave greeting fallback: a map inherits any greeting field it leaves blank
    // from its group, so a multi-map building can define one greeting once at the group level.
    public string GreetingSpeaker { get; set; } = string.Empty;
    public string JoinSay { get; set; } = string.Empty;
    public string LeaveSay { get; set; } = string.Empty;

    /// <summary>Everything a game hangs on this group that the engine has no name for — who holds it, how
    /// long they have held it, what it pays.
    ///
    /// <para>A group is the engine's own idea of a region: several maps that share a name and some
    /// settings. What a region MEANS to a game — a territory, a province, a hunting ground — is a game's,
    /// and this is where it says so.</para></summary>
    public Extensibility.AttributeBag Attributes { get; set; } = new();

    /// <summary>Copy for an off-thread save snapshot. Every other field is a value type or an immutable
    /// string; the bag is copied so a snapshot cannot be written through.</summary>
    public MapGroupRecord Clone()
    {
        var copy = (MapGroupRecord)MemberwiseClone();
        copy.Attributes = Attributes.Clone();
        return copy;
    }
}
