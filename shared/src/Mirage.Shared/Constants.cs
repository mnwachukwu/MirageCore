namespace Mirage.Shared;

/// <summary>
/// Game-wide tuning values and hard limits shared by server, client, and editor: collection caps,
/// combat and AI cadences, economy costs, weather and time-of-day timings, and the ground-stain model.
/// Everything here is a compile-time constant except the assembly-derived version fields.
/// </summary>
public static class Constants
{
    /// <summary>What this engine calls itself.
    ///
    /// <para><b>Must match <c>GameName</c> in <c>Directory.Build.props</c>, and
    /// <c>GameNameIdentityTests</c> is what holds the two together.</b> MSBuild names every executable
    /// from its copy; this copy names the per-user settings folders and is what the server shell
    /// composes its server's filename from. The two disagreeing is not a build error and not a test
    /// failure on its own — it is a shell that cannot find the server sitting beside it.</para></summary>
    public const string GameName = "Mirage Source Remastered Core";
    public const int GamePort = 4000;

    // Version sourced from the running exe's assembly metadata (set in Directory.Build.props).
    // Falls back to Mirage.Shared.dll version, then 0.99.99, when called outside an exe (e.g. tests).
    private static readonly Version _appVer =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 99, 99);

    public static readonly int ClientMajor = _appVer.Major;
    public static readonly int ClientMinor = _appVer.Minor;
    public static readonly int ClientRevision = _appVer.Build;

    /// <summary>The PROTOCOL ceiling on concurrent players — the largest slot number that can ever appear
    /// on the wire, and the size a client allocates its player table for.
    ///
    /// <para>This is NOT a server's limit. That is <c>ServerConfig.MaxPlayers</c>, defaults to 20, and is
    /// what sizes the server's own arrays — so a small world never pays for this number. An operator
    /// raises it against a measured figure from the window's load benchmark rather than a guess.</para>
    ///
    /// <para>It lives here as a <c>const</c> because it is inlined into every shipped client, so a server
    /// configured ABOVE it would hand out slot numbers the client cannot index. Config is validated
    /// against it for exactly that reason. Raising it is a client-side cost — see the loops in
    /// MovementProcessor and RenderCommandBuilder, which walk the ACTIVE set rather than this number.</para></summary>
    public const int MaxPlayers = 500;

    // ── Record-family ceilings live in RecordLimits, NOT here ─────────────────
    // Items, NPCs, shops, spells, quests, conversations, maps and map groups are PER-SERVER settings that
    // travel in the pre-login hello. They were consts, and that was a bug rather than a simplification:
    // a const is inlined into every shipped client, so a client built against 1000 items rejected item
    // 1200 as out of range on a server that had authored it. `RecordLimits.Default` holds the stock
    // values; a server states its own, and both ends size their tables from what was stated.
    //
    // The per-quest and per-conversation caps below are NOT that. They bound the shape of one record —
    // how many objectives a quest carries, how many nodes a dialogue tree has — which is a content and
    // UI concern rather than a catalog size, and they are the same everywhere.
    public const int MaxQuestObjectives = 255;   // safety ceiling on objectives per quest (editor authors as many as needed; bounds the per-character progress list)
    public const int MaxActiveQuests = 10;   // how many quests a character can have IN PROGRESS at once
    public const int MaxConversationNodes = 64;    // dialogue nodes per conversation (editor add-row cap)
    public const int MaxConversationChoices = 8;   // player choices per node (menu size; panel-render sane)
    // NO CAP ON DROP-TABLE LENGTH — deliberately. There was one (8), justified as a backstop against a
    // table "burying a tile in loot", and it guarded a hazard that does not exist: MaxMapItems bounds only
    // PLAYER-dropped clutter, NPC loot is exempt from it by design, and MapItems is an unbounded list. What
    // the cap actually did was silently truncate authored content — seven boss tables sat exactly on it.
    //
    // Repeated lines are now the ONLY way to author a multi-item payout, because quantity does not stack
    // off a Currency item (see NpcDrop), so a length limit is a limit on payouts. Restraint belongs to the
    // author, and the editor shows expected-drops-per-kill so the sum of independent chances stays visible.
    public const int MaxInv = 50;
    public const int MaxBankSlots = 100;
    public const int MaxMapItems = 20;

    // Player-to-player mail: attachments per message + subject/body length caps (enforced server-side;
    // the compose client mirrors them).
    public const int MaxMailAttachments = 4;
    public const int MailSubjectMaxLength = 100;
    public const int MailBodyMaxLength = 5000;   // plenty for a message; bounds abuse
    // A long body escalates the send cost across the board (single + multi): over the first threshold the cost
    // is x2, over the second it's x10 (tiered — the higher tier wins, not cumulative).
    public const int MailLongBodyThreshold = 2000;
    public const int MailLongBodyCostMultiplier = 2;
    public const int MailVeryLongBodyThreshold = 4000;
    public const int MailVeryLongBodyCostMultiplier = 10;
    // The To field takes no character limit, but a single send fans out to at most this many recipients —
    // enforced client-side AND as an inexpensive server backstop (legit or 10 blank tokens, either way capped).
    public const int MaxMailRecipients = 10;
    // Player-origin mail (P2P + marketplace) rides "in transit" for a random delay in this range before it
    // matures (becomes claimable); system/notification mail is instant. Seconds.
    public const int MailP2PDeliveryMinSeconds = 10 * 60;
    public const int MailP2PDeliveryMaxSeconds = 15 * 60;
    // Mail auto-deletes this long after it matures (measured from DeliverAt so in-transit time isn't counted).
    public const int MailRetentionSeconds = 30 * 24 * 60 * 60;   // 30 days
    // Collect-on-Delivery (CoD) mail: an unpaid CoD RETURNS to its sender this long after it matures (measured
    // from DeliverAt like the normal retention) - far shorter than the 30-day normal retention, so locked items
    // don't sit forever. The sender may set a CoD price up to MarketMaxPrice (the marketplace price ceiling).
    public const int CodLifetimeSeconds = 3 * 24 * 60 * 60;   // 3 days
    // Cost to send mail (a gold sink): a base fee, a per-attachment surcharge, and a percent of the
    // parcel's gold value (EconomyFormulas.MailSendCost). A multi-recipient send (attachments
    // disallowed) costs the base fee per recipient. Client previews the total; server charges it.
    //
    // FLAT ON PURPOSE. A fee scaled to the sender's level is unenforceable: hand the parcel to a level-1
    // mule and it drops to the floor. Anything payable on someone else's behalf cannot be priced by who
    // pays it. The flat parts stay SMALL because mail is available from level 1 and any flat fee big
    // enough to matter at 255 would be unaffordable at 5; scale comes from the percent below.
    public const int MailBaseSendCost = 10;
    public const int MailAttachmentSendCost = 50;
    // Keyed on the SHIPMENT rather than the sender, so the mule that defeats a level-scaled fee is
    // irrelevant. Deliberately under the 5% MarketSaleTaxPercent the marketplace and CoD charge: those
    // buy escrow, plain mail does not, and the gap is the price of trust.
    public const int MailAttachedValuePercent = 2;

    // Player marketplace: sale tax (a gold sink, shown to the seller up front), per-seller listing cap, and
    // the maximum gold price a single listing can be set to.
    public const int MarketSaleTaxPercent = 5;
    public const int MaxMarketListingsPerSeller = 10;
    public const int MarketMaxPrice = 1_000_000_000;
    // A listing lives 30 days, then the sweep returns it to the seller. Completed sales are logged to a
    // rolling on-disk history (also the seller's in-panel Sales tab), bounded to the most recent N.
    public const int MarketListingLifetimeSeconds = 30 * 24 * 60 * 60;
    public const int MaxMarketSalesLog = 1000;
    // Direct player-to-player trade: max items each side can stage in an offer, and the pending-invite timeout.
    // Proximity uses the shared spell-range radius (r=5) via WorldCoordHelper.IsInInteractRange.
    public const int MaxTradeOfferItems = 8;
    public const long TradeInviteTimeoutMs = 30_000;
    public const int MaxMapNpcs = 20;   // per-map NPC spawn slots, 1-based (1..MaxMapNpcs)
    public const int MaxChars = 3;
    public const int NameLength = 30;
    public const int MinFieldLength = 3;

    // ── Three sizes that happen to be equal, and must not be confused ────────
    // A map's size, the camera's window, and how far gameplay reaches are three separate things. They
    // are all 16x12 today, which is exactly why each is written out on its own here: a map that is not
    // 16x12 must move NEITHER of the other two. Derive nothing across these groups.

    /// <summary>The size a map is when nothing says otherwise — a NEW map's dimensions, and the padding
    /// a blank map slot is given. A map's ACTUAL size is <c>MapRecord.Width</c> / <c>Height</c>, read off
    /// its own tile grid; never assume a map is this size.</summary>
    public const int DefaultMapWidth = 16;

    /// <inheritdoc cref="DefaultMapWidth"/>
    public const int DefaultMapHeight = 12;

    // The default map size as a 0-based maximum index.
    public const int MaxMapX = DefaultMapWidth - 1;
    public const int MaxMapY = DefaultMapHeight - 1;

    /// <summary>The camera's window in tiles — a property of the RENDER TARGET (512x384 at
    /// <see cref="PicX"/>), not of any map. It is what the client draws and what gameplay reach is
    /// measured against, and it does not move when a map's size does.</summary>
    public const int ViewportTilesX = 16;

    /// <inheritdoc cref="ViewportTilesX"/>
    public const int ViewportTilesY = 12;

    /// <summary>Spell-cast radius in tiles: a symmetric circle around the caster. The largest circle that
    /// fits the viewport, limited by its short half-extent in Y — larger would reach past what is drawn.
    /// Pinned to the VIEWPORT, so a large map never grants extra reach.</summary>
    public const int InteractRangeTiles = (ViewportTilesY / 2) - 1;   // 5

    public const int PicX = 32; // Size, in pixels
    public const int PicY = 32; // Size, in pixels

    // NPC sprite/footprint size classes: 1 = 32x32 (one tile, the default), 2 = 64x64 (a 2x2 tile
    // footprint), 3 = 96x96 (a 3x3 footprint). A larger NPC occupies its whole SxS block of tiles and
    // is bound by the same blocking/attribute rules. Caps NpcRecord.Size (see NpcRecord.EffectiveSize).
    public const int MaxNpcSize = 3;

    // Client lighting: how far (in tiles) a safe-zone map light's soft edge spills past the map boundary.
    // Shared so the visual spill (MirageGame.MapAreaBleed = PicX × this) and the emitter-suppression
    // reach (RenderCommandBuilder) stay in lockstep.
    public const int MapAreaBleedTiles = 2;

    // ── Tile layers & tilesets ───────────────────────────────────────────────
    // Each tile has two layer types — Ground (drawn below entities) and Fringe (above) — and each
    // type is a stack of this many layers.  Arrays are 0-based; the editor UI labels them 1..5.
    // Ground layer 0 is the "bottom"/floor layer (the one a door reveals when opened).
    public const int MaxGroundLayers = 5;
    public const int MaxFringeLayers = 5;
    // Canopy: a third visual stack drawn ON TOP of everything (both logical layers), so a bridge tile
    // can carry overhead art (a roof / foliage) above a fringe-layer walker even though its Fringe[]
    // is consumed as the walkable surface. Paint only: no gameplay attribute, no logical layer.
    public const int MaxCanopyLayers = 5;
    // A layer records which tileset ("sheet") its tile came from as a 0-based index packed into a
    // byte (see LayerCell), so at most this many distinct sheets may exist. The same ceiling bounds
    // every asset class, so a sheet number spans the same range whichever folder it names.
    public const int MaxTilesets = 256;
    // Build-output subfolders under assets/graphics/ holding the graphics for each asset class.
    // Every class is multi-sheet: files are numbered 0_*.bmp, 1_*.png, ... and the leading number is
    // the stable sheet index, scanned at launch and on the editor's Reload Assets action. Sprites are
    // split again by footprint size (32x32/64x64/96x96), where one number is one character at every size.
    public const string TilesAssetSubfolder = "tiles";
    public const string SpritesAssetSubfolder = "sprites";
    public const string ItemsAssetSubfolder = "items";

    public const int WalkSpeed = 4;

    public const int MaxEditorSessions = 5;

    public const int DefaultItemRespawnSeconds = 120;

    /// <summary>The highest tier the pricing curve is defined over.
    ///
    /// <para>NOT a character ceiling — Core has no levels. It is where <c>EconomyFormulas</c>' gold curve
    /// stops being extrapolated, so a rung at the top prices flat instead of running off the fit.</para></summary>
    public const int MaxItemTier = 255;

    // Tiers one gear rung covers. Equipment is authored a rung at a time, so a piece bought on tier is worn
    // across this many before the next is reachable, and EconomyFormulas prices it against the gold earned
    // over exactly that span — buy once, wear it the whole rung.
    public const int GearTierSpan = 5;

    // Action-bar slots, bound to keys 1..4 (and to the gamepad's four face buttons under a trigger
    // modifier).  Four is a UI limit as much as a design one: the bar sits in the sidebar strip above the
    // links, and four icons is what fits there at the strip's width without crowding them.
    public const int MaxHotkeys = 4;
    // What an NPC's sight radius is expected to stay within, in tiles. ADVISORY: Range is a free number,
    // and an author who wants a mob that notices the whole map may have one.
    //
    // Six is the viewport's short half-extent — how far a player can see up or down. Past it, a mob
    // acquires from somewhere its target cannot see, which is worth being deliberate about rather than
    // arriving at by accident, so the editor says so.
    public const int NpcRangeSoftCap = 6;

    // Below this, an NPC that acquires unprovoked notices nothing until somebody is already beside it,
    // which is almost always a slip rather than a design. Also advisory, and also called out.
    public const int MinAggressiveNpcRange = 2;

    // The spawn point is a server SETTING, not a constant — see ServerConfig.Spawn. It defaults to the
    // middle of map 1, which is what it was when it lived here.

    // ── Action timing ────────────────────────────────────────────────────────
    // TWO cooldowns, not one. Both ACTION values share a 1-second beat — a player's and an NPC's —
    // because they are the same act of committing to a turn, and the client's InputProcessor paces to
    // the same value so server and client agree on it.
    public const long PlayerAttackCooldownMs = 1000;
    public const long NpcAttackCooldownMs = 1000;

    // Using something up runs on its OWN clock, and a slower one. Sharing the action beat would make a
    // consumable cost an action, which turns any restorative into a straight substitute for acting; on a
    // separate track it stays useful without replacing what a player would otherwise have done.
    public const long ConsumableCooldownMs = 2000;
    // There is deliberately NO post-cast MOVE lockout: casting does not restrict movement at all, for
    // players or NPCs. At equal run speed a caster can't open a gap anyway, so a lockout would only
    // forbid walking during a second in which no recast was possible. The 1-second cast cadence above
    // is what paces spell damage.

    // ── Loot rolling ─────────────────────────────────────────────────────────
    // Players whose damage credit reaches this fraction of the top-damage contributor are eligible to roll
    // for tagged loot on NPC death, and to share the currency. Read it as "within a quarter of the top
    // dealer": someone who did the work alongside the leader shares the kill, someone who chipped does not.
    public const double LootDamageContributionThreshold = 0.75;

    // ── RNG bounds ───────────────────────────────────────────────────────────
    public const int PercentRollSides = 100;  // Random.Shared.Next(100) for % rolls (durability, drops)

    // Single dial for the granularity of block/dodge/crit chances.
    // 1  = integer percent (caps read as 35% / 25% / 15% / 10%, displayed as "35%").
    // 10 = tenths-of-a-percent per-mille (the same caps reread as 3.5% / 2.5% / 1.5% / 1.0%).
    // The chance formulas and caps in CombatFormulas don't change with this dial — only the roll
    // denominator, display divisor, and decimal precision (CombatFormulas.ChanceDisplayDecimals,
    // derived as ceil(log10(scale))) scale with it. Drops/durability use the fixed PercentRollSides
    // above and are NOT affected.
    public const int ChanceScaleFactor = 1;
    public const int ChancePercentRollSides = 100 * ChanceScaleFactor;
    public const int NumDirections = 4;       // Up/Down/Left/Right enum cardinality

    // ── NPC AI cadence ───────────────────────────────────────────────────────
    // The AI decision tick.  It drives several other cadence-sensitive systems, so it is NOT tied to the
    // player walk speed.  A continuously-moving NPC (active chase, or mid-wander-stride) is issued one
    // walk step per tick, and the client's NPC walk-slide (MovementFormulas.NpcWalkMsPerTile) is bound to
    // this value so NPC walking is GAPLESS (the slide lands exactly as the next step arrives).  The trade:
    // an NPC walks one tile per 500 ms (2 t/s) — a hair slower than a walking player (400 ms, 2.5 t/s) —
    // so players can always step away from a walking NPC.  GameLoop.AiIntervalMs derives from this.
    public const int AiTickIntervalMs = 500;

    // Tile-animation cadence: one animation frame per this many ms, shared by the in-game client
    // renderer and the editor's anim preview so both advance identically. Each frame dwells this long,
    // so an N-frame animated tile loops over N*this (cycle) or (2N-2)*this (pendulum) ms.
    public const int MapAnimIntervalMs = 250;

    // ── NPC wander (committed-stride model) ──────────────────────────────────
    // Idle NPCs amble in STRIDES rather than isolated random steps: on a 1-in-N roll, commit to a heading
    // and a length, then walk it one tile per AI tick.  Mid-stride each step has a small chance to bend a
    // right angle (never a reversal), so paths form Ls and gentle zigzags instead of dead-straight lines.
    // Confined to the map by CanNpcMove's bounds check — an NPC never wanders across a border (only the
    // chase code turns a native into a traversal guest).  Free-roam: NPC spawn tiles are engine-randomized,
    // so there is no authored home to leash toward.
    public const int NpcWanderStrideMinTiles = 2;       // shortest committed stride (tiles)
    public const int NpcWanderStrideMaxTiles = 5;       // longest committed stride (tiles, inclusive)
    public const int NpcWanderStartChancePerTick = 8;   // 1-in-N per idle tick to BEGIN a stride
    public const int NpcWanderTurnChancePerStep = 4;    // 1-in-N per mid-stride step to bend 90° (Ls / zigzags)

    // ── NPC run-chase ────────────────────────────────────────────────────────
    // On the tick an AoS NPC first acquires a combat target it rolls this percent chance to COMMIT to running
    // down the opening gap even from CLOSE range.  Without the roll it strolls in only while the target is within
    // the stroll ceiling (NpcApproachWalkMaxGap) — the roll is the "even a short approach, one in five charges"
    // chance so a close stroll-in isn't fully predictable.  A target spotted FARTHER than the stroll ceiling is
    // rushed regardless of this roll.  Non-AoS chasers (guards, retaliating mobs) are ungated — always run to close.
    public const int NpcApproachRushChancePct = 20;

    // The AoS opening-approach STROLL ceiling (world-Manhattan tiles).  An AoS mob stalking a target within this
    // many tiles strolls in at a WALK — conserving SP for a menacing close-range approach — UNLESS it won the
    // NpcApproachRushChancePct charge roll.  A target spotted FARTHER, or a stalked target that OPENS the gap
    // past this ceiling, is RUSHED down (the ChaseSprinting latch then holds the charge to melee, where the
    // re-close hysteresis takes over).  So a mob strolls only the last few tiles of a close approach; anything
    // farther it runs.  This is deliberately the stroll-vs-charge boundary for BOTH the spot distance AND the
    // gap-reopened charge, since a mob can't stroll at a gap it would also charge at.  At 3 it strolls within 3
    // tiles and charges once the target opens the gap to 4.  Lower = rushes from closer in; raise = roomier stroll.
    public const int NpcApproachWalkMaxGap = 3;

    // Chase limit-cycle damping: a chasing NPC that goes this many AI ticks without ever reducing
    // its world-distance to the target is treated as oscillating ("dancing") and damped — it holds
    // position instead of reversing its previous step, which collapses the cycle.  Only mutual/coupled
    // pursuit (e.g. a guard pinned between an NPC and its quarry) trips this; a normal chase keeps
    // closing distance and never stalls.  3 ticks ≈ 1.5 s at the 500 ms AI cadence.
    public const int NpcChaseStallTicks = 3;

    // World-Manhattan gap (tiles) at which an engaged, already-reached melee mob (NOT a guard, NOT a spell-
    // primary caster) breaks from a WALK into a re-close RUN; it then sprints until adjacent, where it drops
    // back to a walk (see NpcAiSystem.NpcWantsChaseRun + MapNpcRecord.ChaseSprinting).  Bursts stamina instead
    // of gluing.  At 3 there's a one-tile WALK band: the mob follows at a walk while a target sits 2 tiles off
    // (enough breathing room to slip PAST it and maneuver) and only sprints once the gap reaches 3 — so a WALKING
    // player can never shake it (it keeps pace at a walk, or sprints the instant the gap opens), but a running
    // player can open distance.  Lower toward 2 = stickier re-closing (no walk band); raise toward 6-7 = more
    // breathing room.  Guards stay always-run (a deterrent).
    public const int NpcChaseSprintGapTiles = 3;

    // ── Item index reservations ──────────────────────────────────────────────
    // Item slot 1 is the gold (Currency) item. Every system that charges or
    // rewards gold references this constant; do not hardcode 1 at call sites.
    public const int GoldItemIndex = 1;

    // Earned from war kills + guild quests, spent at the war shop, donated to the guild vault (tax relief),
    // or banked. Per-character; the code references this index to grant/spend it, exactly like gold.
    public const int ValorItemIndex = 3;

    // ── Inn: set-spawn cost ──────────────────────────────────────────────────
    // The cost itself is EconomyFormulas.InnSpawnCost, a share of one level's earnings. This is only the
    // floor, for the low levels where that share is still single digits.
    public const int SpawnCostMinimum = 5;

    // ── Guild ────────────────────────────────────────────────────────────────
    // EVERY GOLD FIGURE IN THE GUILD FAMILY IS ONE SET — these, and the ones in guild quests, wars and
    // territory. The ratios between them are deliberate, so they move together by a single factor or not
    // at all; retuning one on its own silently changes a relationship somebody chose.
    //
    // The anchor is 35,000 to found a guild: one level's income at level 30, around 30 hours of at-level
    // play by simulation. NOT a level gate — nothing requires a level to found a guild. It is the level
    // the number was SIZED against, so that a flat cost has a defensible player behind it.
    //
    // FLAT, all of it: a guild is funded collectively from a vault, so pricing anything here by whichever
    // member clicks the button is both arbitrary and trivially minimized by using the lowest-level one.
    // See EconomyFormulas for why that rules out scaling these by the actor's level.

    // How long /home waits between uses, per character. Measured in WALL-CLOCK time from a stamp on the
    // character, so it runs down while the player is logged out and a relog neither clears nor pauses it.
    public const int HomeCooldownSeconds = 30 * 60;

    // Gold to found a new guild. Consumed on success (a creation sink; the new guild's vault starts
    // empty). Charged via GoldItemIndex, client-blocked then server-revalidated.
    public const int GuildCreationCost = 35_000;
    // Max descriptive labels (GuildLabel) a leader may apply to a guild.
    public const int MaxGuildLabels = 3;
    // Max length of a guild's message-of-the-day.
    public const int GuildMotdMaxLength = 200;
    // How long a pending guild invite / join-request stays open before it lapses.
    public const int GuildInviteTimeoutSeconds = 60;
    // Cap on pending open-membership applications a guild holds at once (anti-spam; excess is refused).
    public const int MaxGuildApplications = 50;
    // A guild overhead color is free 24-bit RGB, but may not land within this squared-Euclidean RGB
    // distance of any of the 16 named GameColor palette entries (which carry semantic meaning). Small
    // by design — it only pushes guild colors visibly off the reserved hues, not out of the gamut.
    // 32*32: a shade must differ from every reserved color by ~32 in RGB space.
    public const int GuildColorReservedDistanceSq = 32 * 32;

    // Recent vault-log entries kept for the Vault tab's Donations + Spending views (newest-first, capped). Display-only.
    public const int GuildRecentVaultLogMax = 15;

    // How far back a guild member's rolling "recently active" total reaches. The total resets after an
    // offline gap longer than this (see GuildMember.ActiveSeconds).
    public const long GuildActiveMemberWindowSeconds = 3 * 24 * 3600;

    /// <summary>The share of a mob's damage a player deals for the kill to count toward their quest
    /// objectives — player and guild alike, and the valor rolled for advancing one.
    ///
    // ── Time of Day cycle ────────────────────────────────────────────────────
    // Full cycle = 4 real hours. Dusk and Dawn are carved from Day's 3-hour gross allotment.
    // Game time only advances while the server is running (pauses on shutdown).
    public const long TodDayDurationMs = 150L * 60 * 1_000;   // 2 h 30 min pure daylight
    public const long TodDuskDurationMs = 15L * 60 * 1_000;   // 15 min darkening transition
    public const long TodNightDurationMs = 60L * 60 * 1_000;   // 1 h full night
    public const long TodDawnDurationMs = 15L * 60 * 1_000;   // 15 min lightening transition
    public const long TodCycleDurationMs = TodDayDurationMs + TodDuskDurationMs + TodNightDurationMs + TodDawnDurationMs; // 4 h
    // Cumulative phase-start offsets within the cycle (used on both server and client).
    public const long TodNightStartMs = TodDayDurationMs + TodDuskDurationMs;
    public const long TodDawnStartMs = TodNightStartMs + TodNightDurationMs;

    // ── NPC night-boost ──────────────────────────────────────────────────────
    // While TimePhase == Night, NPCs are tougher: HP flows through GameWorld.EffectiveNpcMaxHp plus a
    // proportional sweep at each Night boundary. Set it to 1.0 to disable the boost.
    public const double NpcNightHpMultiplier = 1.10;  // effective max HP (tankier)

    // ── Weather ──────────────────────────────────────────────────────────────
    // Global weather cycles via two timers (mirrors Time of Day; pauses while offline).
    // Timer Y (idle, weather Clear): 1-2 h, then a 40% trigger roll picks a weather by weight.
    // Timer Z (active): a per-type duration, then back to Clear.
    public const long WeatherIdleMinMs = 1L * 60 * 60 * 1_000;   // 1 h
    public const long WeatherIdleMaxMs = 2L * 60 * 60 * 1_000;   // 2 h
    public const int WeatherTriggerChancePercent = 40;
    // Weighted pick among non-Clear weathers (must sum to 100).
    public const int WeatherWeightRain = 50;
    public const int WeatherWeightHeatWave = 20;
    public const int WeatherWeightSnow = 20;
    public const int WeatherWeightHeavyWind = 10;
    // Active-duration bands (Timer Z) per weather.
    public const long WeatherRainMinMs = 5L * 60 * 1_000;   //  5 min
    public const long WeatherRainMaxMs = 60L * 60 * 1_000;   // 60 min
    public const long WeatherHeatWaveMinMs = 30L * 60 * 1_000;   // 30 min
    public const long WeatherHeatWaveMaxMs = 60L * 60 * 1_000;   // 60 min
    public const long WeatherSnowMinMs = 30L * 60 * 1_000;   // 30 min
    public const long WeatherSnowMaxMs = 60L * 60 * 1_000;   // 60 min
    public const long WeatherHeavyWindMinMs = 5L * 60 * 1_000;   //  5 min
    public const long WeatherHeavyWindMaxMs = 30L * 60 * 1_000;   // 30 min
    // Effect magnitudes. Set any multiplier to its identity (1 / 1.0) to disable that facet.
    // What weather does to combat — damage, stamina, durability, EXP — is a GAME's rule, so only the
    // ones Core can still act on survive here.
    public const double WeatherReducedRegenMultiplier = 0.5;  // Heat Wave + Snow: vital regen magnitude
    public const long WeatherHeavyWindCooldownMultiplier = 2;    // Heavy Wind: attack + cast cooldown doubled
    public const int WeatherHeavyWindMissChancePercent = 10;   // Heavy Wind: attacks and casts torn off course, attacker-side, before any block/dodge

    // ── Stains on the ground (server-authoritative, event-sourced) ────────────
    // A deposit puts a colored rectangle on the ground; the server dries every stain on a shared linear
    // clock and broadcasts only the maps whose LIST changed, because both sides run the same fade from the
    // same constant. Amounts are a dimensionless "stain strength"; the wire quantizes over [0, DecalMaxAmount]
    // to a byte.
    //
    // What makes a stain appear, how big it is and what color it is are a GAME's rules and live in its
    // script. What is here is the part that is the same whatever spilled.
    public const int DecalTickIntervalMs = 250;        // server dry/broadcast cadence; the client fades per-frame, so this only bounds event latency
    public const float DecalMaxAmount = 3.0f;          // hard per-stain cap; maps to wire byte 255
    public const int MaxMapDecals = 128;               // safety cap per map; past it the FAINTEST is evicted
    public const float DecalDryingPerSec = 0.015f;     // linear; lifetime = amount / this (0.6 → 40s, 1.0 → 67s). SHARED by the server tick and the client fade
    public const float DecalVisibleEpsilon = 0.02f;    // below this a stain is dry: skip the draw, and free the map once every stain is under it
    public const float DecalMaxAlpha = 0.9f;           // opacity at full saturation — near-opaque, so a stain reads solid rather than washed out

    // Render mapping (client only): OPACITY is freshness — a fresh deposit redarkens a stain to full, then it
    // fades in step with the amount. SIZE grows with the raw amount, so a stain spreads OUTWARD the more is
    // spilled on it and shrinks back as it dries.
    public const float DecalSizeFullAmount = 2.5f;     // amount at which the blob reaches its maximum size
    public const float DecalMinSizePx = 20f;           // blob diameter for a fresh light spill
    public const float DecalMaxSizePx = 120f;          // blob diameter at full accumulation (~3.75 tiles)
}
