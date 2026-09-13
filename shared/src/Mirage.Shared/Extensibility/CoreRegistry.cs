using Mirage.Shared.Protocol;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Everything the loaded modules declared, built once and never changed after.
///
/// <para><b>This is the answer to "what game is this?".</b> Record families, attribute keys, packet
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
                         DisplayFieldSet displayFields, IReadOnlyList<IWorldObserver> observers,
                         IReadOnlyList<IDeathPolicy> deathPolicies, IReadOnlyList<ILingerPolicy> lingerPolicies,
                         IReadOnlyList<ICoreModule> modules, IReadOnlyList<string> moduleNames)
    {
        Schema = schema;
        Attributes = attributes;
        Packets = packets;
        Tick = tick;
        EquipSlots = equipSlots;
        OverheadBars = overheadBars;
        DisplayFields = displayFields;
        Observers = observers;
        DeathPolicies = deathPolicies;
        LingerPolicies = lingerPolicies;
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

    /// <summary>What each surface shows about a body. Empty until a game says otherwise, and then every
    /// surface draws only what Core itself puts there.</summary>
    public DisplayFieldSet DisplayFields { get; }

    /// <summary>What is told when something happens in the world, in the order their modules were
    /// configured. Empty in an engine with no game loaded, which then tells nobody anything.</summary>
    public IReadOnlyList<IWorldObserver> Observers { get; }

    /// <summary>What this game says about dying, asked in the order their modules were configured.</summary>
    public IReadOnlyList<IDeathPolicy> DeathPolicies { get; }

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
    public string Name => "Core";

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
    private readonly List<DisplayField> _displayFields = [];
    private readonly List<IWorldObserver> _observers = [];
    private readonly List<IDeathPolicy> _deathPolicies = [];
    private readonly List<ILingerPolicy> _lingerPolicies = [];
    private string _module = "(none)";
    private bool _frozen;

    public AttributeSchema.Builder Attributes { get; } = new();
    public PacketRegistry.Builder Packets { get; } = new();

    internal void BeginModule(string name) => _module = name;

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

    public void AddDeathPolicy(IDeathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Refuse();
        _deathPolicies.Add(policy);
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

        var schema = new RecordSchema { Families = [.. _families], ChoiceSets = [.. _choices] };
        return new CoreRegistry(schema, Attributes.Build(), Packets.Build(), _tick.Build(),
                                new EquipSlotSet(_equipSlots), new OverheadBarSet(_overheadBars),
                                new DisplayFieldSet(_displayFields),
                                [.. _observers], [.. _deathPolicies],
                                [.. _lingerPolicies], modules, moduleNames);
    }

    private void Refuse()
    {
        if (_frozen)
            throw new CoreModuleException($"Module '{_module}' kept the builder and used it after loading finished.", _module);
    }
}
