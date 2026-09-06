using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.World;

/// <summary>Client spell line-of-sight (mirrors the server's authoritative trace so the target-arrow color
/// never lies): a straight tile-line to the target is clear unless a Blocked tile or a closed Key door sits on
/// it. Uses the local player on the center map; world coords place center-local (0,0) at world (16,12).</summary>
[TestFixture]
public class ClientLineOfSightTests
{
    // Local player at center-local (5,5) => world (21,17). Targets share row y=5 so the trace is horizontal.
    static ClientState CenterState()
    {
        var s = new ClientState { MyIndex = 1, CenterMapNum = 1 };
        s.NeighborMapNums[1, 1] = 1;
        s.Me.X = 5;
        s.Me.Y = 5;
        return s;
    }

    // Target at center-local (10,5) => world (26,17). The horizontal line crosses local x 6,7,8,9 (row 5).
    const int TargetWX = 26, TargetWY = 17;

    [Test]
    public void HasClear_OpenPath_True()
        => Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(CenterState(), TargetWX, TargetWY), Is.True);

    [Test]
    public void HasClear_BlockedTileOnLine_False()
    {
        var s = CenterState();
        s.Map.EditTile(7, 5, t => t with { Type = TileType.Blocked });   // world (23,17) lies on the line
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY), Is.False);
    }

    // A closed Key door on the line blocks; opening it (TempTile) clears the shot.
    [Test]
    public void HasClear_KeyDoor_ClosedBlocks_OpenClears()
    {
        var s = CenterState();
        s.Map.EditTile(8, 5, t => t with { Type = TileType.Key });   // world (24,17) on the line
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY), Is.False, "closed door blocks");

        s.TempTile.Set(8, 5, (int)WorldLayer.Ground, true);   // ground door open
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY), Is.True, "open door clears");
    }

    // Two-layer world: obstacles are read on the SHOOTER'S layer — a fringe railing on the line blocks a
    // fringe-to-fringe shot but not a ground shot passing beneath it.
    [Test]
    public void HasClear_FringeWall_BlocksFringeShot_NotGroundShotBeneath()
    {
        var s = CenterState();
        s.Map.EditTile(7, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.Blocked } });   // a fringe railing on the line

        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY), Is.True,
            "a ground shot passes beneath a fringe wall");

        s.Me.Layer = WorldLayer.Fringe;
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY, WorldLayer.Fringe), Is.False,
            "a fringe shot is blocked by the fringe wall");
    }

    // Per-layer doors (§1b): a fringe-deck Key door blocks a fringe shot until its FRINGE door state opens —
    // opening the GROUND door at the same tile does nothing for the fringe shooter (independent door state).
    [Test]
    public void HasClear_FringeDoor_IsIndependentOfTheGroundDoorState()
    {
        var s = CenterState();
        s.Map.EditTile(8, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.Key } });   // a fringe-deck door on the line
        s.Me.Layer = WorldLayer.Fringe;

        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY, WorldLayer.Fringe), Is.False,
            "the closed fringe door blocks the fringe shot");

        s.TempTile.Set(8, 5, (int)WorldLayer.Ground, true);   // opening the GROUND door changes nothing up on the deck
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY, WorldLayer.Fringe), Is.False,
            "the ground door is independent — the fringe shot is still blocked");

        s.TempTile.Set(8, 5, (int)WorldLayer.Fringe, true);   // opening the FRINGE door clears it
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY, WorldLayer.Fringe), Is.True,
            "opening the fringe door clears the fringe shot");
    }

    // The arrow grays for a cross-layer target at range, but connects at a ramp foot (the "layer 1.5" reach),
    // mirroring the server's HasLineOfSight.
    [Test]
    public void HasClear_CrossLayer_GraysAtRange_ConnectsAtARampFoot()
    {
        var s = CenterState();
        // Distant fringe target from the ground → no connect.
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, TargetWX, TargetWY, WorldLayer.Fringe), Is.False,
            "no cross-layer reach at range");

        // A ramp on the adjacent tile (local (6,5) = world (22,17)), ground side Left so stepping Right mounts it:
        // a fringe target standing on it connects from the ground foot at (5,5).
        s.Map.EditTile(6, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, 22, 17, WorldLayer.Fringe), Is.True,
            "reaches a target on the adjacent ramp");
    }

    // A ramp is a physical block on a CROSS-LAYER spell line — a clean shot at a ramp target connects,
    // but a ramp mid-line blocks (can't cast through a ramp to a target behind/under it). Endpoints are excluded.
    [Test]
    public void HasClear_CrossLayer_BlockedByARampOnTheLine()
    {
        var s = CenterState();
        // A distant fringe target on row 5 stands ON a ramp (local (9,5) = world (25,17)) → the endpoints connect.
        s.Map.EditTile(9, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, 25, 17, WorldLayer.Fringe), Is.True,
            "a clear cross-layer line to a ramp target connects");

        // Drop a ramp mid-line (local (7,5) = world (23,17)) → it blocks the cross-layer cast.
        s.Map.EditTile(7, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
        Assert.That(ClientLineOfSight.HasClearFromLocalPlayer(s, 25, 17, WorldLayer.Fringe), Is.False,
            "a ramp on the line blocks the cross-layer cast");
    }
}
