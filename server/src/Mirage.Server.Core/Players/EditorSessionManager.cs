using Mirage.Shared;

namespace Mirage.Server.Core.Players;

/// <summary>Fixed pool of editor connection slots (1..<see cref="Constants.MaxEditorSessions"/>).
/// Slots are allocated once and recycled — disconnecting clears a slot's state instead of freeing it —
/// so a session index stays valid for the process's lifetime.</summary>
public sealed class EditorSessionManager
{
    // 1-based; indices 1..MaxEditorSessions; index 0 unused
    private readonly EditorSession[] _sessions;

    public EditorSessionManager()
    {
        _sessions = new EditorSession[Constants.MaxEditorSessions + 1];
        for (int i = 1; i <= Constants.MaxEditorSessions; i++)
            _sessions[i] = new EditorSession { Index = i };
    }

    /// <summary>The lowest-numbered disconnected slot, or null when every slot is in use.</summary>
    public EditorSession? FindOpenSlot()
    {
        for (int i = 1; i <= Constants.MaxEditorSessions; i++)
            if (!_sessions[i].IsConnected) return _sessions[i];
        return null;
    }

    /// <summary>The slot at <paramref name="index"/>, or null if it is out of range. Returns the slot
    /// whether or not it is connected, so callers must check the flags themselves.</summary>
    public EditorSession? GetSession(int index)
    {
        if (index < 1 || index > Constants.MaxEditorSessions) return null;
        return _sessions[index];
    }

    /// <summary>Release a slot: clears the connected and authenticated flags, the login and the session id,
    /// leaving the instance in place for reuse. Out-of-range indices are ignored.</summary>
    public void Disconnect(int index)
    {
        if (index < 1 || index > Constants.MaxEditorSessions) return;
        _sessions[index].IsConnected = false;
        _sessions[index].IsAuthenticated = false;
        _sessions[index].Login = "";
        _sessions[index].SessionId = "";
    }
}
