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
    /// deserialize; <see cref="AddPacketRoute"/> is what then delivers it somewhere.</summary>
    PacketRegistry.Builder Packets { get; }

    /// <summary>Record families this module adds, and the choice sets their fields draw from.</summary>
    void AddFamily(RecordFamily family);

    /// <inheritdoc cref="AddFamily"/>
    void AddChoiceSet(ChoiceSet choices);

    /// <summary>A place on a character where something can be worn. Declare none and nothing in this
    /// game is equippable, which is a perfectly ordinary thing for a game to be.</summary>
    void AddEquipSlot(EquipSlot slot);

    /// <summary>A value to show the player, read off a body's attributes wherever the named surface is
    /// drawn. Declare none and every surface stays as Core draws it, which is a game that tells the
    /// player nothing it invented.</summary>
    void AddDisplayField(DisplayField field);

    /// <summary>A row over a body's head, reading two of that body's attributes. Declare none and
    /// nothing is drawn over anyone, which is what a game with nothing to show at a glance wants.
    /// At most <see cref="OverheadBarSet.Max"/>.</summary>
    void AddOverheadBar(OverheadBar bar);

    /// <summary>Where this module's own packets go. Declare none and a command registered above
    /// deserializes and is delivered to nobody.</summary>
    void AddPacketRoute(IPacketRoute route);

    /// <summary>Work this module wants done on the tick.</summary>
    void AddTickWork(ITickWork work);

    /// <summary>Something to be told what happened in the world. Declare none and the engine runs
    /// exactly as it does now, telling nobody anything.</summary>
    void AddObserver(IWorldObserver observer);

    /// <summary>What this game says about dying — whether it happens, what it costs, where the body
    /// comes back. Declare none and <c>DeathSystem.Kill</c> moves the body and takes nothing.</summary>
    void AddDeathPolicy(IDeathPolicy policy);

    /// <summary>What this game says about a dropped connection — how long the body stays in the world
    /// before it is taken out. Declare none and a disconnect removes the player at once.</summary>
    void AddLingerPolicy(ILingerPolicy policy);
}
