namespace Mirage.Shared.Extensibility;

/// <summary>Which clock a <see cref="Deadline"/> is counted against.</summary>
public enum DeadlineClock : byte
{
    /// <summary>Unix seconds, as <see cref="IClock.UtcNowUnix"/> reads them. Survives a restart, so
    /// anything persisted uses this: a mute that outlasts a reboot, an invite that expires overnight.</summary>
    Utc = 0,

    /// <summary>Game ticks since the loop started. Does not survive a restart, and is the right clock
    /// for anything measured in frames rather than wall time — a cooldown, a movement credit, a door
    /// closing behind someone.</summary>
    Tick = 1,
}

/// <summary>
/// A moment something stops being true.
///
/// <para><b>One sentinel, and it is zero.</b> An unset deadline is <c>default</c>: no separate bool,
/// no -1, no <c>long.MaxValue</c> standing for "never". <see cref="IsSet"/> is the only question, and
/// a field that was never written answers it correctly.</para>
///
/// <para><b>The clock travels with the value.</b> A deadline read against the wrong clock is a number
/// that compares fine and means nothing — a tick count tested against Unix seconds is simply always in
/// the past. Carrying the clock is what lets <see cref="HasPassed"/> refuse the mismatch instead of
/// answering it.</para>
/// </summary>
/// <param name="At">When it lands, on <paramref name="Clock"/>. Zero means unset.</param>
/// <param name="Clock">Which clock <paramref name="At"/> is counted against.</param>
public readonly record struct Deadline(long At, DeadlineClock Clock)
{
    /// <summary>Not set. The zero value, so an unwritten field is already this.</summary>
    public static Deadline None => default;

    /// <summary>True when this names a moment at all.</summary>
    public bool IsSet => At != 0;

    /// <summary>A moment on the wall clock, in Unix seconds.</summary>
    public static Deadline AtUtc(long utcSeconds) => new(utcSeconds, DeadlineClock.Utc);

    /// <summary><paramref name="seconds"/> from <paramref name="nowUtc"/>. A non-positive span yields
    /// <see cref="None"/>: a window that closes the moment it opens is not a window.</summary>
    public static Deadline InSeconds(long nowUtc, long seconds)
        => seconds > 0 ? new Deadline(nowUtc + seconds, DeadlineClock.Utc) : None;

    /// <summary>A moment on the tick clock.</summary>
    public static Deadline AtTick(long tick) => new(tick, DeadlineClock.Tick);

    /// <summary><paramref name="ticks"/> from <paramref name="nowTick"/>. A non-positive span yields
    /// <see cref="None"/>.</summary>
    public static Deadline InTicks(long nowTick, long ticks)
        => ticks > 0 ? new Deadline(nowTick + ticks, DeadlineClock.Tick) : None;

    /// <summary>True when <paramref name="now"/> has reached or gone past this.
    ///
    /// <para>An unset deadline has not passed — nothing is waiting on it. A <paramref name="clock"/>
    /// that is not this deadline's also answers false, so a caller reading the wrong clock sees a
    /// deadline that never lands rather than one that landed immediately.</para></summary>
    public bool HasPassed(long now, DeadlineClock clock)
        => IsSet && clock == Clock && now >= At;

    /// <summary>True while this is set and still ahead of <paramref name="now"/>.</summary>
    public bool IsPending(long now, DeadlineClock clock)
        => IsSet && clock == Clock && now < At;

    /// <summary>How much is left, never negative. Zero once it has passed, and zero when unset.</summary>
    public long Remaining(long now, DeadlineClock clock)
        => IsPending(now, clock) ? At - now : 0;
}

/// <summary>
/// A deadline plus the one bit of memory needed to notice the moment it passes.
///
/// <para><b>The falling edge is the point.</b> Code that only asks "is it still running" cannot tell
/// the first tick after expiry from the thousandth, so cleanup that belongs to expiry either never
/// happens or happens every tick forever. <see cref="Poll"/> answers both questions at once and
/// reports the edge exactly once.</para>
///
/// <para>A mutable struct, so it is stored in a field on the thing it describes and polled in place.
/// Copy one and the copy keeps its own memory of whether it has fired.</para>
/// </summary>
public struct TimedFlag
{
    private Deadline _until;
    private bool _wasPending;

    /// <summary>Starts, restarts, or clears the window. Setting <see cref="Deadline.None"/> clears it
    /// without reporting an edge — a flag that was cancelled did not expire.</summary>
    public void Set(Deadline until)
    {
        _until = until;
        _wasPending = until.IsSet;
    }

    public void Clear() => Set(Deadline.None);

    /// <summary>Whether the window is currently open, without disturbing the edge.</summary>
    public readonly bool IsActive(long now, DeadlineClock clock) => _until.IsPending(now, clock);

    public readonly Deadline Until => _until;

    /// <summary>Reads the flag and reports whether this is the call that saw it expire.</summary>
    /// <param name="expiredNow">True on exactly one call: the first after the deadline passed. False
    /// forever after, and false for a flag that was never set.</param>
    /// <returns>Whether the window is still open.</returns>
    public bool Poll(long now, DeadlineClock clock, out bool expiredNow)
    {
        bool pending = _until.IsPending(now, clock);
        expiredNow = _wasPending && !pending;
        _wasPending = pending;
        return pending;
    }
}
