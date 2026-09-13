namespace Mirage.Shared.Extensibility;

/// <summary>
/// A tile on a map. Where something is, said in the only terms Core has.
///
/// <para>Plain integers, like <see cref="Respawn"/>: a narrower type would silently wrap a coordinate
/// past its width and name a tile nobody meant.</para>
/// </summary>
public readonly record struct WorldPlace(int Map, int X, int Y)
{
    /// <summary>Nowhere. The zero value, which is what a body that was not anywhere before comes from.</summary>
    public static WorldPlace Nowhere => default;

    /// <summary>True when this names a map.</summary>
    public bool IsSet => Map > 0;

    /// <summary>Whether two places are on the same map. The question a game asks before working out
    /// whether anything about a move mattered.</summary>
    public bool SameMapAs(in WorldPlace other) => Map == other.Map;

    public override string ToString() => IsSet ? $"map {Map} ({X},{Y})" : "nowhere";
}

/// <summary>
/// What a game is TOLD.
///
/// <para><b>The other half of the module seam.</b> Everything else a module declares is something it
/// hands the engine — record families, attribute keys, packets, equipment slots, work on the tick. This
/// is the direction back: the engine saying that something happened in the world it owns, so a game can
/// have a rule about it. Without this a module can describe a game and never play one.</para>
///
/// <para><b>Every method has a default</b>, so a module overrides only what it cares about. A game that
/// wants to know about nothing implements <see cref="Name"/> and stops.</para>
///
/// <para><b>These are reports, not requests.</b> Core has already done the thing by the time an observer
/// hears about it; nothing here can refuse or alter it. Where a game needs to say no — dying is the
/// one Core has today — the seam is a policy that is ASKED, not an observer that is told. An observer
/// that wants to act calls back into the engine like any other caller.</para>
///
/// <para><b>They run on the game thread, in the middle of the work that raised them.</b> So an observer
/// does its thinking and returns; anything slow belongs on the tick, which is what
/// <see cref="ITickWork"/> is for. An observer that throws is logged with its name and the others still
/// run, the same as a module's tick work — a game's bug does not stop the world.</para>
/// </summary>
public interface IWorldObserver
{
    /// <summary>What this observer is called, for the log line that names it when it throws.</summary>
    string Name { get; }

    /// <summary>A player is in the world and has been sent everything they need. Raised after the join
    /// completes, so the engine can be called back into from here.</summary>
    void OnPlayerJoined(EntityHandle who) { }

    /// <summary>A player has left. Raised while their record is still readable, so a game can take what
    /// it needs to persist before the slot is cleared.</summary>
    void OnPlayerLeft(EntityHandle who) { }

    /// <summary>A player walked one tile. The event a game builds encounters, triggers and step counters
    /// on, so it is raised on every accepted step and nothing is filtered out here.
    ///
    /// <para>A step may cross a map seam, in which case <paramref name="from"/> and <paramref name="to"/>
    /// name different maps and no warp is raised — walking over a border is a step.</para></summary>
    void OnPlayerMoved(EntityHandle who, in WorldPlace from, in WorldPlace to) { }

    /// <summary>A player was PUT somewhere rather than walking there: a warp tile, a death respawn, an
    /// admin command, the arrival at the end of a login.
    ///
    /// <para><paramref name="from"/> is <see cref="WorldPlace.Nowhere"/> when they were not in the world
    /// before, which is what joining looks like.</para></summary>
    void OnPlayerWarped(EntityHandle who, in WorldPlace from, in WorldPlace to) { }

    /// <summary>A pursuing NPC reached what it was chasing.
    ///
    /// <para><b>This is where a game's answer to "and then what" goes.</b> Core chases and arrives, and
    /// has nothing to do next: an attack, a conversation, a battle screen, a mugging and a footrace are
    /// all games' rules. Raised once when contact is MADE, not for every tick it is held.</para></summary>
    void OnContact(EntityHandle npc, EntityHandle quarry) { }

    /// <summary>A player used an item Core has no rule for.
    ///
    /// <para>Equipment is worn, a key opens a door, and those are the engine's. Everything else is a
    /// game's: what a potion restores, what a scroll teaches, what a rod does to the water in front of
    /// you. Core consumes nothing on this path — an observer that spends the item says so by taking
    /// it.</para></summary>
    void OnItemUsed(EntityHandle who, int itemNum, int invSlot) { }
}
