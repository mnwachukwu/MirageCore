using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>Per-frame movement interpolation: XOffset/YOffset advance toward 0 at the walk/run rate, then the
/// Moving flag demotes to None when the tile-step settles. The LOCAL player's RUN rate is SPD-scaled (the
/// gap-control mechanic); walk and everyone-else's run use the flat baseline.</summary>
[TestFixture]
public class MovementProcessorTests
{
    [Test]
    public void Process_WalkingPlayer_OffsetAdvancesTowardZero_ThenSettles()
    {
        var s = new ClientState { MyIndex = 1 };
        var me = s.Players[1];
        me.Name = "Me";
        me.XOffset = -Constants.PicX;   // -32, just stepped right
        me.Moving = MovementType.Walking;

        // step = PicX / BaseWalkMsPerTile(400) * 200 = 16
        MovementProcessor.Process(s, 200f);
        Assert.That(me.XOffset, Is.EqualTo(-16f).Within(1e-3f));
        Assert.That(me.Moving, Is.EqualTo(MovementType.Walking), "still mid-step");

        MovementProcessor.Process(s, 200f);   // completes the tile
        Assert.That(me.XOffset, Is.EqualTo(0f));
        Assert.That(me.Moving, Is.EqualTo(MovementType.None), "a settled step demotes Moving to None");
    }

    // Same elapsed time (140ms): a max-SPD local player finishes the run-step; a 0-SPD one does not. Locks the
    // SPD → run-speed gap-control mechanic on the client's own interpolation.
    [Test]
    public void Process_LocalRunSpeed_ScalesWithSpd()
    {
        var fast = new ClientState { MyIndex = 1 };
        fast.Players[1].Name = "Fast";
        fast.Players[1].Spd = 150;
        fast.Players[1].XOffset = -Constants.PicX;
        fast.Players[1].Moving = MovementType.Running;
        MovementProcessor.Process(fast, 140f);
        Assert.That(fast.Players[1].XOffset, Is.EqualTo(0f), "max-SPD run closes the tile within 140ms");

        var slow = new ClientState { MyIndex = 1 };
        slow.Players[1].Name = "Slow";
        slow.Players[1].Spd = 0;
        slow.Players[1].XOffset = -Constants.PicX;
        slow.Players[1].Moving = MovementType.Running;
        MovementProcessor.Process(slow, 140f);
        Assert.That(slow.Players[1].XOffset, Is.LessThan(0f), "0-SPD run has NOT closed the tile in 140ms");
    }

    // An empty player slot (blank name) is skipped entirely — no phantom interpolation.
    [Test]
    public void Process_EmptyNameSlot_Untouched()
    {
        var s = new ClientState { MyIndex = 1 };
        s.Players[2].XOffset = -Constants.PicX;
        s.Players[2].Moving = MovementType.Walking;   // but Name is blank
        MovementProcessor.Process(s, 400f);
        Assert.That(s.Players[2].XOffset, Is.EqualTo(-Constants.PicX), "an unused slot is not advanced");
    }

    // A stopped player still flagged Moving (offsets already zero) is cleaned up to None.
    [Test]
    public void Process_ZeroOffsetButMovingFlag_DemotesToNone()
    {
        var s = new ClientState { MyIndex = 1 };
        var me = s.Players[1];
        me.Name = "Me";
        me.XOffset = 0;
        me.YOffset = 0;
        me.Moving = MovementType.Walking;
        MovementProcessor.Process(s, 16f);
        Assert.That(me.Moving, Is.EqualTo(MovementType.None));
    }

    // NPC slides interpolate too, at the tick-matched NPC walk rate.
    [Test]
    public void Process_NpcOffset_AdvancesTowardZero()
    {
        var s = new ClientState { MyIndex = 1 };
        s.Players[1].Name = "Me";
        var n = s.MapNpcs[1];
        n.Num = 1;
        n.XOffset = -Constants.PicX;
        n.Moving = MovementType.Walking;
        MovementProcessor.Process(s, MovementFormulas.NpcWalkMsPerTile);   // one full walk-tile duration
        Assert.That(n.XOffset, Is.EqualTo(0f));
        Assert.That(n.Moving, Is.EqualTo(MovementType.None));
    }
}
