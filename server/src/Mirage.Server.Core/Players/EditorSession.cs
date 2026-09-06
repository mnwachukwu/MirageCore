using Mirage.Shared;

namespace Mirage.Server.Core.Players;

/// <summary>One editor connection slot. Instances are pre-allocated by
/// <see cref="EditorSessionManager"/> and reused, so a slot is "free" by virtue of its flags rather
/// than by being null.</summary>
public sealed class EditorSession
{
    /// <summary>1-based slot number; fixed for the lifetime of the manager.</summary>
    public int Index { get; set; }
    /// <summary>Account name once authenticated; blank while free or unauthenticated.</summary>
    public string Login { get; set; } = "";
    /// <summary>Identifies this occupancy of the slot, minted at login and cleared on disconnect. Slots are
    /// recycled, so <see cref="Index"/> alone would let a new session inherit the last one's identity — and a
    /// lock is a fact about a connection, not about the account or the slot.</summary>
    public string SessionId { get; set; } = "";
    /// <summary>Access level of the authenticated account, which gates the editor sections.</summary>
    public AdminLevel AdminLevel { get; set; }
    /// <summary>Whether credentials have been accepted. Connected but unauthenticated is a real state —
    /// the socket is open while the login is still in flight.</summary>
    public bool IsAuthenticated { get; set; }
    /// <summary>The language this editor asked for at login. A notice the server sends back is resolved in
    /// it, the same as the login message: the rules being reported are the game's, and the editor has no
    /// vocabulary for them.</summary>
    public string Locale { get; set; } = "";
    /// <summary>Whether the slot is in use; this is what <see cref="EditorSessionManager.FindOpenSlot"/> tests.</summary>
    public bool IsConnected { get; set; }
}
