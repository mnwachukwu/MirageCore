using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.Services;

/// <summary>
/// Who is holding which record, as this session last heard it.
///
/// <para>The server owns the truth and sends the whole table whenever it moves, so this is a cache and never
/// a decision: it drives what the lists show and what the editors refuse, and a save the server disagrees
/// with is still refused there.</para>
///
/// <para>A lock belongs to a SESSION, not to an account. Another editor window signed in as you is another
/// set of unsaved changes, and locks you out of the record exactly as a colleague would — so every "is this
/// mine?" test here is on <see cref="MySession"/>, and <see cref="MyLogin"/> is only ever used to word what
/// the reader is shown.</para>
///
/// <para>Empty while offline. Locks are about other sessions, and offline there are none.</para>
/// </summary>
public sealed class EditorLockState
{
    private Dictionary<(string Section, int Num), (string Login, string Session)> _held = [];

    /// <summary>The account this session is signed in as. For display only.</summary>
    public string MyLogin { get; set; } = "";

    /// <summary>The id the server gave this connection at login. What tells this session's locks from every
    /// other session's, including another window signed in as the same account.</summary>
    public string MySession { get; set; } = "";

    /// <summary>Raised after the table changes, on whatever thread the packet arrived on — subscribers that
    /// touch view-model state marshal it themselves.</summary>
    public event Action? Changed;

    public void Apply(EditorLocksPacket p)
    {
        _held = p.Locks.ToDictionary(h => (h.Section, h.Num), h => (h.Login, h.Session));
        Changed?.Invoke();
    }

    /// <summary>Drops everything — used on disconnect, where every lock this session knew about belongs to a
    /// conversation that is over.</summary>
    public void Clear()
    {
        if (_held.Count == 0 && MyLogin.Length == 0 && MySession.Length == 0) return;
        _held = [];
        MyLogin = "";
        MySession = "";
        Changed?.Invoke();
    }

    /// <summary>The account holding a record, or null when nobody is.</summary>
    public string? HolderOf(string section, int num) =>
        _held.TryGetValue((section, num), out var h) ? h.Login : null;

    /// <summary>Another SESSION has it. What greys a row out and refuses an edit — a lock this session took
    /// is just its own unsaved work, and must never lock it out of it.</summary>
    public bool IsHeldByOther(string section, int num) =>
        _held.TryGetValue((section, num), out var h)
        && !string.Equals(h.Session, MySession, StringComparison.Ordinal);

    /// <summary>Held by another window signed in as you. Reads as a conflict like any other, but saying so
    /// with the account name alone would name the reader — so the label says which it is.</summary>
    public bool IsHeldByMyAccountElsewhere(string section, int num) =>
        IsHeldByOther(section, num)
        && _held.TryGetValue((section, num), out var h)
        && string.Equals(h.Login, MyLogin, StringComparison.OrdinalIgnoreCase);
}
