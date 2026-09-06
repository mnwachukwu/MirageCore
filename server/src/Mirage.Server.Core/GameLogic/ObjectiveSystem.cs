using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// The shared objective-tracking kernel. Holds the set of active <see cref="Objective"/>s and advances
/// them from game events; the mob-kill hook (<see cref="RecordNpcKill"/>, called from
/// <see cref="CombatSystem"/>'s kill path) is the only event wired in. It is deliberately scope-agnostic:
/// each registration carries a predicate deciding which kill contributors count toward it and a reward
/// callback fired on completion, so the guild-quest layer and the player-quest system both reuse it
/// without the kernel knowing about either. All access is on the single game thread (no locking), like
/// every other system.
/// </summary>
public sealed class ObjectiveSystem
{
    private readonly List<Registration> _active = [];

    private sealed class Registration
    {
        public Registration(Objective objective, Func<int, bool> countsContributor, Action onCompleted, Action? onAdvanced)
        {
            Objective = objective;
            CountsContributor = countsContributor;
            OnCompleted = onCompleted;
            OnAdvanced = onAdvanced;
        }

        public Objective Objective { get; }
        public Func<int, bool> CountsContributor { get; }
        public Action OnCompleted { get; }
        // Optional: fires whenever Progress changes (including the completing hit), BEFORE OnCompleted — lets a
        // customer surface live "3/5" progress + persist it. Null for callers that only care about completion.
        public Action? OnAdvanced { get; }
        // Set when the objective completes or the caller stops it; swept lazily on the next kill so a
        // completion callback can freely Track/Stop without disturbing an in-flight walk.
        public bool Canceled { get; set; }
    }

    /// <summary>Handle to an objective being tracked; call <see cref="Stop"/> to stop tracking it before
    /// it completes (e.g. a guild quest expiring or being abandoned). Completed objectives untrack
    /// themselves, so a handle need only be stopped for an early cancel.</summary>
    public sealed class Handle
    {
        // Wraps the stop action rather than the private Registration, so this public type's signature
        // stays free of the internal impl detail; the closure captures the registration in Track's body.
        private readonly Action _stop;
        internal Handle(Action stop) => _stop = stop;
        public void Stop() => _stop();
    }

    /// <summary>Count of objectives currently being tracked (excludes ones canceled but not yet swept).</summary>
    public int ActiveCount => _active.Count(r => !r.Canceled);

    /// <summary>Begin tracking <paramref name="objective"/>. <paramref name="countsContributor"/> is
    /// asked, per kill, whether a given contributor's player index counts toward this objective (e.g.
    /// "is this player's account in guild G"); <paramref name="onCompleted"/> fires once, on the game
    /// thread, the moment the objective completes, after which it is auto-untracked. Optional
    /// <paramref name="onAdvanced"/> fires on EVERY progress change (including the completing hit, just
    /// before onCompleted) so a customer can surface live progress and persist it.</summary>
    public Handle Track(Objective objective, Func<int, bool> countsContributor, Action onCompleted, Action? onAdvanced = null)
    {
        var reg = new Registration(objective, countsContributor, onCompleted, onAdvanced);
        _active.Add(reg);
        return new Handle(() => reg.Canceled = true);
    }

    /// <summary>Mob-kill progress hook (called from the kill path). Advances every tracked Kill
    /// objective that (a) targets <paramref name="npcNum"/> and (b) at least one of the kill's
    /// <paramref name="contributors"/> counts toward — firing its reward and untracking it if the kill
    /// completes it. One kill advances a matching objective by exactly 1, regardless of how many of its
    /// contributors count, and credits every distinct objective the kill matches (so two guilds sharing
    /// a kill each get progress).</summary>
    public void RecordNpcKill(int npcNum, IReadOnlyCollection<int> contributors)
    {
        if (contributors.Count == 0 || _active.Count == 0) return;
        // Iterate a snapshot so a completion callback may Track/Stop objectives without disturbing this
        // walk; mark completed registrations for the sweep below rather than mutating mid-loop.
        foreach (var reg in _active.ToArray())
        {
            if (reg.Canceled || !CountsAnyContributor(reg, contributors)) continue;
            int before = reg.Objective.Progress;
            bool completed = reg.Objective.TryAdvance(ObjectiveKind.Kill, npcNum);
            if (reg.Objective.Progress != before)
                reg.OnAdvanced?.Invoke();   // progress moved (incl. the completing hit) — surface live progress
            if (completed)
            {
                reg.Canceled = true;   // one-shot: mark before the callback so a re-entrant kill can't double-fire
                reg.OnCompleted();
            }
        }
        _active.RemoveAll(r => r.Canceled);
    }

    private static bool CountsAnyContributor(Registration reg, IReadOnlyCollection<int> contributors)
    {
        foreach (int i in contributors)
            if (reg.CountsContributor(i)) return true;
        return false;
    }
}
