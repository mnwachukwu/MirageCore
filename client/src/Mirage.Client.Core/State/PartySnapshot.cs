using Mirage.Shared;

namespace Mirage.Client.Core.State;

/// <summary>
/// Latest server-pushed snapshot of the local player's party partner, plus per-frame lerped
/// display values consumed by the party overlay.  <see cref="Active"/> is false (empty Name)
/// when the player has no partner — the overlay treats that as "do not draw."
/// </summary>
public sealed class PartySnapshot
{
    public int Index;
    public string Name = "";
    public int MapNum, X, Y;
    public bool ShowAsMarked;
    public AdminLevel Access;
    // Server's CombatExpiresAt converted to the local client's TickCount64 clock at receive
    // time; the existing IsInCombat(stamp, now) < 10s test in RenderCommandBuilder works as-is.
    public long LastCombatTickMs;

    /// <summary>How full each declared bar is for the partner, in declaration order, as the server read
    /// them. Empty until the first snapshot arrives, and for a game that declares no bars.
    ///
    /// <para>Sent rather than derived: a partner is usually somebody this client cannot see, so it has
    /// never been told their attributes and computing the rows here would mean reading a body it does
    /// not hold.</para></summary>
    public IReadOnlyList<float> Bars = [];

    // Animated display values — initialized -1f so the first push snaps rather than lerps from 0.

    public bool Active => !string.IsNullOrEmpty(Name);

    public void Clear()
    {
        Index = 0;
        Name = "";
        MapNum = X = Y = 0;
        ShowAsMarked = false;
        Access = AdminLevel.Player;
        Bars = [];
        LastCombatTickMs = 0;
    }
}
