namespace Mirage.Shared.Extensibility;

/// <summary>
/// What a game hands the engine: its record families, its attribute keys, its packets, its work on the
/// tick, and what it wants to be told about.
///
/// <para><b>Everything a game declares arrives through one of these.</b> A module is asked to describe
/// itself once, before the world loads, and is not consulted again — so the registries every subsystem
/// reads are complete and immutable by the time anything runs, and no part of Core has to cope with a
/// family or a key appearing halfway through a session.</para>
///
/// <para>Several modules may be loaded, and they are configured in the order given. Two modules
/// claiming the same family id, attribute key, or packet command is an error at startup rather than a
/// silent last-one-wins.</para>
/// </summary>
public interface ICoreModule
{
    /// <summary>What this module is called, for logs and for the error that names a collision.</summary>
    string Name { get; }

    /// <summary>Declares everything this module adds.</summary>
    void Configure(ICoreBuilder builder);
}

/// <summary>What a module declares into. Handed to <see cref="ICoreModule.Configure"/> and not valid
/// after it returns.</summary>
public interface ICoreBuilder
{
    /// <summary>Attribute keys this module wants synced or labeled.</summary>
    AttributeSchema.Builder Attributes { get; }

    /// <summary>Packet commands this module can read.</summary>
    PacketRegistry.Builder Packets { get; }

    /// <summary>Record families this module adds, and the choice sets their fields draw from.</summary>
    void AddFamily(RecordFamily family);

    /// <inheritdoc cref="AddFamily"/>
    void AddChoiceSet(ChoiceSet choices);

    /// <summary>A place on a character where something can be worn. Declare none and nothing in this
    /// game is equippable, which is a perfectly ordinary thing for a game to be.</summary>
    void AddEquipSlot(EquipSlot slot);

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
