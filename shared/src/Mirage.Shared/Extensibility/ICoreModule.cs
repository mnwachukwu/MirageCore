namespace Mirage.Shared.Extensibility;

/// <summary>
/// What a game hands the engine: its record families, its attribute keys, its packets, its work on the
/// tick, and what it wants to be told about.
///
/// <para><b>Everything a game declares arrives through one of these.</b> A module is asked to describe
/// itself once, before the world loads — so the registries every subsystem reads are complete and
/// immutable by the time anything runs, and no part of Core has to cope with a family or a key appearing
/// halfway through a session.</para>
///
/// <para><b>Declaring and acting are two phases, and they have to be.</b> What a module declares shapes
/// the engine that is then built, so nothing exists yet to hand it when <see cref="Configure"/> runs.
/// <see cref="Start"/> is the other end: the engine is built, the world is loaded, nothing is live yet,
/// and the module is handed the <see cref="IWorld"/> it will act through.</para>
///
/// <para>Several modules may be loaded, and they are configured in the order given. Two modules
/// claiming the same family id, attribute key, or packet command is an error at startup rather than a
/// silent last-one-wins.</para>
/// </summary>
public interface ICoreModule
{
    /// <summary>What this module is called, for logs and for the error that names a collision.</summary>
    string Name { get; }

    /// <summary>Declares everything this module adds. Nothing may be called here — what is declared
    /// decides what gets built.</summary>
    void Configure(ICoreBuilder builder);

    /// <summary>Hands this module the world, once. The engine is built and the world is loaded; the game
    /// loop has not started and no player is connected, so a module may set anything up without racing
    /// anything.
    ///
    /// <para><b>Keep the reference.</b> It is the only one a module ever gets, and everything a game does
    /// afterwards — from an observer, from tick work, from a policy — goes through it.</para>
    ///
    /// <para>Default is to do nothing, so a module that only declares data implements none of this.</para></summary>
    void Start(IWorld world) { }
}

/// <summary>What a module declares into. Handed to <see cref="ICoreModule.Configure"/> and not valid
/// after it returns.</summary>
public interface ICoreBuilder
{
    /// <summary>Attribute keys this module wants synced or labeled.</summary>
    AttributeSchema.Builder Attributes { get; }

    /// <summary>Packet commands this module can read. Registering one makes a line naming it
    /// deserialize; <see cref="AddPacketRoute"/> then delivers it.</summary>
    PacketRegistry.Builder Packets { get; }

    /// <summary>Record families this module adds, and the choice sets their fields draw from.</summary>
    void AddFamily(RecordFamily family);

    /// <summary>
    /// Fields this module adds to a family that already exists — the engine's <c>Items</c> or
    /// <c>NPCs</c>, or another module's.
    ///
    /// <para>🔴 <b>A record the engine owns has a closed set of properties, because Core cannot act on
    /// one it has never heard of.</b> A game's are open, and they belong on the same record rather than
    /// in a table beside it: a class gate is a fact about the sword, and a second family keyed by item
    /// number is that fact stored where it can be forgotten.</para>
    ///
    /// <para>Every field lands in the record's own attribute bag, so nothing here needs a new column, a
    /// new file, or a new packet. A key already on the family is refused by name.</para>
    /// </summary>
    void ExtendFamily(string familyId, IReadOnlyList<FieldDescriptor> fields);

    /// <inheritdoc cref="AddFamily"/>
    void AddChoiceSet(ChoiceSet choices);

    /// <summary>A place on a character where something can be worn. Declare none and nothing in this
    /// game is equippable, which is a supported configuration.</summary>
    void AddEquipSlot(EquipSlot slot);

    /// <summary>A value to show the player, read off a body's attributes wherever the named surface is
    /// drawn. Declare none and every surface stays as Core draws it.</summary>
    void AddDisplayField(DisplayField field);

    /// <summary>A row over a body's head, reading two of that body's attributes. Declare none and
    /// nothing is drawn over anyone. At most <see cref="OverheadBarSet.Max"/>.</summary>
    void AddOverheadBar(OverheadBar bar);

    /// <summary>A color for the name over a creature carrying one of that creature's attributes, asked
    /// in declaration order until one matches. Declare none and every creature is named in the plain
    /// color. At most <see cref="NameTintSet.Max"/>.</summary>
    void AddNameTint(NameTint tint);

    /// <summary>The three name colors a game owns, each packed <c>0xRRGGBB</c>: what a creature matching
    /// no tint is named in, what a MARKED player is named in, and what somebody who just started a fight
    /// pulses to. One module at most sets them, and left unsaid they are
    /// <see cref="NameTintSet.PlainRgb"/>, <see cref="NameTintSet.MarkedDefaultRgb"/> and
    /// <see cref="NameTintSet.AggressorDefaultRgb"/>.
    ///
    /// <para>Said together because they are one decision — how this world colors a name — and three calls
    /// would let a game set one and forget that the other two are still Core's.</para>
    ///
    /// <para>⚠ Access colors are NOT here. An operator's rank is a permission Core owns and it reads the
    /// same in every world, so a game cannot repaint it and cannot disguise one.</para></summary>
    void SetNameColors(int plainRgb, int markedRgb, int aggressorRgb);

    /// <summary>A descriptive tag a guild leader may apply to their guild — what a guild advertises
    /// about itself, which is a question about this game rather than about guilds.
    ///
    /// <para>Declare none and the picker is not offered, and a guild is known by its name.</para></summary>
    void AddGuildLabel(GuildLabel label);

    /// <summary>What founding a guild costs, in the money item. Consumed on success — the new guild's
    /// vault starts empty — and the client shows the figure on the Create button before anybody presses
    /// it, so a game changing it changes both halves at once.
    ///
    /// <para>Declare nothing and founding one is free, which is the only answer Core can give: how much
    /// a guild ought to be worth is a question about an economy the engine cannot see.</para></summary>
    void SetGuildCost(int cost);

    // ── What the engine's own conveniences cost ───────────────────────────────
    //
    // Core charges nothing it was not told to charge, and derives no price of its own. A shop sells for
    // the figure its author wrote on the item; everything below is a flat amount or a percentage of one,
    // and every one defaults to nothing.

    /// <summary>What moving your respawn point to an inn costs, in the money item. Declare nothing and
    /// an inn will anchor anybody for free.</summary>
    void SetInnSpawnCost(int cost);

    /// <summary>What a letter costs to send before anything is attached to it.</summary>
    void SetMailBaseCost(int cost);

    /// <summary>What each attachment adds to the postage.</summary>
    void SetMailAttachmentCost(int cost);

    /// <summary>A percentage of what is IN the parcel, added to the postage.
    ///
    /// <para>Keyed on the shipment rather than on the sender, deliberately: every flat fee is paid by
    /// whoever clicks, so a cost scaled to the payer is avoided by handing the job to an alt. What is in
    /// the parcel cannot be.</para></summary>
    void SetMailValuePercent(int percent);

    /// <summary>A percentage of a marketplace sale, taken from the seller when it completes.</summary>
    void SetMarketTaxPercent(int percent);

    /// <summary>A percentage of an item's authored price, which is what a shop pays for one a player
    /// brings in, scaled by its condition. Declare nothing and a shop buys nothing back.</summary>
    void SetSellBackPercent(int percent);

    /// <summary>A percentage of an item's authored price, which is what restoring it from broken to whole
    /// costs; part of a repair costs that share of it. Declare nothing and mending is free.</summary>
    void SetRepairPercent(int percent);

    /// <summary>How long a player waits between one trip home and the next, in seconds. Declare nothing
    /// and there is no wait.</summary>
    void SetHomeCooldown(int seconds);

    /// <summary>A screen this game paints: a title, the display surface that fills it, the verbs under
    /// it, and optionally a key that opens it. Declare none and the client shows only Core's own
    /// windows.</summary>
    void AddPanel(GamePanel panel);

    /// <summary>How many action-bar slots this game gives the player, at most
    /// <see cref="HotkeyBar.Max"/>. Declare none and there is no bar at all.
    ///
    /// <para>What may go in a slot is said elsewhere: <see cref="GameAction.Hotkeyable"/> on a verb,
    /// <see cref="RecordFamily.Hotkeyable"/> and <see cref="RecordFamily.HotkeyAction"/> on a family.
    /// A bar with nothing eligible is a row of boxes nothing can be put in.</para></summary>
    void SetHotkeyBar(int slots);

    /// <summary>A kind of line this game's own rules produce, that a player can read apart from
    /// everything else. Declare none and everything a game says lands on Core's System channel.
    ///
    /// <para>⚠ The id may not be one of Core's five — <c>Global</c>, <c>System</c>, <c>Tell</c>,
    /// <c>Guild</c>, or <c>Admin</c> — nor <c>Always</c>.</para></summary>
    void AddChatChannel(ChatChannelSpec channel);

    /// <summary>Something the player may do that this game invented. A stock client offers it by
    /// caption and sends its id back; <see cref="AddActionHandler"/> then handles it. It may carry a
    /// key from <see cref="GameKey.Offered"/>, which reaches it without opening a menu.</summary>
    void AddAction(GameAction action);

    /// <inheritdoc cref="AddAction"/>
    void AddActionHandler(IActionHandler handler);

    /// <summary>Where this module's own packets go. Declare none and a command registered above
    /// deserializes and then reaches nothing.</summary>
    void AddPacketRoute(IPacketRoute route);

    /// <summary>Work this module wants done on the tick.</summary>
    void AddTickWork(ITickWork work);

    /// <summary>Something to be told what happened in the world. Declare none and the engine runs
    /// exactly as it does now and tells nothing.</summary>
    void AddObserver(IWorldObserver observer);

    /// <summary>Something that answers commands typed at the SERVER'S console. Declare none and an
    /// unknown command is refused there exactly as it is with no game loaded.</summary>
    void AddConsoleHandler(IConsoleHandler handler);

    /// <summary>What this game says about dying — whether it happens, what it costs, where the body
    /// comes back. Declare none and <c>DeathSystem.Kill</c> moves the body and takes nothing.</summary>
    void AddDeathPolicy(IDeathPolicy policy);

    /// <summary>What this game says about moving under your own power — whether a body can still
    /// manage a run, and what a run costs it. Declare none and every body runs for free, forever.</summary>
    void AddMovePolicy(IMovePolicy policy);

    /// <summary>What this game says about using something out of a bag. Every policy must allow it; the
    /// first refusal stops the use and stands.</summary>
    void AddUsePolicy(IUsePolicy policy);

    /// <summary>What this game says about a slain creature's drops — how often a line lands, how much of
    /// it there is, and who it belongs to. Declare none and every creature drops what its table says at
    /// the rate its table says, free to whoever reaches it.</summary>
    void AddLootPolicy(ILootPolicy policy);

    /// <summary>Something to ask before a character exists — a class, a bloodline, a starting town. The
    /// answer is written onto the new character under the choice's key, before anything is told they
    /// joined. Declare none and the creation screen asks for a name and a face, which is a supported
    /// world.</summary>
    void AddCreationChoice(CreationChoice choice);

    /// <summary>What this game says about a dropped connection — how long the body stays in the world
    /// before it is taken out. Declare none and a disconnect removes the player at once.</summary>
    void AddLingerPolicy(ILingerPolicy policy);
}
