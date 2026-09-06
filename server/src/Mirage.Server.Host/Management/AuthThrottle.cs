using Mirage.Shared;
using System.Collections.Concurrent;

namespace Mirage.Server.Host.Management;

/// <summary>
/// Per-address failure limit for the management port, so the token cannot be guessed by volume.
///
/// <para>Failures inside a window count toward a lockout; a slow drip does not accumulate. Records are
/// pruned once they go stale, and a sweep at the cap keeps a spray across many addresses from growing
/// this without bound.</para>
/// </summary>
public sealed class AuthThrottle(IClock? clock = null)
{
    /// <summary>Failures allowed before an address is locked out.</summary>
    public const int MaxFailures = 5;

    /// <summary>How long a locked-out address stays locked out, and how long a failure counts toward the
    /// next lockout. One window for both: what a lockout means is "too many failures this recently".</summary>
    public const long LockoutSeconds = 300;

    /// <summary>Addresses remembered at once. Past this, stale records are swept before a new one is
    /// taken — a bound on memory, not on how many operators can attach.</summary>
    private const int MaxTrackedAddresses = 1024;

    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public int Failures;
        public long LastFailureUnix;
        public long LockedUntilUnix;
    }

    /// <summary>True when <paramref name="address"/> is currently locked out.</summary>
    public bool IsLockedOut(string address)
    {
        if (!_entries.TryGetValue(address, out var entry)) return false;

        lock (entry)
        {
            // Counting failures is not being locked out. Reading must not disturb the count, or the
            // per-connection check would reset it and the limit would never be reached.
            if (entry.LockedUntilUnix == 0) return false;
            if (_clock.UtcNowUnix < entry.LockedUntilUnix) return true;
        }

        // The lockout has run out. Drop the record rather than resetting it: an address with nothing
        // against it should cost nothing to remember.
        _entries.TryRemove(address, out _);
        return false;
    }

    /// <summary>Records a failed attempt. Returns true if that attempt was the one that locked the
    /// address out.</summary>
    public bool RecordFailure(string address)
    {
        long now = _clock.UtcNowUnix;
        if (_entries.Count >= MaxTrackedAddresses && !_entries.ContainsKey(address)) Sweep(now);

        var entry = _entries.GetOrAdd(address, _ => new Entry());
        lock (entry)
        {
            // A failure older than the window is not evidence of guessing, so counting starts over
            // rather than a wrong password a week ago costing someone their next attempt.
            if (now - entry.LastFailureUnix > LockoutSeconds) entry.Failures = 0;
            entry.LastFailureUnix = now;
            entry.Failures++;

            if (entry.Failures < MaxFailures) return false;
            entry.Failures = 0;
            entry.LockedUntilUnix = now + LockoutSeconds;
            return true;
        }
    }

    /// <summary>Clears an address's history after it authenticates.</summary>
    public void RecordSuccess(string address) => _entries.TryRemove(address, out _);

    private void Sweep(long now)
    {
        foreach (var (address, entry) in _entries)
        {
            bool stale;
            lock (entry)
            {
                stale = now >= entry.LockedUntilUnix && now - entry.LastFailureUnix > LockoutSeconds;
            }
            if (stale) _entries.TryRemove(address, out _);
        }
    }
}
