using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class NpcRecord
{
    private string _name = string.Empty;
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — record names are stored fixed-width and
    /// every NPC message string TrimEnds them.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    /// <summary>This body's one line, or blank for one that says nothing.
    ///
    /// <para>Said on two occasions, which is why it is named for neither: the first time it notices a
    /// player, and again when a player reaches for a body that has no conversation and no shop to
    /// offer. Once per player rather than once per encounter.</para>
    ///
    /// <para>A greeting, a warning and a threat are the same field. Which one it reads as is a property
    /// of the game rather than of the engine.</para></summary>
    public string Says { get; set; } = string.Empty;
    public int Sprite { get; set; }
    /// <summary>Which sprite sheet <see cref="Sprite"/> is a row of.
    ///
    /// <para>Always written, including when it is 0: a record states which sheet it draws from rather than
    /// leaving a reader to infer it. Absent from an older file it still reads as 0, which is the sheet
    /// every such file meant.</para></summary>
    public int SpriteSheet { get; set; }
    /// <summary>Sprite/footprint size class: 1 = 32x32 (one tile, the default), 2 = 64x64 (a 2x2 tile
    /// footprint), 3 = 96x96 (a 3x3 footprint).  A larger NPC occupies its whole SxS block, anchored at
    /// its top-left tile, and obeys the same blocking/attribute rules as a one-tile NPC.  A 0 in a legacy
    /// or blank record is treated as 1 (see <see cref="EffectiveSize"/>; normalized once at load).</summary>
    public int Size { get; set; }
    /// <summary><see cref="Size"/> clamped to a valid footprint class [1, <see cref="Constants.MaxNpcSize"/>].
    /// Read this at runtime so a 0 ("not defined") legacy value behaves as the 1x1 default.</summary>
    [JsonIgnore]
    public int EffectiveSize => Math.Clamp(Size, 1, Constants.MaxNpcSize);
    public int SpawnSecs { get; set; }
    public NpcBehavior Behavior { get; set; }
    /// <summary>AoS alliance tag: an Attack-on-Sight NPC won't attack another NPC sharing its
    /// non-zero Group (additive with the same-type peace).  0 = ungrouped (original behavior).</summary>
    public int Group { get; set; }
    /// <summary>How far it notices anything, in tiles. Free: <see cref="Constants.NpcRangeSoftCap"/> is
    /// what the editor expects it to stay within, not a limit anything enforces.</summary>
    public int Range { get; set; }
    /// <summary>What this NPC can drop. Null or empty = drops nothing, which is a perfectly ordinary state
    /// for trash. Every entry rolls INDEPENDENTLY on a kill, so a death can yield nothing, one thing, or
    /// several — see <see cref="NpcDrop"/> for why that beats a weighted single pick.</summary>
    public List<NpcDrop>? Drops { get; set; }

    /// <summary>Everything a game hangs on this NPC TEMPLATE — what every copy of it starts with.
    /// Authored, so it is what the editor edits and what a world file carries.
    ///
    /// <para>A running copy's own values live on <see cref="MapNpcRecord.Attributes"/> instead: one
    /// wolf taking damage must not wound the species.</para></summary>
    public AttributeBag Attributes { get; set; } = new();

    /// <summary>How fast this body moves, as a pure additive bonus over the speed everything starts
    /// with. 0 is the baseline, which is what a world that never sets it gets.
    ///
    /// <para><b>Core's only speed number, and it is not a stat.</b> Movement is the one thing the engine
    /// itself performs on every body, so the pace has to live somewhere Core can read without knowing
    /// what a game calls its attributes. A game that derives speed from agility, a mount, a road, or a
    /// status effect writes the result here; Core never asks where the number came from.</para>
    ///
    /// <para>It is also what the server bills a move against, so it is authoritative rather than
    /// cosmetic: a client claiming a faster pace than this allows is refused a step.</para></summary>
    public int MoveSpeed { get; set; }

    /// <summary>Author flag marking this NPC as a BOSS — a deliberate designer classification, NOT inferred from
    /// size and attributes (a large body is not automatically a boss, and what makes one is a separate
    /// tankiness lever). Its only effect today: a guild quest that rolls a boss uses a COMPRESSED kill-count
    /// curve (tens, not hundreds) and a reduced reward, so a boss
    /// target can never become an impossible "kill hundreds of bosses" quest. Otherwise a boss is an ordinary
    /// NPC in every system (spawn, combat, war despawn). Defaults false.</summary>
    public bool IsBoss { get; set; }
    /// <summary>When true, this NPC acts as a light source at night (like players). When false it
    /// receives no light halo. Defaults false so existing NPCs stay dark unless opted in.</summary>
    public bool EmitsLight { get; set; }
    /// <summary>Light attributes used when <see cref="EmitsLight"/> is true (ignored otherwise). Defaults to
    /// the classic torch so existing emit-light NPCs render exactly as before.</summary>
    public LightSpec Light { get; set; } = LightSpec.Torch;

    /// <summary>Canonicalize the drop table, so what reaches disk says what it means.
    ///
    /// <para>Idempotent, which matters because it runs on load AND on every editor save: re-running it on
    /// an already-canonical record changes nothing.</para></summary>
    public void Normalize()
    {
        if (Drops is null) return;
        // Drop inert lines (no item, or a chance that can never land) rather than carrying them on disk —
        // an editor may hold a half-authored row in memory, but a saved file should say what it means.
        Drops.RemoveAll(d => !d.IsLive);
        // An empty list and "no table" are the same thing; collapse so an NPC that drops nothing carries
        // no key at all.
        if (Drops.Count == 0) Drops = null;
        // No length cap: a hoard is authored as repeated lines (quantity does not stack off Currency), so
        // truncating here would silently delete payout.
    }
}
