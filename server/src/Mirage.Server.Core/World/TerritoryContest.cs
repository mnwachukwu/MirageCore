using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.World;

/// <summary>The phase of a live territory war-night contest: the 10-min setup ramp, the 20-min
/// king-of-the-hill contest, then the 10-min cooldown before it finalizes.</summary>
public enum ContestPhase { Setup, Contest, Cooldown }

/// <summary>One capture point in a live contest — its world position, current owner + capture meter, and the
/// guild currently pushing it. Runtime-only (regenerated each war night).</summary>
public sealed class ContestPoint
{
    public string Label = "";
    public int Map, X, Y;
    // Two-layer world: a point placed on a bridge tile lives on the Fringe (deck) — you hold it by standing ON
    // the bridge, not on the ground beneath.  Credit is gated to this layer.
    public WorldLayer Layer;
    public int OwnerGuild;        // 0 = neutral
    public int Meter;            // signed [-Full, +Full]; -Full = owner secure, +Full flips to the challenger
    public int ChallengerGuild;  // guild currently pushing the meter up (0 = none)
}

/// <summary>A live territory contest for one war night. Held in memory only — a mid-contest
/// restart abandons it (the territory keeps its pre-contest owner; challengers re-contest next war night).</summary>
public sealed class TerritoryContest
{
    public int TerritoryIndex;
    public int DefenderGuild;                       // 0 when the contest is over unclaimed land
    public ContestPhase Phase;
    public long PhaseEndUtc;
    public List<ContestPoint> Points = new();
    public Dictionary<int, long> Scores = new();    // KotH score per guild
    public HashSet<int> Participants = new();        // the defender (if any) + all challengers
    // Every map of the territory on one tile grid, built once at start. The layout cannot change while the
    // contest runs, and a client needs it to place a point on a map it has not loaded.
    public List<ContestMapView> Layout = new();
}
