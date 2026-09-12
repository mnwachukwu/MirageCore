using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>One drifting chat-bubble entry. Sits above the head bubble, rising and fading over
/// BubbleFloatMs from the moment it was demoted.</summary>
public readonly record struct ChatBubbleDrifter(string Text, int Color, long DemotedMs);

public sealed class PlayerRecord
{
    // General
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — record names are stored fixed-width,
    /// buffers padded with spaces, so almost every message-formatting site trims them. The
    /// cache invalidates on Name reassignment; the first read after each change recomputes.
    /// Saves ~100 string allocations across the server's hot paths.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();
    public int Sprite { get; set; }
    /// <summary>Which sprite sheet <see cref="Sprite"/> is a row of. Copied from the class at creation
    /// alongside the row, so re-arting a class never restyles a character already made.
    ///
    /// <para>Always written, including when it is 0.</para></summary>
    public int SpriteSheet { get; set; }
    /// <summary>Cumulative seconds this character has been online (across all sessions), persisted. The active
    /// session's not-yet-saved time is added live at readout (see <c>ServerPlayer</c>); the account total is
    /// the sum across the account's characters. Surfaced by <c>/played</c> and <c>/info</c>.</summary>
    public long PlayTimeSeconds { get; set; }
    /// <summary>Admin access — a runtime mirror of the account's <see cref="AccountRecord.Access"/>, set at
    /// login and carried on the wire for name coloring/prefaces. NOT persisted per-character (access is
    /// per-account now); [JsonIgnore] so old per-char "access" fields in save data are simply ignored.</summary>
    [JsonIgnore] public AdminLevel Access { get; set; }
    public long PkExpiryUtc { get; set; }

    /// <summary>PK status is derived purely from the expiry timer — no separate bool flag.</summary>
    public bool IsPk(long nowUtc) => PkExpiryUtc > nowUtc;

    // ── Death & respawn ──────────────────────────────────────────────────────
    // Persisted: a relogin while dead re-opens the death panel.
    /// <summary>True while in the timed dead state (a corpse awaiting a Respawn click).</summary>
    public bool Dead { get; set; }
    /// <summary>UTC-seconds the Respawn button unlocks (server-owned countdown). Meaningful only while
    /// <see cref="Dead"/>.</summary>
    public long RespawnReadyUtc { get; set; }

    /// <summary>Everything a game hangs on this character: its stats, its currencies, its flags, its
    /// standing with whoever cares. Persisted whole, so a game adds a concept without touching the save
    /// format or this record.
    ///
    /// <para><b>Core never reads a key out of it.</b> The one exception is deliberate and lives beside
    /// this: <see cref="MoveSpeed"/> is a real property rather than a bag key, because movement is the
    /// one rule the engine performs itself and it cannot be performed against a name only the game
    /// knows.</para></summary>
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

    // Equipment slots: 1-based inventory index of equipped item; 0 = not equipped
    public int ArmorSlot { get; set; }
    public int WeaponSlot { get; set; }
    public int HelmetSlot { get; set; }
    public int ShieldSlot { get; set; }
    // 1-based spell-slot index of the prepared (Q-cast) spell; 0 = none
    public int PreparedSpell { get; set; }

    // Inventory: 1-based, indices 1..MaxInv; index 0 unused
    public PlayerInvSlot[] Inv { get; set; } = new PlayerInvSlot[Constants.MaxInv + 1];
    // Items escrowed off this character for an IN-FLIGHT direct trade (TradeSystem holds the live session
    // state on ServerPlayer, but the escrowed items must live HERE so they ride the normal character save —
    // otherwise a crash or shutdown mid-trade, after the offer removed them from Inv, would wipe them. Empty
    // during normal play. On login any leftover escrow (a trade the leave-path never got to unwind) is
    // returned to the bag by TradeSystem.RecoverEscrowOnLogin; a live trade never resumes across a restart.
    public List<PlayerInvSlot> TradeOffer { get; set; } = new();
    // The bank is account-shared, not per-character — see AccountRecord.Bank / ServerPlayer.Bank.
    // Spells: 1-based, indices 1..MaxPlayerSpells; index 0 unused; value 0 = empty slot
    public int[] Spell { get; set; } = new int[Constants.MaxPlayerSpells + 1];
    // Action bar: 1-based, indices 1..MaxHotkeys; index 0 unused. Each slot names an item or spell by
    // NUMBER, never by bag/book position — see PlayerHotkey. Load through PlayerHotkey.Normalize so a
    // character saved before the bar existed (or at a different width) comes back the right length.
    public PlayerHotkey[] Hotkeys { get; set; } = PlayerHotkey.NewBar();
    // Player-quest state: InProgress + Done entries only (a never-touched quest has no entry). QuestSystem
    // owns the runtime ObjectiveSystem.Track handles; this is the persisted per-character record it re-tracks
    // from on login. Empty for a questless character.
    public List<PlayerQuest> Quests { get; set; } = new();
    // NPC conversations this character has spoken to (opened at least once) — a per-character visited-set that
    // colors the overhead "..." glyph (yellow = unspoken, gray = spoken). Just conversation numbers, no state.
    public List<int> ConversationsSpoken { get; set; } = new();

    // Position
    public int Map { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    // Two-layer world: which logical layer (ground vs bridge-top fringe) this player occupies. PERSISTED with the
    // character — it's part of the position in a layered world, so a relog restores the player onto the bridge
    // instead of snapping to Ground INSIDE a now-solid ramp tile (a ramp is Blocked on Ground). A true warp/boot
    // still passes destLayer (usually Ground); PlayerWarp re-fits to a walkable layer on arrival. Recomputed on
    // movement. See LayerLogic/WorldLayer.
    public WorldLayer Layer { get; set; }
    // Client-only: the layer this player was on BEFORE the current move-slide started.  While the walk-offset is
    // still animating, a cross-layer step (onto/off a ramp) renders the sprite on the higher layer so it isn't
    // occluded by the ramp/fringe art mid-slide ("sliding out from under the ramp").  Only read while sliding.
    [JsonIgnore] public WorldLayer PrevLayer { get; set; }

    // Spawn point set at an Inn (0 = use server default StartMap/StartX/StartY)
    public int SpawnMap { get; set; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }

    /// <summary>When /home last ran, as unix seconds; 0 when it never has. A wall-clock stamp rather than
    /// elapsed play time, so the cooldown counts down while the character is logged out.</summary>
    public long HomeUsedAtUtc { get; set; }

    // Runtime fields (not persisted — populated by server/client during play)
    [JsonIgnore] public float XOffset { get; set; }
    [JsonIgnore] public float YOffset { get; set; }
    /// <summary>Slide time left over when a tile finished part-way through a frame, carried into the next
    /// one. Dropping it costs up to a frame per tile, which at run speeds that do not divide the frame time
    /// shows as the step cadence alternating between two lengths. Client-side animation only.</summary>
    [JsonIgnore] public float SlideCarryMs { get; set; }
    [JsonIgnore] public MovementType Moving { get; set; }
    [JsonIgnore] public bool Attacking { get; set; }
    [JsonIgnore] public long AttackTimer { get; set; }
    /// <summary>When this player last drank — the client's mirror of the server's potion clock, which
    /// runs apart from <see cref="AttackTimer"/> so drinking and swinging never spend each other.</summary>
    [JsonIgnore] public long PotionTimer { get; set; }
    [JsonIgnore] public long LastCombatMs { get; set; }
    [JsonIgnore] public long PkGraceUntilUtc { get; set; }
    // Aggressor expiry in UTC seconds. Carried on the wire (PlayerData + AggressorRefresh);
    // server-side authority lives on ServerPlayer.PvpAttackerUntil (TickCount64 ms). Client uses
    // this to drive the flashing-red name during the 30 s window.
    [JsonIgnore] public long AggressorUntilUtc { get; set; }

    /// <summary>Observer mode: this character passes through everything, spends no stamina, cannot act on
    /// anyone and cannot be acted on. The one flag both sides read — the server's rules gate on it, and the
    /// client sets it from the wire to render the overhead name grey.
    ///
    /// <para>PERSISTED, so a character left in observer mode is still in it at the next login. Access is
    /// re-checked on join via <see cref="MayUseGodMode"/>, which covers a demotion that happened while the
    /// character was offline.</para></summary>
    public bool GodMode { get; set; }

    /// <summary>Whether this character is allowed observer mode. The toggle refuses below the bar and the
    /// join path drops a persisted <see cref="GodMode"/> that outlived the access which allowed it — one
    /// threshold, so the two can never disagree.</summary>
    [JsonIgnore] public bool MayUseGodMode => Access >= AdminLevel.Developer;

    // Guild display (client-only; wire-fed by SendPlayerData's nullable guild fields, never persisted —
    // guild membership persists per-account on AccountRecord, not on the character). GuildId 0 = guildless.
    [JsonIgnore] public int GuildId { get; set; }
    [JsonIgnore] public GuildRank GuildRank { get; set; }
    [JsonIgnore] public string? GuildName { get; set; }
    [JsonIgnore] public bool GuildOpen { get; set; }
    /// <summary>Overhead guild-name color, packed 0xRRGGBB (0 = unset → a neutral default).</summary>
    [JsonIgnore] public int GuildColor { get; set; }
    /// <summary>Client-only: the member's guild toggles showing the guild's SEASONAL STANDING as "(N)" in the
    /// overhead cluster (the rank word itself now shows unconditionally). Field name predates the
    /// repurpose. Wire-fed by the nullable guild fields on SendPlayerData; never persisted.</summary>
    [JsonIgnore] public bool GuildShowRank { get; set; }
    /// <summary>Client-only: the guild's 1-based seasonal standing (leaderboard position; 0 = unranked), shown
    /// Wire-fed; never persisted.</summary>

    // Chat bubble (client-side render state). Head is anchored above the speaker at full alpha
    // until ChatBubbleEndMs; the tick pass then demotes it to a drifter (rise + fade over BubbleFloatMs).
    [JsonIgnore] public string? ChatBubbleText { get; set; }
    [JsonIgnore] public long ChatBubbleEndMs { get; set; }
    [JsonIgnore] public int ChatBubbleColor { get; set; }
    // Drifters are lazy-allocated on first demote so silent players pay zero allocation.
    [JsonIgnore] public List<ChatBubbleDrifter>? ChatBubbleDrifters { get; set; }

    public PlayerRecord()
    {
        for (int i = 1; i <= Constants.MaxInv; i++)
            Inv[i] = new PlayerInvSlot();
    }

    /// <summary>
    /// Deep copy used to snapshot a still-live player for a background save: the server game thread
    /// keeps mutating the original while the write happens off-thread, so the array fields
    /// (<see cref="Inv"/>, <see cref="Spell"/>) must be cloned, not shared.  (Leave/ghost saves don't
    /// need this — there the record is already detached from its slot before the save fires.)  The
    /// account-shared bank is snapshotted separately (ServerPlayer.CloneBank).
    /// </summary>
    public PlayerRecord Clone()
    {
        var c = (PlayerRecord)MemberwiseClone();   // all scalars; array/list fields still shared after this
        c.Attributes = Attributes.Clone();
        c.Spell = (int[])Spell.Clone();
        c.Inv = new PlayerInvSlot[Inv.Length];
        for (int i = 0; i < Inv.Length; i++)
        {
            var s = Inv[i];
            c.Inv[i] = s is null ? null! : new PlayerInvSlot { Num = s.Num, Quantity = s.Quantity, Dur = s.Dur };
        }
        // Deep-copy the trade escrow too — the game thread keeps mutating it mid-trade while this snapshot
        // is written off-thread (same reason Inv is cloned, not shared).
        c.TradeOffer = new List<PlayerInvSlot>(TradeOffer.Count);
        foreach (var s in TradeOffer)
            c.TradeOffer.Add(new PlayerInvSlot { Num = s.Num, Quantity = s.Quantity, Dur = s.Dur });
        // Deep-copy quest state (QuestSystem mutates Progress live as kills land while this snapshot writes).
        c.Quests = new List<PlayerQuest>(Quests.Count);
        foreach (var q in Quests) c.Quests.Add(q.Clone());
        c.ConversationsSpoken = new List<int>(ConversationsSpoken);
        return c;
    }
}
