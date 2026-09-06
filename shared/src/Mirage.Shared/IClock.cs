namespace Mirage.Shared;

/// <summary>
/// Wall-clock time, injectable so that time-dependent rules can be asserted instead of sampled.
///
/// <para>Every deadline the server owns is stored as a Unix second — PK-flag expiry, post-death
/// grace, mute and ban windows, mail maturity and its 30-day retention, market listing lifetime,
/// guild tax due dates. Reading <c>DateTimeOffset.UtcNow</c> at each point of use would leave those
/// rules untestable in practice: a test can assert that mail matures, but not that it matures at the
/// right moment, because it cannot move the clock.</para>
///
/// <para><b>This is not the game loop's clock.</b> <c>Environment.TickCount64</c> remains the
/// monotonic tick source, and is threaded through the tick paths as a <c>long now</c> parameter,
/// which is what makes AI and combat-timer behavior testable. <see cref="IClock"/> covers only the
/// calendar/wall-clock reads, which have no such parameter to ride on.</para>
/// </summary>
public interface IClock
{
    /// <summary>Now, as a Unix timestamp in seconds — the unit every persisted deadline uses.</summary>
    long UtcNowUnix { get; }

    /// <summary>Now in the server operator's LOCAL time. Used only where a calendar date or day of
    /// week is the rule (guild daily settlement, the weekly reset, season rollover), because those
    /// are meant to land on the operator's civil day rather than on a UTC boundary.</summary>
    DateTime LocalNow { get; }
}

/// <summary>The production clock: reads the machine clock on every access.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared instance — the type is stateless, so one is enough. Used as the default when
    /// a system is constructed without an explicit clock (tests that do not care about time, and any
    /// construction site predating the seam).</summary>
    public static readonly SystemClock Instance = new();

    public long UtcNowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public DateTime LocalNow => DateTime.Now;
}
