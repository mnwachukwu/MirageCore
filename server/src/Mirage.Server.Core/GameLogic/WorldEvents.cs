using Microsoft.Extensions.Logging;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Telling every observer what just happened.
///
/// <para><b>One of these, injected wherever an event is raised.</b> A system that raises an event names
/// this and not the observers, so a game module is the only thing that ever appears in the list — and
/// the systems stay ignorant of whether anybody is listening.</para>
///
/// <para><b>An engine with no game loaded holds none, and every method returns on its first line.</b>
/// That matters because the busiest of these runs on every step every player takes: the cost of a seam
/// nobody uses has to be a length check, not a loop over an empty array with a try around it.</para>
///
/// <para><b>An observer that throws is logged with its name and the rest still run</b>, the same answer
/// the loop gives a module's tick work. A game's bug is a game's bug; it does not stop the world for
/// everyone else in it.</para>
/// </summary>
public sealed class WorldEvents
{
    /// <summary>Nobody is listening. What an engine with no module loaded raises into.</summary>
    public static readonly WorldEvents None = new(CoreRegistry.CoreOnly, null);

    private readonly IWorldObserver[] _observers;
    private readonly ILogger? _logger;

    public WorldEvents(CoreRegistry registry, ILogger<WorldEvents>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _observers = [.. registry.Observers];
        _logger = logger;
    }

    /// <summary>Whether anything is listening at all. Worth asking before building an argument that
    /// costs something to produce.</summary>
    public bool Any => _observers.Length > 0;

    public void PlayerJoined(int index)
    {
        if (_observers.Length == 0) return;
        var who = EntityHandle.ForPlayer(index);
        foreach (var observer in _observers)
        {
            try { observer.OnPlayerJoined(who); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnPlayerJoined), ex); }
        }
    }

    public void PlayerLeft(int index)
    {
        if (_observers.Length == 0) return;
        var who = EntityHandle.ForPlayer(index);
        foreach (var observer in _observers)
        {
            try { observer.OnPlayerLeft(who); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnPlayerLeft), ex); }
        }
    }

    public void PlayerMoved(int index, WorldPlace from, WorldPlace to)
    {
        if (_observers.Length == 0) return;
        var who = EntityHandle.ForPlayer(index);
        foreach (var observer in _observers)
        {
            try { observer.OnPlayerMoved(who, in from, in to); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnPlayerMoved), ex); }
        }
    }

    public void PlayerWarped(int index, WorldPlace from, WorldPlace to)
    {
        if (_observers.Length == 0) return;
        var who = EntityHandle.ForPlayer(index);
        foreach (var observer in _observers)
        {
            try { observer.OnPlayerWarped(who, in from, in to); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnPlayerWarped), ex); }
        }
    }

    public void Contact(EntityHandle npc, EntityHandle target)
    {
        if (_observers.Length == 0) return;
        foreach (var observer in _observers)
        {
            try { observer.OnContact(npc, target); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnContact), ex); }
        }
    }

    public void NpcSpawned(EntityHandle npc)
    {
        if (_observers.Length == 0) return;
        foreach (var observer in _observers)
        {
            try { observer.OnNpcSpawned(npc); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnNpcSpawned), ex); }
        }
    }

    public void ItemUsed(int index, int itemNum, int invSlot)
    {
        if (_observers.Length == 0) return;
        var who = EntityHandle.ForPlayer(index);
        foreach (var observer in _observers)
        {
            try { observer.OnItemUsed(who, itemNum, invSlot); }
            catch (Exception ex) { Faulted(observer, nameof(IWorldObserver.OnItemUsed), ex); }
        }
    }

    private void Faulted(IWorldObserver observer, string method, Exception ex) =>
        _logger?.LogError(ex, "Observer {Observer} threw from {Method}.", observer.Name, method);
}
