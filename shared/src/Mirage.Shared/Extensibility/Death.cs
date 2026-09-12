namespace Mirage.Shared.Extensibility;

/// <summary>
/// One body reaching the end of its turn in the world.
///
/// <para><b>Core knows that bodies stop and come back; it does not know why.</b> Hit points, hunger,
/// drowning, a trap, an arrest, being voted off — every one of those is a game's rule, and none of them
/// is in here. What is in here is the fact and its two parties, which is what the engine needs to move a
/// body out of play and put it back.</para>
///
/// <para><b>The cause is a localization key, not a sentence</b> — the same reason <see cref="Refusal"/>
/// carries one. Empty when nothing worth announcing caused it.</para>
/// </summary>
/// <param name="Who">The body that died.</param>
/// <param name="Killer">Who brought it about, or <see cref="EntityHandle.None"/> when nobody did.</param>
/// <param name="CauseKey">A localization key naming the cause, or empty.</param>
public readonly record struct Death(EntityHandle Who, EntityHandle Killer, string CauseKey)
{
    /// <summary>True when something else brought this about, rather than the world itself.</summary>
    public bool WasKilled => Killer.IsSet;
}

/// <summary>
/// Where a body comes back.
///
/// <para>The zero value names nowhere, which is how a policy says "wherever Core would have put it"
/// rather than having to know the answer. Plain integers so it can name any tile on any map: a narrower
/// type would silently wrap a coordinate past its width and put somebody somewhere nobody chose.</para>
/// </summary>
public readonly record struct Respawn(int Map, int X, int Y)
{
    /// <summary>Nowhere in particular. The zero value.</summary>
    public static Respawn Default => default;

    /// <summary>True when this names a place.</summary>
    public bool IsSet => Map > 0;
}

/// <summary>
/// What a game says about dying.
///
/// <para>Every method has a default, so a module overrides only the question it has an opinion about and
/// a game with no notion of death implements none of this at all. Several policies may be loaded; they
/// are asked in the order their modules were configured.</para>
/// </summary>
public interface IDeathPolicy
{
    /// <summary>Whether it happens. Asked before anything is taken or moved, so a policy can refuse —
    /// a last stand, a revive, a safe zone — and leave the body exactly as it was.
    ///
    /// <para>Every policy must allow it. The first refusal stops the death and is the answer.</para></summary>
    Refusal MayDie(in Death death) => Refusal.Allow;

    /// <summary>What it costs. Called once the death is settled and before the body is moved, so a
    /// policy that sheds inventory is dropping it where the body fell.</summary>
    void OnDied(in Death death) { }

    /// <summary>Where the body comes back. <see cref="Respawn.Default"/> leaves the answer to Core,
    /// which is the home this world already knows. The first policy naming a place wins.</summary>
    Respawn RespawnFor(in Death death) => Respawn.Default;
}
