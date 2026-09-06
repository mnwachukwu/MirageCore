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

    public string AttackSay { get; set; } = string.Empty;
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

    public int Str { get; set; }
    public int Def { get; set; }
    public int Spd { get; set; }
    public int Int { get; set; }
    /// <summary>Flat bonus max-HP added 1:1 on top of the stat-derived HP pool (0 = none, the default).  A
    /// designer lever for authoring bosses / walls or buffing significant NPCs beyond what their combat stats
    /// imply: the HP formula derives HP from total stat investment, so this is how you make something
    /// exceptionally tanky without inflating its other stats.  Grants PREMIUM kill-EXP — more EXP per point than
    /// natural stat HP earns — because grinding through boss HP is an epic slog (epic HP → epic EXP).  It's also
    /// the intended way to restore an old-style extreme-DEF wall under the unified vital formula.</summary>
    public int ExtraHp { get; set; }
    /// <summary>Author flag marking this NPC as a BOSS — a deliberate designer classification, NOT inferred from
    /// HP/Size/stats (a tanky or large mob is not automatically a boss, and <see cref="ExtraHp"/> is a separate
    /// tankiness lever). Its only effect today: a guild quest that rolls a boss uses a COMPRESSED kill-count
    /// curve (tens, not hundreds — see <see cref="GuildQuests.KillCount"/>) and a reduced reward, so a boss
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
        // no key at all, matching how ClassGate collapses an empty AllowedClasses.
        if (Drops.Count == 0) Drops = null;
        // No length cap: a hoard is authored as repeated lines (quantity does not stack off Currency), so
        // truncating here would silently delete payout.
    }
}
