namespace Mirage.Shared.Extensibility;

/// <summary>
/// What a game asks the client to show, for the moment it happens.
///
/// <para>🔴 <b>These are the only draws a game may call, and they are all the same kind of thing:</b> an
/// event that happened once, with nothing for a client to derive it from. Everything else a game does
/// sets state — an attribute, a timed state, a record — and the client decides what that looks like.
/// A sword swing is not state.</para>
///
/// <para><b>Machinery with no opinion about what caused it.</b> A crescent sweeping over a tile is a
/// sword, a claw, a thrown net, or a shop door opening; a burst is blood, sparks off an anvil, water, or
/// dust off a rockfall. Which one it is belongs to the game making the call, so Core carries a
/// genre it has never heard of.</para>
///
/// <para>⚠ <b>Weather is not here and is not a game's.</b> It runs on the world's own clock and every
/// client renders it from state it already holds, so there is nothing to ask for.</para>
///
/// <para>⚠ <b>Nothing here persists.</b> Anything that should still be there a minute later is a stain
/// rather than a particle, and goes through <see cref="IWorld.Stain"/>.</para>
/// </summary>
public enum GameEffect : byte
{
    /// <summary>A crescent sweeping over a body, oriented by the way it is facing.</summary>
    Sweep = 0,

    /// <summary>Something flying from one body to another, bursting on arrival.</summary>
    Throw = 1,

    /// <summary>A burst of droplets from a body, arcing down under gravity.</summary>
    Burst = 2,
}

/// <summary>
/// What a thrown thing looks like on its way.
///
/// <para>Describes the VISUAL and not what caused it, which is the whole reason a game can use these
/// without Core knowing what it is making.</para>
/// </summary>
public enum ProjectileStyle : byte
{
    /// <summary>A single bullet that homes on its target and bursts on arrival.</summary>
    Bolt = 0,

    /// <summary>A scattered cluster of motes that land spread around the target.</summary>
    Glitter = 1,

    /// <summary>A carried box, for something visibly changing hands.</summary>
    Parcel = 2,
}
