namespace Mirage.Shared;

/// <summary>
/// Game-wide tuning values and hard limits shared by server, client, and editor: collection caps,
/// combat and AI cadences, economy costs, weather and time-of-day timings, and the blood-pool model.
/// Everything here is a compile-time constant except the assembly-derived version fields and the
/// blood-strength helpers at the bottom.
/// </summary>
public static class Constants
{
    public const string GameName = "Mirage Source Remastered";
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
    // Share of the DRAINED FRACTION a Sub* potion pays into each of the other two vitals — see
    // StatFormulas.SubPotionGain. Spending a quarter of one bar buys an eighth of each of the others,
    // which holds at any pool size; the old rule paid half the raw amount and only made sense while every
    // pool was equal.
    public const int SubPotionExchangePercent = 50;

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
    // Proximity uses the shared spell-range radius (r=5) via WorldCoordHelper.IsInSpellRange.
    public const int MaxTradeOfferItems = 8;
    public const long TradeInviteTimeoutMs = 30_000;
    public const int MaxMapNpcs = 20;   // per-map NPC spawn slots, 1-based (1..MaxMapNpcs)
    public const int MaxPlayerSpells = 20;
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
    public const int SpellRangeTiles = (ViewportTilesY / 2) - 1;   // 5

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
    public const int RunSpeed = 8;

    public const int MaxEditorSessions = 5;
    public const int MaxClasses = 50;

    public const int DefaultItemRespawnSeconds = 120;

    public const int MaxLevel = 255;
    public const int PointsPerLevel = 3;

    // Levels one gear tier covers. The armory authors a rung every five levels (five per band), so a piece
    // bought on tier is worn for five levels before the next one is reachable. EconomyFormulas prices
    // equipment against the gold earned across exactly this span — buy once, wear it the whole rung.
    public const int GearTierLevels = 5;

    // Action-bar slots, bound to keys 1..4 (and to the gamepad's four face buttons under a trigger
    // modifier).  Four is a UI limit as much as a design one: the bar sits in the sidebar strip above the
    // links, and four icons is what fits there at the strip's width without crowding them.
    public const int MaxHotkeys = 4;
    // Every authored class starts at this total stat allotment (Str+Def+Int+Spd). With PointsPerLevel it
    // inverts a stat spread back into a character level, which is how an NPC gets a player-faithful
    // "virtual level" (level = (statSum - PlayerBaseStatTotal)/PointsPerLevel + 1) driving its vitals,
    // mitigation, EXP and strength readout exactly as a real level drives a player's.
    public const int PlayerBaseStatTotal = 20;

    // PvP — level gap that fully protects the lower-level player (no EXP/gear/item loss);
    // also gates the attacker's EXP reward on a kill.
    public const int PvpLevelGapMax = 5;

    // NPC vs player relative-strength tier: a virtual-level gap of at least this much (either
    // direction) reads as "no contest" and drives the kill-feed flavor when a mob kills a player
    // (mob this much stronger → "slaughtered"; this much weaker → a careless death). Mirrors the
    // outer tiers of the on-target strength readout in PacketHandler (levelDiff >= 5 / <= -5).
    public const int NpcStrengthTierGap = 5;

    // How far under a player an attack-on-sight NPC has to be before it stops starting fights. The test is
    // STRICT: a virtual level (StatFormulas.NpcLevel) MORE than this far below the player's is beneath its
    // notice, so a mob exactly this far under still comes for them and the tier below it is the quiet one.
    //
    // Same value as NpcStrengthTierGap and the same "no contest" idea, kept separate because they are tuned
    // for different things — one is what a zone FEELS like to walk back through, the other is flavor text —
    // and because the kill feed's test is inclusive where this one is not.
    //
    // Unprovoked acquisition only. A mob that is struck still fights back however far beneath the player it
    // is, so a revisited zone goes quiet without going inert.
    public const int NpcAggroIgnoreLevelGap = 5;

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

    // PK flag — duration applied/extended on each fresh kill, and the per-death reduction
    // when a flagged player is killed (2 deaths fully clear a single fresh flag).
    public const long PkFlagDurationSeconds = 3600;
    public const long PkKillReductionSeconds = 1800;
    // Post-respawn protection window for freshly-respawned PK players.
    public const long PkGraceDurationSeconds = 60;

    // Aggressor flag — lit when a player throws the first hit at a non-PK / non-aggressor target,
    // refreshed every time the aggressor lands or receives any combat hit (incl. 0-dmg/block/dodge),
    // cleared on death, on natural lapse, or on becoming a PKer. While lit: guards treat the player
    // as a PKer, the player is attackable in safe zones, and a kill on them carries no PK flag.
    public const long AggressorDurationSeconds = 30;
    public const long AggressorDurationMs = AggressorDurationSeconds * 1000;

    // Death-time drop chances, % per slot.  Compared against Random.Shared.Next(100).
    public const int NormalDropChancePercent = 20;  // each non-equipped slot on normal death
    public const int PkEqDropChancePercent = 25;  // each equipped slot on PK death

    // The spawn point is a server SETTING, not a constant — see ServerConfig.Spawn. It defaults to the
    // middle of map 1, which is what it was when it lived here.

    // ── Combat timing ────────────────────────────────────────────────────────
    // TWO cooldowns, not one. The three ACTION values share a 1-second beat — player attack, NPC
    // attack, spell cast — because they are the same act of committing to a turn, and the client's
    // InputProcessor paces to the same value so server and client agree on it.
    public const long PlayerAttackCooldownMs = 1000;
    public const long NpcAttackCooldownMs = 1000;
    public const long SpellCastCooldownMs = 1000;

    /// <summary>Cast-packet <c>TargetType</c> for a cast that found nothing: the pose and the cooldown play,
    /// no projectile does. The rest of the convention is 0=player, 1=npc, 2=self, 3=traversal.</summary>
    public const byte CastTargetNone = 4;

    // Drinking runs on its OWN clock, and a slower one. Sharing the action beat made a potion cost a
    // swing, which turned self-healing into a straight substitute for a second body in the fight; on a
    // separate 2s track it stays useful without replacing the healer standing next to you.
    public const long PotionCooldownMs = 2000;
    // There is deliberately NO post-cast MOVE lockout: casting does not restrict movement at all, for
    // players or NPCs. At equal run speed a caster can't open a gap anyway, so a lockout would only
    // forbid walking during a second in which no recast was possible. The 1-second cast cadence above
    // is what paces spell damage.

    // After N consecutive "want to cast but in melee" ticks, a mage NPC stops trying to retreat
    // and casts the spell at melee range anyway, then resets and tries to break off again next
    // tick.  Prevents players from kiting the kiter — refusing to leave melee range no longer
    // locks the NPC into a never-casts-while-adjacent loop.
    public const int NpcMeleeKiteMaxAttempts = 3;

    // An Int NPC that WEAVES melee and magic commits to whichever modality it rolls for a random run of this many
    // ready beats before re-rolling, instead of re-rolling every beat.  Rapid per-beat cast↔melee switching reads
    // as twitchy/mechanical; a short commitment gives a legible "casts for a bit, then melees for a bit" rhythm.
    // Only bites the MIXED builds (both Str>0 and Int>0): a pure caster always casts, a pure-melee mob never does.
    public const int NpcWeaveCommitMinBeats = 3;
    public const int NpcWeaveCommitMaxBeats = 5;

    // ── Loot rolling ─────────────────────────────────────────────────────────
    // Players whose damage credit reaches this fraction of the top-damage contributor are eligible to roll
    // for tagged loot on NPC death, and to share the currency. Read it as "within a quarter of the top
    // dealer": someone who did the work alongside the leader shares the kill, someone who chipped does not.
    public const double LootDamageContributionThreshold = 0.75;
    public const int LootRollSides = 100;          // d100, +1 → roll in [1..100]
    public const long LootTagDurationMs = 30_000;  // 30 s exclusive pickup window

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
    // SP drained per tile while a chasing NPC RUNS (mirrors the player's per-tile run drain).  Against the
    // NPC SP pool (StatFormulas.GetNpcMaxSp = Spd×2) this gasses a chaser out after a longer sprint, dropping
    // it to a walk the player outpaces.  Higher = shorter sprints, easier escapes.
    public const int NpcRunSpDrainPerTile = 1;   // also drained per kite (retreat) tile — same "SP per tile moved"

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

    // Run-stamina hysteresis: once a running NPC drains SP to empty it must rebuild the reservoir back up to
    // this FRACTION of its max SP before it may sprint again (chase OR kite).  Without the gate an NPC burns
    // each SP-regen trickle the instant it lands — flicking run/walk every regen tick and snapping the slide;
    // with it the NPC commits to one sustained walk (rebuilding) then one sustained run per cycle.
    public const float NpcRunReservoirFraction = 0.5f;

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

    // Item slot 2 is the spellcasting reagent (a Currency item authored in data). A SubHp cast consumes
    // CombatFormulas.SubHpReagentCostExact(LevelReq) of it — the magic-side mirror of a warrior's repair upkeep.
    // The item's definition (name, value, drops, shop stock) is authored in item data; the code only references
    // this index to check/consume the stack, exactly as gold does.
    public const int CastingReagentItemIndex = 2;

    // Item slot 3 is valor — the war currency (a Currency item authored in data, flagged NonTradeable).
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
    // Weekly guild tax = guild Level * this, taken from the vault at the daily 00:00 settlement on the
    // guild's founding weekday. L0 = free (no perks either); L1 = 35,000, ... L5 = 175,000.
    public const int GuildTaxPerLevel = 35_000;
    // Valor auto-offsets the weekly tax at settlement (consumed before gold): every
    // GuildValorPerTaxDiscount valor in the vault removes GuildGoldPerTaxDiscount gold from the tax, in whole
    // increments, capped at GuildValorTaxOffsetCapPercent% of the tax (at L5: 250 valor → 87,500 off of
    // 175,000). Scales with the tax it offsets, so valor's relief stays a fixed share of the bill.
    public const int GuildValorPerTaxDiscount = 10;
    public const int GuildGoldPerTaxDiscount = 3_500;
    public const int GuildValorTaxOffsetCapPercent = 50;
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

    // ── Guild leveling & perks ─────────────────────────────────────────────────
    // Levels 0-5; start at 0, perks begin at level 1.
    public const int GuildMaxLevel = 5;
    // Guild XP per member mob-KO (the minor trickle; guild quests are the main XP driver).
    public const int GuildExpPerKill = 1;
    // Cumulative guild XP to REACH each level (0 => level 0). The curve balloons (~4x per tier) so higher
    // tiers are near-impossible solo; recruiting + questing accelerate it.
    public const long GuildLevel1Exp = 1_000_000;
    public const long GuildLevel2Exp = 4_000_000;
    public const long GuildLevel3Exp = 16_000_000;
    public const long GuildLevel4Exp = 64_000_000;
    public const long GuildLevel5Exp = 256_000_000;
    // Perks unlock one per level, cumulative, and apply only when the guild is >= the perk's level AND its
    // tax is paid (GuildRecord.PerksActive). L1 items drop more often; L2 chance to shrug off durability/
    // reagent wear (NOT death wear); L3 +% individual EXP; L4 chance to double a drop; L5 (vault chunk)
    // chance a mob kill trickles gold to the vault.
    public const int GuildPerkLevelDropRate = 1;
    public const int GuildPerkDropRateBonusPercent = 20;
    public const int GuildPerkLevelPreventWear = 2;
    public const int GuildPerkPreventWearChancePercent = 20;
    public const int GuildPerkLevelBonusExp = 3;
    public const int GuildPerkBonusExpPercent = 10;
    public const int GuildPerkLevelDoubleDrop = 4;
    public const int GuildPerkDoubleDropChancePercent = 5;
    public const int GuildPerkLevelVaultGold = 5;
    public const int GuildPerkVaultGoldChancePercent = 25;
    // Gold the L5 perk trickles into the vault on a qualifying KO. A constant rather than a literal at
    // its call site because it belongs to the guild gold family above and has to move with it.
    public const int GuildPerkVaultGold = 35;
    // Recent vault-log entries kept for the Vault tab's Donations + Spending views (newest-first, capped). Display-only.
    public const int GuildRecentVaultLogMax = 15;

    // ── Guild quests ───────────────────────────────────────────────────────────
    // One active quest at a time; up to this many completed per day. A quest can be abandoned (freeing a
    // fresh acquire) with no refund; the acquire cost is what limits re-rolling.
    public const int GuildQuestMaxPerDay = 3;
    // Acquire cost = this * guild level (L0 = free), charged in gold.
    public const int GuildQuestCostPerLevel = 17_500;
    // A quest expires this many hours after it is acquired.
    public const int GuildQuestDurationHours = 24;
    // Kill count = base + difficulty/perKill, clamped to max; difficulty = the target NPC's Str+Def+Int.
    // Quest kill count is a BIG objective (hundreds of kills) that scales UP with mob difficulty — tougher
    // mobs mean more (and harder) kills, matched to the bigger XP reward. Base is for the weakest mob; each
    // point of difficulty adds kills, capped at the maximum.
    public const int GuildQuestBaseKills = 300;
    public const int GuildQuestKillsAddedPerDifficulty = 1;   // kills added per point of NPC difficulty (Str+Def+Int)
    public const int GuildQuestMaxKills = 1000;
    // +/- spread applied to the baseline kill count (and the rewards, in lockstep) so quests aren't a flat,
    // linear grind: a bigger roll = more kills AND more XP/gold, a smaller roll = fewer of each.
    public const int GuildQuestVariationPercent = 25;
    // Reward XP/gold = base*(guildLevel+1) + difficulty*perDifficulty. ~33k XP for an L0 guild on a low-
    // difficulty mob; scales with guild level (to chase the ballooning curve) + mob difficulty.
    public const long GuildQuestBaseExp = 33_000;
    public const long GuildQuestExpPerDifficulty = 300;
    // The gold half of the reward scales with the acquire cost that gates it — a quest has to stay worth
    // running, and cost and payout are two ends of the same lever. The XP half is untouched: guild XP is
    // its own currency and has no exchange rate with the player economy.
    public const long GuildQuestBaseGold = 8_750;
    public const long GuildQuestGoldPerDifficulty = 175;
    // BOSS mobs (NpcRecord.IsBoss) use a COMPRESSED kill-count curve so a quest to kill bosses is tens, not
    // hundreds: kills = clamp(BossBaseKills + difficulty * BossKillsPer100Difficulty / 100, 1, BossMaxKills).
    // Still scales with strength, just on a much smaller axis (~30 for a weak boss up to the cap for a maxed one).
    public const int GuildQuestBossBaseKills = 30;
    public const int GuildQuestBossKillsPer100Difficulty = 10;   // kills added per 100 points of difficulty (Str+Def+Int)
    public const int GuildQuestBossMaxKills = 100;
    // A boss quest pays this percent of the XP + gold a same-difficulty normal mob would (far fewer kills, so a
    // slighter reward): attractive but never best-in-slot. The 3/day acquire cap blocks any reroll-for-boss.
    public const int GuildQuestBossRewardPercent = 50;
    // At MAX guild level quest XP is worthless (the guild can't level), so it is ESCHEWED entirely (0) and the
    // gold reward is bumped by this percent instead — keeping maxed guilds running quests to bolster the vault.
    public const int GuildQuestMaxLevelGoldBonusPercent = 25;

    // ── Guild wars ─────────────────────────────────────────────────────────────
    // A level-0 guild can neither declare NOR return a declaration (it defends only) — leveling is grindy,
    // so throwaway guilds can't become war-harassment machines.
    public const int GuildWarMinLevelToDeclare = 1;
    // Declare cost (gold, from the vault) = base - (targetLevel - declarerLevel) x step: punching DOWN
    // (a lower target) costs more, punching UP costs less, same level = flat base. E.g. L1-on-L5 = 21,000,
    // L5-on-L1 = 49,000. Floored so a huge gap can't drive it to nothing.
    public const int GuildWarDeclareBaseCost = 35_000;
    public const int GuildWarDeclareLevelStep = 3_500;
    public const int GuildWarDeclareMinCost = 3_500;
    // Declaring on a level-0 guild DOUBLES the whole cost (that war can never go mutual, so its daily
    // maintenance is paid indefinitely — costly for a low payout, which protects level-0 guilds).
    public const int GuildWarL0TargetCostMultiplier = 2;
    // Declarer's daily maintenance = this % of the declare cost, taken at the 00:00 settlement while the
    // war stays one-sided (a mutual war waives it for both). Inability to pay drops the declaration.
    public const int GuildWarDailyMaintenancePercent = 50;
    // A one-sided grievance goes live after this warmup grace; a mutual war (reciprocated) pops immediately.
    public const int GuildWarWarmupSeconds = 600;   // 10 min
    // A declaration can't be retracted until this long after it was made.
    public const int GuildWarRetractionLockSeconds = 900;   // 15 min
    // Declare on up to this many guilds at once (receiving declarations is unlimited).
    public const int GuildWarMaxConcurrentDeclarations = 5;
    // Cap on officer war-action requests pending Leader approval (declare/retract), to bound the queue.
    public const int GuildWarMaxPendingRequests = 10;

    // ── Guild war combat ─────────────────────────────────────────────────────
    // A war death's penalty is worn-gear durability ONLY (no item drops, no EXP loss/transfer). The wear is
    // DOUBLED vs a normal death (normal = 10% of max; war = 20%).
    public const int GuildWarDeathWearPercent = 20;
    // On death the vault pays this % of the repair cost of that doubled wear, whole-or-nothing; if it pays,
    // only the remaining % of the wear lands on the gear (25% => half a normal death's wear). If the vault
    // can't cover the full share it pays nothing and the FULL doubled wear falls on the player.
    public const int GuildWarVaultRepairPercent = 75;
    public const int GuildWarPlayerWearPercent = 25;
    // A guild-war participant's respawn timer is a flat value — it neither escalates nor decays, and
    // is independent of the normal death penalty steps.
    public const int GuildWarRespawnSeconds = 30;

    // ── Guild war attrition & resolution (MUTUAL wars only) ──────────────────
    // Each side's tug-of-war meter starts here; a war death depletes the victim's side and restores the
    // killer's by the same amount (loss = the other side's gain). Push the enemy's meter to 0 to win.
    public const int GuildWarAttritionPool = 1000;
    // A war death's attrition swing has TWO parts:
    //   1. Durability "war spend" — the gold this death drained from the vault (its repair cost). Applied in
    //      FULL with NO diminishing returns: real economic loss always drives attrition. It's 0 for a naked /
    //      uncovered death (nothing to repair), which is what part 2 covers.
    //   2. A flat base-death rate (this const), DR-scaled by how farmed the target is but floored at the DR
    //      table's minimum (never 0) — so EVERY death moves the meter and you can't unequip to contribute
    //      nothing to your own war score.
    public const int GuildWarBaseDeathAttrition = 20;
    // Per-target DR applied ONLY to the base-death rate: the % counted at stages 1..N; beyond the last stage
    // it STAYS at the minimum (the last entry), never 0, so a heavily-farmed target is still worth some
    // attrition. The treasury "war spend" is never DR-scaled. The target recovers 1 stage per recovery period.
    public static readonly int[] GuildWarDrStagePercents = { 100, 75, 50, 25 };
    public const int GuildWarDrRecoverySeconds = 300;   // 5 min per recovered DR stage
    // Bankruptcy short-circuit: this many consecutive vault-uncovered war deaths auto-loses a mutual war.
    public const int GuildWarBankruptcyStreak = 5;
    // A mutual war goes cold (a draw) if neither side pushes the other to a new attrition low for this long.
    public const int GuildWarColdSeconds = 2 * 60 * 60;   // 2 hours
    // After a war with an opponent ends, this cooldown must elapse before re-declaring on them (anti-pile-on).
    public const int GuildWarRedeclareCooldownSeconds = 60 * 60;   // 1 hour

    // ── Guild war wagers (MUTUAL wars only, consensual) ──────────────────────
    // A matched ante must be agreed within this long of the war becoming mutual; after the window closes no
    // new ante can be set (an already-locked ante rides to the war's end regardless).
    public const int GuildWarWagerWindowSeconds = 60 * 60;   // 1 hour
    // The ante (and a no-ante peace offering) is capped at this % of the staking guild's vault. Since an ante
    // is matched, this effectively caps it at 50% of the SMALLER of the two vaults (the other side can't
    // accept more than it can stake). Winner-take-all; a cold draw returns each side's own stake.
    public const int GuildWarWagerMaxVaultPercent = 50;

    // ── Territory income ─────────────────────────────────────────────────────
    // Per PvE mob-kill in a controlled territory (by ANYONE), a chance to generate gold into the controlling
    // guild's vault: the non-owner base if the killer isn't in the owning guild, the owner base if they are
    // (stacks with the L5 perk for an owning member). Scaled by the weeks-held multiplier. PvP never feeds it.
    public const int TerritoryIncomeChancePercent = 25;
    public const int TerritoryIncomeNonOwnerGold = 35;
    public const int TerritoryIncomeOwnerGold = 70;
    // Weeks-held income multiplier caps here (= 1 "month"): weeks 0→x1, 1→x2, 2→x3, 3+→x4.
    public const int TerritoryWeeksHeldCap = 4;
    // Per-territory per-day accrual cap (anti-snowball); resets when the day's income is credited.
    public const long TerritoryIncomeDailyCap = 17_500;
    // ── Territory war night ──────────────────────────────────────────────────
    // The weekly contest slot and the weekly boundary that follows it are server SETTINGS — see
    // ServerConfig.Schedule. They default to Saturday 8pm and the Sunday after, which is what they were
    // when they lived here. WeeksHeld itself ticks at war-night retention, not at the boundary.
    // Up to this many challengers per territory; with the defender that's TerritoryMaxChallengers + 1 guilds.
    public const int TerritoryMaxChallengers = 4;
    // Flat cost to challenge an UNCLAIMED territory (a non-refundable sink). An OWNED territory costs the base
    // grudge-war declare formula on the two guilds' levels (no level-0-target doubling) — GuildWarFormulas.BaseDeclareCost.
    public const int TerritoryUnclaimedChallengeCost = 35_000;

    // ── Seasonal leaderboard ─────────────────────────────────────────────────
    // A season is this many whole weeks (4 x 13 = 52/yr). Boundaries roll on the configured week reset day (Sunday,
    // the day after war night). Scoring skips the season's first week so established control carries in.
    public const int TerritorySeasonWeeks = 13;
    public const int TerritorySeasonScoringStartWeek = 1;
    // Season active-member gate: a member earns the per-member placing payout only if they were online >= this
    // many seconds (3h) within the trailing window (3 days). Tracked as a rolling total that resets after an
    // offline gap longer than the window (see GuildMember.ActiveSeconds).
    public const long GuildActiveMemberWindowSeconds = 3 * 24 * 3600;
    public const long GuildActiveMemberMinSeconds = 3 * 3600;
    // Weekly leaderboard points for holding a territory: a base per week times a consecutive-hold bonus that
    // compounds (+bonus% per consecutive week held, capped) — losing the territory resets WeeksHeld (the streak).
    public const long TerritorySeasonPointsPerWeek = 100;
    public const int TerritorySeasonHoldBonusPercentPerWeek = 25;
    public const int TerritorySeasonHoldBonusCapWeeks = 12;   // streak weeks the bonus scales with, then flat
    // Season-end placing payouts: the per-active-member gold (delivered to their account; DEFERRED delivery) and
    // the guild-vault gold (credited now). "Other scorers" (4th+) get the flat scorer payout; non-scorers get 0.
    public const long TerritorySeason1stMemberGold = 175_000, TerritorySeason1stVaultGold = 700_000;
    public const long TerritorySeason2ndMemberGold = 87_500, TerritorySeason2ndVaultGold = 350_000;
    public const long TerritorySeason3rdMemberGold = 35_000, TerritorySeason3rdVaultGold = 175_000;
    public const long TerritorySeasonScorerMemberGold = 17_500, TerritorySeasonScorerVaultGold = 35_000;

    // ── Territory capture contest (king-of-the-hill) ─────────────────────────
    // War-night phases (Server Time): a 10-min setup ramp, the 20-min king-of-the-hill contest, a 10-min
    // cooldown. The contest scores on a 5s tick (20 min = 240 ticks).
    public const int TerritoryContestSetupSeconds = 10 * 60;
    public const int TerritoryContestSeconds = 20 * 60;
    public const int TerritoryContestCooldownSeconds = 10 * 60;
    public const int TerritoryContestTickSeconds = 5;
    // Capture points: 1 per this many maps, clamped to [min, max]; randomly placed, labeled Alpha..Echo.
    public const int TerritoryCapturePointMapsPer = 5;
    public const int TerritoryMinCapturePoints = 2;
    public const int TerritoryMaxCapturePoints = 5;
    // Capture radius, in tiles. Sized against SpellRangeTiles (5), which is what makes the ranged/melee trade
    // on a point work out, and both edges land exactly at 4:
    //   - A caster OUTSIDE the zone reaches inward to (R + 1) - SpellRange. At 4 that is the center exactly
    //     and nothing past it, so poking from outside pressures a holder without owning the point.
    //   - A melee at the center answering that caster closes to adjacent, landing at distance R from the
    //     center — still inside, still contesting. At R = 3 the same answer would step them out of the zone.
    //   - A caster INSIDE the zone contests, but the zone is only 2R across, so it has 3 tiles of retreat
    //     before falling out — a kite it loses to a melee that commits (7 tiles, about a second sprinting).
    // Also 9 tiles across, so the whole zone fits the viewport (ViewportTilesY = 12) and can be read at once.
    public const int TerritoryCapturePointRadius = 4;
    // Capture meter: signed [-Full, +Full]. -Full = the owner securely holds; a challenger with the tick's
    // strict-plurality pushes it up (+1/tick); the owner pushes it back down; a contested/empty point drifts
    // toward 0 (neutral). At +Full the point flips to the challenger (reset to -Full = they now securely hold).
    // A full owner→challenger swing is 2*Full ticks; at 5s/tick and Full=3 that's ~30s.
    public const int TerritoryCaptureFull = 3;
    // The owner scores a held point only while the meter is at/below -NeutralBand; the band around 0 scores
    // nobody (a ~10s neutral zone mid-swing).
    public const int TerritoryCaptureNeutralBand = 1;
    // KotH scoring: an owned point scores this per tick for its owner, plus a defender edge when the owner is
    // the territory's defender (so a held point pays 2/tick vs an attacker's 1/tick).
    public const int TerritoryOwnedScorePerTick = 1;
    public const int TerritoryDefenderScoreBonus = 1;

    // ── Valor earning (war currency, ValorItemIndex) ─────────────────────────────
    // Per-kill chance for each war-participant damage contributor to earn 1 valor from a grudge-war kill.
    public const int GuildWarGrudgeValorChancePercent = 10;
    // Per-kill chance for each war-participant damage contributor to earn 1 valor from a TERRITORY-contest
    // kill — the higher rate that makes holding land the richer valor source.
    public const int GuildWarTerritoryValorChancePercent = 50;
    // Per-kill chance for each guild-quest contributor to earn 1 valor when they help kill their guild's
    // active quest mob.
    public const int GuildQuestValorChancePercent = 25;

    /// <summary>The share of a mob's damage a player deals for the kill to count toward their quest
    /// objectives — player and guild alike, and the valor rolled for advancing one.
    ///
    /// <para>EXP is split by share, so a token hit earns a token amount and nothing is gained by it. A quest
    /// objective is not divisible: the tick is the same size however little was done for it, so without a floor
    /// one point of damage on someone else's kill is a full one, and tagging becomes the fastest way to quest.</para>
    ///
    /// <para>Twelve leaves room for eight to share a mob comfortably, which is well past any party size, so a
    /// group genuinely working a mob together all qualify while a passer-by landing one hit does not.</para>
    ///
    /// <para>🔴 A PARTY PARTNER of someone who clears it shares the credit on one damaging blow instead — see
    /// <c>CombatSystem.QuestCreditFor</c>. The pair still has to put a real share in between them.</para></summary>
    public const int QuestCreditDamagePercent = 12;

    // ── Death & respawn ──────────────────────────────────────────────────────
    // Non-war respawn delay = penalty steps x this (base 10s). Steps escalate +1 per death, decay 1 step
    // per full minute since the last death, and clamp to [1, max] (so the cap is max x 10s = 120s).
    public const int RespawnPenaltyStepSeconds = 10;
    public const int RespawnMaxPenaltySteps = 12;
    // On death a caster destroys reagents (item CastingReagentItemIndex) based on its PREPARED spell —
    // independently of, and on top of, any equipped weapon's wear (a weapon wears from the weapon; reagents
    // wear from the prepared spell). The amount = the per-cast reagent cost at that tier (the prepared spell's
    // power, else the strongest known SubHp spell's) x this multiplier, scaled by the death's wear percent
    // (a normal death = 10%). At 1 reagent = 1 gold this tracks a warrior's weapon-repair cost; in a guild war
    // it is doubled and the vault absorbs it exactly like weapon wear.
    public const int CasterDeathReagentMultiplier = 10;

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
    // While TimePhase == Night, NPCs are boosted. Damage/EXP are checked at their combat chokepoints;
    // HP flows through GameWorld.EffectiveNpcMaxHp plus a proportional sweep at each Night boundary.
    // Set any to 1.0 to disable that facet.
    public const double NpcNightDamageMultiplier = 1.10;  // melee + spell damage to players
    public const double NpcNightHpMultiplier = 1.10;  // effective max HP (tankier)
    public const double NpcNightExpMultiplier = 1.20;  // EXP reward per kill

    // NPC-vs-player damage disfavor: on-level mobs get +20% HP (favor, StatFormulas.GetNpcMaxHp) AND hit players
    // this much softer, so PvE fights stay impactful without spiking a squishy build down.  PvE-only lever
    // (player→NPC and NPC→NPC stay full mirror); applied post-mitigation at CombatSystem.ApplyNpcDamageToPlayer
    // and folded into the kill-EXP danger term (ExpFormulas.ExpForKill) so EXP prices the softened real threat.
    public const double NpcVsPlayerDamageMultiplier = 0.70;

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
    public const int WeatherRainDurabilityWear = 2;    // Rain: durability loss doubled — 2 pts per combat wear event (vs 1) AND x2 on-death gear damage
    public const int WeatherRainReagentMultiplier = 2; // SubHp casting-reagent cost multiplier (magic mirror of the wear above)
    public const double WeatherReducedRegenMultiplier = 0.5;  // Heat Wave + Snow: vital regen magnitude
    public const int WeatherHeatWaveSpCostMultiplier = 2;    // Heat Wave: block/crit/dodge/run stamina cost
    public const long WeatherHeavyWindCooldownMultiplier = 2;    // Heavy Wind: attack + cast cooldown doubled
    public const int WeatherHeavyWindMissChancePercent = 10;   // Heavy Wind: attacks and casts torn off course, attacker-side, before any block/dodge
    // Per-weather EXP reward multiplier (compounds with Night + party). Clear = 1.0.
    public const double WeatherRainExpMultiplier = 1.05;
    public const double WeatherHeatWaveExpMultiplier = 1.15;
    public const double WeatherSnowExpMultiplier = 1.15;
    public const double WeatherHeavyWindExpMultiplier = 1.25;
    // Snow temporarily reduces max vitals (current scaled proportionally at the boundary).
    public const double WeatherSnowMaxHpMultiplier = 0.90;
    public const double WeatherSnowMaxMpMultiplier = 0.80;
    public const double WeatherSnowMaxSpMultiplier = 0.80;

    // ── Blood pools (server-authoritative, event-sourced) ─────────────────────
    // When an entity takes HP damage, blood is deposited on its tile sized by intensity =
    // clamp(|damage| / targetMaxHp, 0, 1): bigger hits (relative to the target's HP) leave more.  The server
    // decays the field and broadcasts only the tiles a deposit touched; each client replays the SAME linear
    // decay locally, so both sides must share BloodDissipationPerSec.  There is no tile-to-tile spread — a
    // pool grows OUTWARD purely by its decal size scaling up as the tile accumulates (see the render consts).
    // Amounts are a dimensionless "stain strength"; the wire quantizes amount in [0, BloodMaxTileAmount] to a byte 0..255.
    public const int BloodTickIntervalMs = 250;        // server sim/broadcast cadence (client fade is per-frame, so this only bounds event latency)
    public const float BloodPerHitScale = 0.45f;       // per-hit deposit = intensity * this (intensity = hit-size x closeness boost, see BloodDepositStrength)
    public const float BloodStrengthExponent = 0.5f;   // concave damage-fraction → strength map (sqrt): LOW-damage hits still leave clear blood
    public const float BloodMinHitStrength = 0.12f;    // floor so ANY damaging hit shows something (a chip off a huge-HP boss still bleeds)
    public const float BloodLownessScale = 3.0f;       // per-hit deposit boost by HP-left-after-hit: x1 at full HP up to x(1+this)=x4 on a killing blow → pooling ACCELERATES as a mob weakens, and any kill (even a 1-shot) gives a big "death" splash
    public const float BloodTrailHpThreshold = 0.34f;  // an entity at/below this fraction of max HP leaves a blood TRAIL as it walks/runs (drips onto fresh tiles)
    public const float BloodTrailStrength = 0.25f;     // deposit strength for one trail drip → ~0.11 amount: a small stain (~24px) that lasts ~6.2s (decays from 0.11 to the 0.02 visibility floor at BloodDissipationPerSec) with ~1 droplet
    public const float BloodMaxTileAmount = 3.0f;      // hard per-pool amount cap; maps to wire byte 255
    public const int MaxMapBloodPools = 128;           // safety cap on live blood pools per map; the faintest is evicted past this (merge + decay usually keep it far lower)
    public const float BloodDissipationPerSec = 0.015f; // linear decay; lifetime = amount / this (0.6 → 40s, 1.0 → 67s). SHARED by server sim + client decay.
    public const float BloodVisibleEpsilon = 0.02f;    // below this a tile is dry: skip render, and free the map once every tile is under it
    public const float BloodMaxAlpha = 0.9f;           // decal opacity at full saturation (near-opaque so pools read solid, not washed out)
    // Render mapping (client only): OPACITY = freshness — any hit REDARKENS the stain to full, then it fades in
    // step with the amount as it decays (a new hit on an almost-gone stain darkens it back to full).  SIZE grows
    // with the raw amount, so a tile's pool expands OUTWARD the more it's bled on and shrinks back as it dries.
    public const float BloodSizeFullAmount = 2.5f;     // amount at which the pool blob reaches max SIZE — a pool starts small and, as the victim weakens and deposits accelerate, the finishing hits push it near/at max (the emergent "death splash")
    public const float BloodDecalMinSizePx = 20f;      // pool blob diameter (px) for a fresh light spill (small start)
    public const float BloodDecalMaxSizePx = 120f;     // pool blob diameter (px) at full accumulation (~3.75 tiles) — big, but reached only slowly
    // The furthest-reaching blood element (a max blob ~85px, or a droplet flung ~BloodSatelliteDistMax past the
    // tile center) sits under ~3 tiles from the tile ORIGIN.  EmitBloodDecals scans this many tiles beyond the
    // strict visible bounds so blood whose origin sits just off-screen still renders its overhang (the world pass
    // is scissor-clipped, so the off-screen part is trimmed) — without it, blood pops in/out at the viewport edge.
    public const int BloodCullMarginTiles = 3;
    public const uint BloodTintRgb = 0x520808;         // dark arterial red (packed 0xRRGGBB); dims naturally under the night multiply

    /// <summary>Maps a hit to a 0..1 blood "strength" driving both the pool deposit and the droplet burst:
    /// the fraction of the target's max HP the hit dealt, run through a concave curve
    /// (<see cref="BloodStrengthExponent"/>) so LOW-damage hits still leave clearly-visible blood, with a
    /// floor (<see cref="BloodMinHitStrength"/>) so any damaging hit shows something.</summary>
    public static float BloodStrength(int damage, int maxHp)
    {
        if (damage <= 0 || maxHp <= 0) return 0f;
        float raw = Math.Clamp(damage / (float)maxHp, 0f, 1f);
        return Math.Max(MathF.Pow(raw, BloodStrengthExponent), BloodMinHitStrength);
    }

    /// <summary>Per-hit blood-pool deposit intensity: the hit-size term (<see cref="BloodStrength"/> — bigger hits
    /// leave more) times a CLOSENESS boost that rises as the victim nears death (<see cref="BloodLownessScale"/>).
    /// The boost keys on the HP left AFTER the hit, so a KILLING blow (0 HP after) always gets the FULL boost —
    /// a quick 1-2 hit kill splashes big, and a long fight (already near-death at the end) is barely changed.  The
    /// "death splash" falls out of this with no special death case.  Can exceed 1.  <paramref name="victimHp"/> is
    /// the PRE-hit HP.</summary>
    public static float BloodDepositStrength(int damage, int maxHp, int victimHp)
    {
        if (maxHp <= 0) return 0f;
        float hpAfter = Math.Clamp((victimHp - damage) / (float)maxHp, 0f, 1f);   // 0 on a kill → finishing blows get the full boost
        return BloodStrength(damage, maxHp) * (1f + BloodLownessScale * (1f - hpAfter));
    }
}
