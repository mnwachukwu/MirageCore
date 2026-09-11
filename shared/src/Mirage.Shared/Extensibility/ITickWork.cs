namespace Mirage.Shared.Extensibility;

/// <summary>
/// Something that runs on the game loop.
///
/// <para><b>Registered, not named.</b> The loop drives whatever is in its list, so a game adds work to
/// the tick without its own type appearing in Core's composition root — and Core's loop does not carry
/// a list of the systems some particular game happens to have.</para>
/// </summary>
public interface ITickWork
{
    /// <summary>For logs and for the loop's own timing readout.</summary>
    string Name { get; }

    /// <summary>How often this runs, in ticks. One is every tick; a larger number is every Nth, which
    /// is how work that does not need the full rate stays off the hot path.
    ///
    /// <para>A value below one is read as one.</para></summary>
    int EveryTicks => 1;

    /// <summary>Where this sits relative to other work on the same tick. Lower runs first; equal
    /// values run in registration order.</summary>
    int Order => 0;

    /// <summary>Does the work for this tick.</summary>
    /// <param name="tick">The tick number, counted from the loop's start. The clock
    /// <see cref="DeadlineClock.Tick"/> deadlines are measured against.</param>
    void Tick(long tick);
}

/// <summary>
/// The ordered set of <see cref="ITickWork"/> the loop drives.
///
/// <para>Built once and not changed after, so the loop iterates it without locking or copying.</para>
/// </summary>
public sealed class TickSchedule
{
    /// <summary>Nothing registered. What the loop runs when no game layer is loaded.</summary>
    public static readonly TickSchedule Empty = new(Array.Empty<ITickWork>());

    private readonly ITickWork[] _work;

    private TickSchedule(ITickWork[] work) => _work = work;

    public IReadOnlyList<ITickWork> Work => _work;

    /// <summary>Everything due on <paramref name="tick"/>, in order.</summary>
    public IEnumerable<ITickWork> DueOn(long tick)
    {
        foreach (var work in _work)
        {
            int every = Math.Max(1, work.EveryTicks);
            if (tick % every == 0) yield return work;
        }
    }

    /// <summary>Runs everything due on <paramref name="tick"/>.</summary>
    public void RunDue(long tick)
    {
        foreach (var work in DueOn(tick)) work.Tick(tick);
    }

    public sealed class Builder
    {
        private readonly List<ITickWork> _work = new();

        public Builder Add(ITickWork work)
        {
            ArgumentNullException.ThrowIfNull(work);
            _work.Add(work);
            return this;
        }

        /// <summary>Sorts by <see cref="ITickWork.Order"/>, keeping registration order within a rank —
        /// <see cref="Enumerable.OrderBy{T, TKey}(IEnumerable{T}, Func{T, TKey})"/> is stable, which is
        /// what makes equal orders mean "as registered" rather than "in some order".</summary>
        public TickSchedule Build() => new(_work.OrderBy(w => w.Order).ToArray());
    }
}
