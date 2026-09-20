using Mirage.Shared.Protocol;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Everything the loaded modules declared, built once and never changed after.
///
/// <para><b>This answers "what game is this?".</b> Record families, attribute keys, packet
/// commands and tick work each have a registry that some subsystem reads, and until they are built
/// those subsystems have nothing to read. Building them all in one pass, before the world loads, is
/// what lets every reader treat its registry as complete and immutable — no part of Core copes with a
/// family or a key appearing halfway through a session.</para>
///
/// <para><b>Core is the first module.</b> Its families and packets are declared through
/// <see cref="ICoreBuilder"/> exactly as a game's are, rather than being merged in by a privileged
/// path. If the interface were not sufficient to describe Core, it would not be sufficient to describe
/// a game either, and this is where that would show.</para>
/// </summary>
public sealed class CoreRegistry
{
    /// <summary>An engine with no game loaded: Core's own families and packets, no game attributes, and
    /// nothing on the tick.</summary>
    public static CoreRegistry CoreOnly { get; } = Build();

    internal CoreRegistry(RecordSchema schema, AttributeSchema attributes, PacketRegistry packets,
                         TickSchedule tick, EquipSlotSet equipSlots, OverheadBarSet overheadBars,
                         NameTintSet nameTints,
                         DisplayFieldSet displayFields, PacketRoutes packetRoutes,
                         GameActions actions, IReadOnlyList<IActionHandler> actionHandlers,
                         GamePanels panels, ChatChannelSet chatChannels, int hotkeyBarSlots,
                         GuildLabelSet guildLabels, GamePrices prices,
                         IReadOnlyList<IWorldObserver> observers,
                         IReadOnlyList<IConsoleHandler> consoleHandlers,
                         IReadOnlyList<IDeathPolicy> deathPolicies, IReadOnlyList<ILingerPolicy> lingerPolicies,
                         IReadOnlyList<IMovePolicy> movePolicies,
                         IReadOnlyList<IUsePolicy> usePolicies, IReadOnlyList<ILootPolicy> lootPolicies,
                         CreationChoiceSet creationChoices,
                         IReadOnlyList<ICoreModule> modules, IReadOnlyList<string> moduleNames)
    {
        Schema = schema;
        Attributes = attributes;
        Packets = packets;
        Tick = tick;
        EquipSlots = equipSlots;
        OverheadBars = overheadBars;
        NameTints = nameTints;
        DisplayFields = displayFields;
        PacketRoutes = packetRoutes;
        Actions = actions;
        ActionHandlers = actionHandlers;
        Panels = panels;
        ChatChannels = chatChannels;
        HotkeyBarSlots = hotkeyBarSlots;
        GuildLabels = guildLabels;
        Prices = prices;
        Observers = observers;
        ConsoleHandlers = consoleHandlers;
        DeathPolicies = deathPolicies;
        LingerPolicies = lingerPolicies;
        MovePolicies = movePolicies;
        UsePolicies = usePolicies;
        LootPolicies = lootPolicies;
        CreationChoices = creationChoices;
        Modules = modules;
        ModuleNames = moduleNames;
    }

    /// <summary>The record families every loaded module declared, and the choice sets their fields draw
    /// from. What a server sends an editor on connect.</summary>
    public RecordSchema Schema { get; }

    /// <summary>Every attribute key that was declared, in declaration order.</summary>
    public AttributeSchema Attributes { get; }

    /// <summary>How to read every packet command any module can receive.</summary>
    public PacketRegistry Packets { get; }

    /// <summary>The ordered work the game loop drives.</summary>
    public TickSchedule Tick { get; }

    /// <summary>Where a character may wear something. Empty until a game says otherwise.</summary>
    public EquipSlotSet EquipSlots { get; }

    /// <summary>What is drawn over a body's head, in draw order. Empty until a game says otherwise,
    /// and then nothing is drawn over anyone.</summary>
    public OverheadBarSet OverheadBars { get; }

    /// <summary>What colors the name over a creature's head. Plain white until a game says otherwise.</summary>
    public NameTintSet NameTints { get; }

    /// <summary>What each surface shows about a body. Empty until a game says otherwise, and then every
    /// surface draws only what Core itself puts there.</summary>
    public DisplayFieldSet DisplayFields { get; }

    /// <summary>Where a module's own packets go, indexed by command. Empty in an engine with no game
    /// loaded, and then every command belongs to Core's own handler.</summary>
    public PacketRoutes PacketRoutes { get; }

    /// <summary>What a game lets the player do, grouped by the surface that offers it. Empty in an
    /// engine with no game loaded, and then every menu holds only Core's own items.</summary>
    public GameActions Actions { get; }

    /// <summary>What does those things, in the order their modules were configured.</summary>
    public IReadOnlyList<IActionHandler> ActionHandlers { get; }

    /// <summary>The screens this game paints, by the id that opens one. Empty in an engine with no game
    /// loaded, and then the client shows only Core's own windows.</summary>
    public GamePanels Panels { get; }

    /// <summary>The chat channels this game declared, beside Core's own five. Empty in an engine with no
    /// game loaded, and then everything a game would say lands on Core's System channel.</summary>
    public ChatChannelSet ChatChannels { get; }

    /// <summary>How many action-bar slots this game gives the player. <see cref="HotkeyBar.None"/> in an
    /// engine with no game loaded, and then the client draws no bar.</summary>
    public int HotkeyBarSlots { get; }

    /// <summary>The tags a guild leader may apply, in display order. Empty in an engine with no game
    /// loaded, and then a guild is known by its name.</summary>
    public GuildLabelSet GuildLabels { get; }

    /// <summary>What the engine's own conveniences cost, as this game declared them. Every figure is
    /// nothing in an engine with no game loaded, and then none of them charges anybody.</summary>
    public GamePrices Prices { get; }

    /// <summary>What is told when something happens in the world, in the order their modules were
    /// configured. Empty in an engine with no game loaded, which then tells nobody anything.</summary>
    public IReadOnlyList<IWorldObserver> Observers { get; }

    /// <summary>What this game adds to the server console, asked in the order their modules were
    /// configured until one answers. Empty in an engine with no game loaded.</summary>
    public IReadOnlyList<IConsoleHandler> ConsoleHandlers { get; }

    /// <summary>What this game says about dying, asked in the order their modules were configured.</summary>
    public IReadOnlyList<IDeathPolicy> DeathPolicies { get; }

    /// <summary>What a game lets somebody use out of their bag. Empty in an engine with no game
    /// loaded, and then every use Core itself understands is allowed.</summary>
    /// <summary>What a game says about moving under your own power, asked in the order their modules
    /// were configured. Empty in an engine with no game loaded, and then a run costs nothing.</summary>
    public IReadOnlyList<IMovePolicy> MovePolicies { get; }

    public IReadOnlyList<IUsePolicy> UsePolicies { get; }

    /// <summary>What a game says a slain creature leaves behind. Empty in an engine with no game loaded,
    /// and then a creature drops what its own table says, free to whoever reaches it.</summary>
    public IReadOnlyList<ILootPolicy> LootPolicies { get; }

    /// <summary>What this game asks before a character exists. Empty until a game says otherwise, and
    /// then the creation screen asks for a name and an appearance and nothing else.</summary>
    public CreationChoiceSet CreationChoices { get; }

    /// <summary>What this game says about a dropped connection. Empty means a disconnect takes the player
    /// straight out of the world.</summary>
    public IReadOnlyList<ILingerPolicy> LingerPolicies { get; }

    /// <summary>The modules that were loaded, in the order they were configured, Core first. Kept so
    /// the host can hand each one the world once the engine is built — see
    /// <see cref="ICoreModule.Start"/>.</summary>
    public IReadOnlyList<ICoreModule> Modules { get; }

    /// <summary>What those modules are called, in the same order.</summary>
    public IReadOnlyList<string> ModuleNames { get; }

    /// <inheritdoc cref="Build(IEnumerable{ICoreModule})"/>
    public static CoreRegistry Build(params ICoreModule[] modules)
        => Build((IEnumerable<ICoreModule>)(modules ?? []));

    /// <summary>Runs every module's <see cref="ICoreModule.Configure"/> once, in the order given, and
    /// freezes what they declared.</summary>
    /// <exception cref="CoreModuleException">Two modules claimed the same family id, choice-set id,
    /// attribute key or packet command, or a module threw while configuring. Either way the exception
    /// names the module, because the stack of a builder call does not.</exception>
    public static CoreRegistry Build(IEnumerable<ICoreModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var builder = new CoreBuilder();
        var names = new List<string>();
        var loaded = new List<ICoreModule>();

        foreach (var module in new[] { (ICoreModule)new CoreModule() }.Concat(modules))
        {
            if (module is null) throw new CoreModuleException("A null module was supplied.", "(null)");

            string name = string.IsNullOrWhiteSpace(module.Name) ? module.GetType().Name : module.Name;
            builder.BeginModule(name);
            try
            {
                module.Configure(builder);
            }
            catch (CoreModuleException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CoreModuleException($"Module '{name}' failed while configuring: {ex.Message}", name, ex);
            }

            names.Add(name);
            loaded.Add(module);
        }

        return builder.Freeze(loaded, names);
    }
}

/// <summary>A module could not be loaded. Carries <see cref="ModuleName"/> because the failure is
/// almost always a collision with a module loaded earlier, and the stack shows only the builder.</summary>
public sealed class CoreModuleException : Exception
{
    public CoreModuleException(string message, string moduleName, Exception? inner = null)
        : base(message, inner) => ModuleName = moduleName;

    public CoreModuleException() { }

    public CoreModuleException(string message) : base(message) { }

    public CoreModuleException(string message, Exception inner) : base(message, inner) { }

    /// <summary>The module being configured when this was raised.</summary>
    public string ModuleName { get; } = string.Empty;
}

/// <summary>Core's own declarations, made through the same seam a game uses.</summary>
internal sealed class CoreModule : ICoreModule
{
    internal const string ModuleName = "Core";

    public string Name => ModuleName;

    public void Configure(ICoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var family in CoreRecordFamilies.World) builder.AddFamily(family);
        CorePackets.Register(builder.Packets);
    }
}

/// <summary>
/// The one <see cref="ICoreBuilder"/>, handed to each module in turn.
///
/// <para>It remembers which module is talking, so a collision blames the module that caused it rather
/// than reporting a bare duplicate key. After <see cref="Freeze"/> it refuses everything: a module that
/// squirrels the builder away and writes to it later is declaring into a registry somebody is already
/// reading, which is the failure this whole one-pass design exists to prevent.</para>
/// </summary>
internal sealed class CoreBuilder : ICoreBuilder
{
    private readonly List<RecordFamily> _families = [];
    private readonly List<ChoiceSet> _choices = [];
    private readonly TickSchedule.Builder _tick = new();
    private readonly List<EquipSlot> _equipSlots = [];
    private readonly List<OverheadBar> _overheadBars = [];
    private readonly List<NameTint> _nameTints = [];
    private readonly List<GuildLabel> _guildLabels = [];
    private int _otherwiseNameRgb = NameTintSet.PlainRgb;
    private int _markedRgb = NameTintSet.MarkedDefaultRgb;
    private int _aggressorRgb = NameTintSet.AggressorDefaultRgb;
    private string? _nameColorsSetBy;
    private int _guildCost;
    private int _innSpawnCost;
    private int _mailBaseCost;
    private int _mailAttachmentCost;
    private int _mailValuePercent;
    private int _marketTaxPercent;
    private int _sellBackPercent;
    private int _repairPercent;
    private int _homeCooldownSeconds;
    // Which module named each price, by the name this refuses in. One game sets a price; a second
    // one setting the same price is two games disagreeing about an economy, which is a startup error
    // rather than a last-writer-wins.
    private readonly Dictionary<string, string> _priceSetBy = new(StringComparer.Ordinal);
    private readonly List<DisplayField> _displayFields = [];
    private readonly List<IPacketRoute> _packetRoutes = [];
    private readonly List<GameAction> _actions = [];
    private readonly List<GamePanel> _panels = [];
    private readonly List<ChatChannelSpec> _chatChannels = [];
    private int _hotkeyBarSlots = HotkeyBar.None;
    private string? _hotkeyBarBy;
    private readonly List<IActionHandler> _actionHandlers = [];
    private readonly List<IWorldObserver> _observers = [];
    private readonly List<IDeathPolicy> _deathPolicies = [];
    private readonly List<IMovePolicy> _movePolicies = [];
    private readonly List<IUsePolicy> _usePolicies = [];
    private readonly List<ILootPolicy> _lootPolicies = [];
    private readonly List<IConsoleHandler> _consoleHandlers = [];
    private readonly List<CreationChoice> _creationChoices = [];
    private readonly List<ILingerPolicy> _lingerPolicies = [];
    private string _module = "(none)";
    private bool _frozen;

    public AttributeSchema.Builder Attributes { get; } = new();
    public PacketRegistry.Builder Packets { get; } = new();

    internal void BeginModule(string name) => _module = name;

    /// <summary>
    /// Add a game's own fields to a family that already exists — the engine's <c>Items</c> or
    /// <c>NPCs</c>, or another module's.
    ///
    /// <para>🔴 <b>A record the engine owns has a closed set of properties, because Core cannot act on
    /// one it has never heard of.</b> A game's are open, and they belong on the same record rather than
    /// in a table beside it: a class gate is a fact about the sword, and a second family keyed by item
    /// number is that fact stored where it can be forgotten.</para>
    ///
    /// <para>Every field lands in the record's own attribute bag, so nothing here needs a new column, a
    /// new file, or a new packet — an extended family is the same family with more rows on its
    /// form.</para>
    ///
    /// <para>⚠ A key that clashes with one already on the family is refused by name. Two modules both
    /// calling something <c>power</c> would write over each other, and the one that lost would present
    /// as an authored value that will not stay put.</para>
    /// </summary>
    public void ExtendFamily(string familyId, IReadOnlyList<FieldDescriptor> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Refuse();

        int at = _families.FindIndex(f => string.Equals(f.Id, familyId, StringComparison.Ordinal));

        if (at < 0)
        {
            throw new CoreModuleException(
                $"Module '{_module}' added fields to record family '{familyId}', which nothing declared.",
                _module);
        }

        var family = _families[at];

        foreach (FieldDescriptor field in fields)
        {
            if (family.Fields.Any(f => string.Equals(f.Key, field.Key, StringComparison.Ordinal)))
            {
                throw new CoreModuleException(
                    $"Module '{_module}' added field '{field.Key}' to record family '{familyId}', which "
                    + "already has one by that name.", _module);
            }
        }

        _families[at] = family with { Fields = [.. family.Fields, .. fields] };
    }

    public void AddFamily(RecordFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Refuse();

        if (string.IsNullOrWhiteSpace(family.Id))
            throw new CoreModuleException($"Module '{_module}' declared a record family with no id.", _module);

        if (_families.Any(f => string.Equals(f.Id, family.Id, StringComparison.Ordinal)))
            throw new CoreModuleException($"Module '{_module}' declared record family '{family.Id}', which is already declared.", _module);

        // Two families sharing a directory would read each other's files as their own, which looks like
        // record corruption rather than a registration mistake.
        var clash = _families.FirstOrDefault(f =>
            string.Equals(f.EffectiveDirectory, family.EffectiveDirectory, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared record family '{family.Id}' in directory '{family.EffectiveDirectory}', "
                + $"which '{clash.Id}' already uses.", _module);
        }

        // 🔴 Two halves, and the missing one is silent. A family a player may bind to the bar, with no
        // verb saying what firing one DOES, gives a slot that draws an icon and answers nothing — which
        // looks exactly like a feature somebody has not finished yet.
        //
        // Core's own families are the exception: a blank action on Items means the engine uses it the
        // way the bag does, and only the engine can mean that.
        if (family.Hotkeyable && family.HotkeyAction.Length == 0
            && !string.Equals(_module, CoreModule.ModuleName, StringComparison.Ordinal))
        {
            throw new CoreModuleException(
                $"Module '{_module}' said records of '{family.Id}' may go on the action bar, but named no "
                + "verb to fire one with. Set HotkeyAction to one of this game's actions.", _module);
        }

        _families.Add(family);
    }

    public void AddChoiceSet(ChoiceSet choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Refuse();

        if (string.IsNullOrWhiteSpace(choices.Id))
            throw new CoreModuleException($"Module '{_module}' declared a choice set with no id.", _module);

        if (_choices.Any(c => string.Equals(c.Id, choices.Id, StringComparison.Ordinal)))
            throw new CoreModuleException($"Module '{_module}' declared choice set '{choices.Id}', which is already declared.", _module);

        _choices.Add(choices);
    }

    public void AddEquipSlot(EquipSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Refuse();

        if (string.IsNullOrWhiteSpace(slot.Key))
            throw new CoreModuleException($"Module '{_module}' declared an equipment slot with no key.", _module);

        if (_equipSlots.Any(s => string.Equals(s.Key, slot.Key, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared equipment slot '{slot.Key}', which is already declared.", _module);
        }

        _equipSlots.Add(slot);
    }

    public void AddDisplayField(DisplayField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Refuse();

        if (string.IsNullOrWhiteSpace(field.Surface))
            throw new CoreModuleException($"Module '{_module}' declared a display field with no surface.", _module);

        // A heading says something about the rows under it rather than about a value, so it is the one
        // style that needs no key. Everything else without one would draw a caption and nothing beside it.
        if (field.Style != DisplayStyle.Heading && string.IsNullOrWhiteSpace(field.ValueKey))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared a display field on surface '{field.Surface}' with no value key.", _module);
        }

        if (field.Style == DisplayStyle.Meter && string.IsNullOrWhiteSpace(field.MaxKey))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared a meter on '{field.ValueKey}' with no maximum key.", _module);
        }

        _displayFields.Add(field);
    }

    public void AddOverheadBar(OverheadBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        Refuse();

        if (string.IsNullOrWhiteSpace(bar.ValueKey) || string.IsNullOrWhiteSpace(bar.MaxKey))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared an overhead bar without both a value key and a maximum key.", _module);
        }

        if (_overheadBars.Any(b => string.Equals(b.ValueKey, bar.ValueKey, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared an overhead bar on '{bar.ValueKey}', which already has one.", _module);
        }

        if (_overheadBars.Count == OverheadBarSet.Max)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared overhead bar '{bar.ValueKey}', which is one more than the "
                + $"{OverheadBarSet.Max} a head has room for.", _module);
        }

        _overheadBars.Add(bar);
    }

    public void AddNameTint(NameTint tint)
    {
        ArgumentNullException.ThrowIfNull(tint);
        Refuse();

        if (string.IsNullOrWhiteSpace(tint.Key))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared a name tint naming no attribute.", _module);
        }

        if (_nameTints.Any(t => string.Equals(t.Key, tint.Key, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared a name tint on '{tint.Key}', which already has one.", _module);
        }

        if (_nameTints.Count == NameTintSet.Max)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared name tint '{tint.Key}', which is one more than the "
                + $"{NameTintSet.Max} a world may have.", _module);
        }

        _nameTints.Add(tint);
    }

    /// <summary>How this world colors a name.
    ///
    /// <para>⚠ One MODULE owns the answer, and may restate it — a scripting layer offering the three as
    /// separate calls arrives here once per call. A SECOND module is refused, because which of them won
    /// would then depend on load order.</para></summary>
    public void SetNameColors(int plainRgb, int markedRgb, int aggressorRgb)
    {
        Refuse();

        if (_nameColorsSetBy is not null && _nameColorsSetBy != _module)
        {
            throw new CoreModuleException(
                $"Module '{_module}' set the name colors, which module '{_nameColorsSetBy}' "
                + "has already set.", _module);
        }

        _nameColorsSetBy = _module;
        _otherwiseNameRgb = plainRgb;
        _markedRgb = markedRgb;
        _aggressorRgb = aggressorRgb;
    }

    /// <summary>One declared price, on the same terms as the name colors: a game may restate its own
    /// answer, and a second game naming the same price is refused by name.</summary>
    private int Price(string what, int amount)
    {
        Refuse();

        if (_priceSetBy.TryGetValue(what, out string? owner) && owner != _module)
        {
            throw new CoreModuleException(
                $"Module '{_module}' set {what}, which module '{owner}' has already set.", _module);
        }

        _priceSetBy[what] = _module;
        return Math.Max(0, amount);
    }

    /// <summary>A tag a guild may wear. Two games claiming one key would be two meanings for the same
    /// saved string, so the second is refused.</summary>
    public void AddGuildLabel(GuildLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Refuse();

        if (string.IsNullOrWhiteSpace(label.Key))
            throw new CoreModuleException($"Module '{_module}' declared a guild label with no key.", _module);

        if (_guildLabels.Any(l => string.Equals(l.Key, label.Key, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared the guild label '{label.Key}', which is already declared.",
                _module);
        }

        _guildLabels.Add(label);
    }

    public void SetGuildCost(int cost) => _guildCost = Price("the guild cost", cost);

    public void SetInnSpawnCost(int cost) => _innSpawnCost = Price("the inn spawn cost", cost);

    public void SetMailBaseCost(int cost) => _mailBaseCost = Price("the base postage", cost);

    public void SetMailAttachmentCost(int cost)
        => _mailAttachmentCost = Price("the postage per attachment", cost);

    public void SetMailValuePercent(int percent)
        => _mailValuePercent = Price("the postage share of a parcel", percent);

    public void SetMarketTaxPercent(int percent) => _marketTaxPercent = Price("the sale tax", percent);

    public void SetSellBackPercent(int percent) => _sellBackPercent = Price("the sell-back share", percent);

    public void SetRepairPercent(int percent) => _repairPercent = Price("the repair share", percent);

    public void SetHomeCooldown(int seconds) => _homeCooldownSeconds = Price("the trip-home wait", seconds);

    public void AddPanel(GamePanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Refuse();

        if (string.IsNullOrWhiteSpace(panel.Id))
            throw new CoreModuleException($"Module '{_module}' declared a panel with no id.", _module);

        if (_panels.Any(p => string.Equals(p.Id, panel.Id, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared panel '{panel.Id}', which is already declared.", _module);
        }

        if (!GameKey.IsOffered(panel.Key))
        {
            throw new CoreModuleException(
                $"Module '{_module}' put panel '{panel.Id}' on key '{panel.Key}', which is not a key a "
                + $"game may bind. Those are: {GameKey.Listed}.", _module);
        }

        if (Claimed(panel.Key) is { } holder)
        {
            throw new CoreModuleException(
                $"Module '{_module}' put panel '{panel.Id}' on key '{panel.Key}', which {holder} already "
                + "has. One key does one thing.", _module);
        }

        // A window the player cannot dismiss, and nothing to take it away, is a rectangle over their
        // game forever. Refused by name, because the alternative is a world that loads and a player
        // who has to restart the client.
        if (panel.Held && panel.While.AsksNothing)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared panel '{panel.Id}' held, but said nothing about when it "
                + "is up. A held panel has no close button, so something has to take it away: give it "
                + "a condition, or let the player close it.", _module);
        }

        _panels.Add(panel);
    }

    public void AddChatChannel(ChatChannelSpec channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Refuse();

        if (string.IsNullOrWhiteSpace(channel.Id))
            throw new CoreModuleException($"Module '{_module}' declared a chat channel with no id.", _module);

        // Core sends on these five itself. A declaration that took one would answer for the engine's own
        // lines, so a player switching off a game's feed would lose their tells with it.
        if (Protocol.ChatChannels.IsCore(channel.Id))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared chat channel '{channel.Id}', which is one of Core's own. "
                + $"Those are: {string.Join(", ", Protocol.ChatChannels.Core)}, and Always.", _module);
        }

        if (_chatChannels.Any(c => string.Equals(c.Id, channel.Id, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared chat channel '{channel.Id}', which is already declared.", _module);
        }

        _chatChannels.Add(channel);
    }

    public void SetHotkeyBar(int slots)
    {
        Refuse();

        if (!HotkeyBar.IsOffered(slots))
        {
            throw new CoreModuleException(
                $"Module '{_module}' asked for {slots} action-bar slots. A bar holds 0 to "
                + $"{HotkeyBar.Max}, and the client draws it as one row in the sidebar.", _module);
        }

        // Two games in one world, each sizing the bar, would leave the player with whichever loaded
        // last and no way to tell which. One bar, one number, said once.
        if (_hotkeyBarBy is { } already && !string.Equals(already, _module, StringComparison.Ordinal))
        {
            throw new CoreModuleException(
                $"Module '{_module}' sized the action bar, which module '{already}' already did. "
                + "There is one bar.", _module);
        }

        _hotkeyBarSlots = slots;
        _hotkeyBarBy = _module;
    }

    public void AddAction(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Refuse();

        if (string.IsNullOrWhiteSpace(action.Id))
            throw new CoreModuleException($"Module '{_module}' declared an action with no id.", _module);

        if (_actions.Any(a => string.Equals(a.Id, action.Id, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared action '{action.Id}', which is already declared.", _module);
        }

        if (!GameKey.IsOffered(action.Key))
        {
            throw new CoreModuleException(
                $"Module '{_module}' put action '{action.Id}' on key '{action.Key}', which is not a key a "
                + $"game may bind. Those are: {GameKey.Listed}.", _module);
        }

        if (Claimed(action.Key) is { } holder)
        {
            throw new CoreModuleException(
                $"Module '{_module}' put action '{action.Id}' on key '{action.Key}', which {holder} "
                + "already has. One key does one thing.", _module);
        }

        _actions.Add(action);
    }

    /// <summary>What already answers to <paramref name="key"/>, or null for one nothing holds.
    ///
    /// <para>Actions and panels share one keyboard, so the check has to look at both. Two things on one
    /// key is the failure this prevents, and it would otherwise be silent: the client would bind
    /// whichever it found first, and which one that is depends on declaration order.</para></summary>
    private string? Claimed(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        foreach (GameAction action in _actions)
            if (string.Equals(action.Key, key, StringComparison.Ordinal))
                return $"action '{action.Id}'";

        foreach (GamePanel panel in _panels)
            if (string.Equals(panel.Key, key, StringComparison.Ordinal))
                return $"panel '{panel.Id}'";

        return null;
    }

    public void AddActionHandler(IActionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Refuse();

        if (handler.Actions.Count == 0)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared action handler '{handler.Name}', which owns no actions.", _module);
        }

        foreach (string id in handler.Actions)
        {
            var clash = _actionHandlers.FirstOrDefault(h => h.Actions.Contains(id, StringComparer.Ordinal));
            if (clash is not null)
            {
                throw new CoreModuleException(
                    $"Module '{_module}' declared handler '{handler.Name}' for action '{id}', "
                    + $"which '{clash.Name}' already owns.", _module);
            }
        }

        _actionHandlers.Add(handler);
    }

    public void AddPacketRoute(IPacketRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Refuse();

        if (route.Commands.Count == 0)
        {
            throw new CoreModuleException(
                $"Module '{_module}' declared packet route '{route.Name}', which owns no commands.", _module);
        }

        foreach (string command in route.Commands)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new CoreModuleException($"Module '{_module}' declared a packet route with a blank command.", _module);

            // Two routes half-handling one command is a coin toss at runtime rather than an error, so it
            // is an error here instead.
            var clash = _packetRoutes.FirstOrDefault(r => r.Commands.Contains(command, StringComparer.Ordinal));
            if (clash is not null)
            {
                throw new CoreModuleException(
                    $"Module '{_module}' declared packet route '{route.Name}' for command '{command}', "
                    + $"which '{clash.Name}' already owns.", _module);
            }
        }

        _packetRoutes.Add(route);
    }

    public void AddTickWork(ITickWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Refuse();
        _tick.Add(work);
    }

    // Neither of these can collide: two modules both wanting to hear about a step, or both having an
    // opinion about dying, is the ordinary case. They are kept in declaration order and all of them run.
    public void AddObserver(IWorldObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Refuse();
        _observers.Add(observer);
    }

    public void AddConsoleHandler(IConsoleHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Refuse();
        _consoleHandlers.Add(handler);
    }

    public void AddDeathPolicy(IDeathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _deathPolicies.Add(policy);
    }

    public void AddMovePolicy(IMovePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _movePolicies.Add(policy);
    }

    public void AddUsePolicy(IUsePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _usePolicies.Add(policy);
    }

    public void AddLootPolicy(ILootPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _lootPolicies.Add(policy);
    }

    public void AddCreationChoice(CreationChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        Refuse();

        if (string.IsNullOrWhiteSpace(choice.Key))
            throw new CoreModuleException($"Module '{_module}' asked something at creation with no key.", _module);

        // Two choices under one key would both write it, and the second would win silently - which
        // presents as a player's pick not sticking rather than as a declaration mistake.
        if (_creationChoices.Any(c => string.Equals(c.Key, choice.Key, StringComparison.Ordinal)))
        {
            throw new CoreModuleException(
                $"Module '{_module}' asked twice under key '{choice.Key}' at creation.", _module);
        }

        _creationChoices.Add(choice);
    }

    public void AddLingerPolicy(ILingerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _lingerPolicies.Add(policy);
    }

    internal CoreRegistry Freeze(IReadOnlyList<ICoreModule> modules, IReadOnlyList<string> moduleNames)
    {
        Refuse();
        _frozen = true;

        var attributes = Attributes.Build();

        // ⚠ Both halves or neither. A tint the client never receives the attribute for colors nothing,
        // and nothing reports it: every creature simply comes out the plain color, which is a world
        // that looks finished. Checked here rather than in AddNameTint because a module may declare
        // the tint before the attribute it reads.
        foreach (var tint in _nameTints)
        {
            if (attributes.IsVisibleTo(tint.Key, AttributeVisibility.Viewport)) continue;

            throw new CoreModuleException(
                $"A name tint reads '{tint.Key}', which no module declared as an attribute onlookers "
                + "can see. Declare it to the viewport, or the tint colors nothing.", _module);
        }

        var schema = new RecordSchema { Families = [.. _families], ChoiceSets = [.. _choices] };
        return new CoreRegistry(schema, attributes, Packets.Build(), _tick.Build(),
                                new EquipSlotSet(_equipSlots), new OverheadBarSet(_overheadBars),
                                new NameTintSet(_nameTints, _otherwiseNameRgb, _markedRgb, _aggressorRgb),
                                new DisplayFieldSet(_displayFields), new PacketRoutes([.. _packetRoutes]),
                                new GameActions(_actions), [.. _actionHandlers], new GamePanels([.. _panels]),
                                new ChatChannelSet([.. _chatChannels]), _hotkeyBarSlots,
                                new GuildLabelSet([.. _guildLabels]),
                                new GamePrices
                                {
                                    GuildCost = _guildCost,
                                    InnSpawnCost = _innSpawnCost,
                                    MailBaseCost = _mailBaseCost,
                                    MailAttachmentCost = _mailAttachmentCost,
                                    MailValuePercent = _mailValuePercent,
                                    MarketTaxPercent = _marketTaxPercent,
                                    SellBackPercent = _sellBackPercent,
                                    RepairPercent = _repairPercent,
                                    HomeCooldownSeconds = _homeCooldownSeconds,
                                },
                                [.. _observers], [.. _consoleHandlers], [.. _deathPolicies],
                                [.. _lingerPolicies], [.. _movePolicies],
                                [.. _usePolicies], [.. _lootPolicies],
                                new CreationChoiceSet([.. _creationChoices]), modules, moduleNames);
    }

    private void Refuse()
    {
        if (_frozen)
            throw new CoreModuleException($"Module '{_module}' kept the builder and used it after loading finished.", _module);
    }
}
