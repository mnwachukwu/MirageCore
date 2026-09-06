using Microsoft.Extensions.Logging;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// The single serialized writer for account files. The game runs on one thread (see GameLoop) and
/// account file I/O must never block it, so every write is handed here to run on a background task,
/// <b>chained per-login</b> so all writes to one account file are serialized — concurrent character
/// saves and field mutations can never lose an update. Each write is a <b>load-merge</b>: it loads the
/// current file, applies only its own change, and saves, so it never clobbers another writer's fields.
///
/// Two entry points:
/// <list type="bullet">
/// <item><see cref="SaveCharInBackground"/> — persist one character + the shared bank from snapshots.</item>
/// <item><see cref="MutateAccountInBackground"/> — apply an arbitrary account-field change (mute, kick,
/// guild membership, ...). This is the ONE correct way to change an account-level field: a direct
/// <c>LoadAccountAsync → set → SaveAccountAsync</c> elsewhere would race this chain and lose updates.</item>
/// </list>
///
/// Contract: anything passed in must NOT be mutated after the call returns — pass a
/// <see cref="PlayerRecord.Clone"/> (still-online save) or a detached record, and have a mutation
/// closure capture <i>values</i>, not live game state (the closure runs later, off-thread).
/// </summary>
public sealed class PlayerSaver
{
    private readonly IPersistenceService _persistence;
    private readonly ILogger<PlayerSaver> _logger;

    // Per-login chain of pending writes: each write awaits the previous write to the SAME account so
    // two writes never race the same file (the latest change lands last). Guarded by _chainLock
    // because callers span the game thread (character autosave) AND background I/O continuations
    // (admin mute/kick, login). The lock is held only to swap the chain task, never across I/O.
    private readonly Dictionary<string, Task> _writeChains = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _chainLock = new();

    public PlayerSaver(IPersistenceService persistence, ILogger<PlayerSaver> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    /// <summary>Persist one character record + the account-shared bank, from snapshots the caller took
    /// on the game thread. Load-merges, so the account's other characters and fields are preserved.</summary>
    public void SaveCharInBackground(string login, int charNum, PlayerRecord snapshot, PlayerInvSlot[] bankSnapshot) =>
        Chain(login, account => { account.Chars[charNum] = snapshot; account.Bank = bankSnapshot; });

    /// <summary>Like <see cref="SaveCharInBackground"/> but RETURNS the write task, so a caller can await it —
    /// used by the trade swap to delete its write-ahead journal only once both participants are durably saved.</summary>
    public Task SaveCharTracked(string login, int charNum, PlayerRecord snapshot, PlayerInvSlot[] bankSnapshot) =>
        Chain(login, account => { account.Chars[charNum] = snapshot; account.Bank = bankSnapshot; });

    /// <summary>Apply an account-field change on the per-login chain — the single race-free way to
    /// mutate an account-level field. <paramref name="mutate"/> runs later on a background thread, so
    /// it must capture values, not live game state.</summary>
    public void MutateAccountInBackground(string login, Action<AccountRecord> mutate) =>
        Chain(login, mutate);   // discards the task; fire-and-forget

    /// <summary>The same chained write, awaited.
    ///
    /// <para>For a caller that has to READ THE ACCOUNT BACK afterwards. The fire-and-forget form only
    /// enqueues, so anything that re-reads the file immediately sees the value it just changed — which is
    /// how lifting a kick reported success while the moderation page went on showing it as live.</para></summary>
    public Task MutateAccountAsync(string login, Action<AccountRecord> mutate) => Chain(login, mutate);

    /// <summary>Await every pending per-login write. Call at shutdown — after the game loop has
    /// stopped, so nothing new is enqueued — to guarantee all account writes hit disk before exit.</summary>
    public Task DrainAsync()
    {
        Task[] pending;
        lock (_chainLock) { pending = _writeChains.Values.ToArray(); }
        return Task.WhenAll(pending);
    }

    // Chain a load-merge-save after any prior write to this login. The mutate delegate applies the
    // caller's change to the freshly-loaded account; everything else is preserved from disk. Returns the
    // enqueued task so a caller that needs durability confirmation (the trade journal) can await it.
    private Task Chain(string login, Action<AccountRecord> mutate)
    {
        lock (_chainLock)
        {
            Task prior = _writeChains.TryGetValue(login, out var t) ? t : Task.CompletedTask;

            async Task WriteAfterPrior()
            {
                // Wait for the previous write to THIS account first (its own failure is already
                // logged) so two writes never race the same file; the latest change wins.
                try { await prior.ConfigureAwait(false); }
                catch { /* prior write failed and logged; don't break the chain */ }

                try
                {
                    var account = await _persistence.LoadAccountAsync(login).ConfigureAwait(false);
                    if (account is not null)
                    {
                        mutate(account);
                        await _persistence.SaveAccountAsync(account).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background account write failed for {Login}", login);
                }
            }

            var task = Task.Run(WriteAfterPrior);
            _writeChains[login] = task;
            return task;
        }
    }
}
