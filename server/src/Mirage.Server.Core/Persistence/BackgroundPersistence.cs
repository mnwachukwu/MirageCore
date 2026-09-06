using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// Tracks fire-and-forget persistence tasks so (a) faults get logged instead of
/// silently swallowed, and (b) graceful shutdown can await every queued write.
/// Used in place of the `_ = _persistence.X(...)` pattern at any write-through
/// call site (item drops, account logs, editor saves, etc.).
/// </summary>
public interface IBackgroundPersistence
{
    /// <summary>Queue a fire-and-forget persistence task. Faults are logged with
    /// <paramref name="operation"/>. The task is removed from the pending set
    /// when it completes (success or failure).</summary>
    void Run(Task task, string operation);

    /// <summary>Wait for every currently-queued task to complete. Called once
    /// during graceful shutdown so the process doesn't exit with pending writes.</summary>
    Task DrainAsync();
}

public sealed class BackgroundPersistence : IBackgroundPersistence
{
    private readonly ILogger<BackgroundPersistence> _logger;
    private readonly ConcurrentDictionary<Task, byte> _pending = new();

    public BackgroundPersistence(ILogger<BackgroundPersistence> logger) => _logger = logger;

    public void Run(Task task, string operation)
    {
        _pending[task] = 0;
        task.ContinueWith(t =>
        {
            _pending.TryRemove(t, out _);
            if (t.IsFaulted)
                _logger.LogError(t.Exception, "Background persistence failed: {Operation}", operation);
        }, TaskScheduler.Default);
    }

    public Task DrainAsync() => Task.WhenAll(_pending.Keys);
}
