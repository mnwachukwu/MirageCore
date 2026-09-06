using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>Client-side blood-pool decay replays the shared linear dissipation between server events.  Amount
/// (size) and Freshness (opacity) fade in lockstep, and a pool that dries below the visibility floor is dropped
/// from the list (matching the server) so both sides converge without a removal wire.</summary>
[TestFixture]
public class BloodProcessorTests
{
    static ClientState.BloodPool AddPool(ClientState s, int mapNum, int x, int y, float amount, float fresh, int size = 1)
    {
        var p = new ClientState.BloodPool { X = x, Y = y, Size = size, Amount = amount, Freshness = fresh };
        s.BloodPoolsForMap(mapNum).Add(p);
        return p;
    }

    [Test]
    public void Process_DecaysAmount_AndFadesFreshnessProportionally()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var p = AddPool(s, 1, 3, 4, amount: 0.6f, fresh: 1f);
        // d = BloodDissipationPerSec(0.015) * 20 = 0.3; newAmount = 0.3; freshness *= 0.3/0.6 = 0.5
        BloodProcessor.Process(s, 20f);
        Assert.Multiple(() =>
        {
            Assert.That(p.Amount, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(p.Freshness, Is.EqualTo(0.5f).Within(1e-4f));
        });
    }

    [Test]
    public void Process_DriedPool_IsDropped()
    {
        var s = new ClientState { CenterMapNum = 1 };
        AddPool(s, 1, 0, 0, amount: 0.6f, fresh: 1f);
        BloodProcessor.Process(s, 100f);   // d = 1.5 > amount → below the floor → pool removed, map emptied
        Assert.That(s.BloodByMap.ContainsKey(1), Is.False, "a fully-decayed pool (and its now-empty map) is dropped");
    }

    [Test]
    public void Process_NonPositiveDt_NoOp()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var p = AddPool(s, 1, 1, 1, amount: 0.5f, fresh: 1f);
        BloodProcessor.Process(s, 0f);
        Assert.That(p.Amount, Is.EqualTo(0.5f));
    }

    [Test]
    public void Process_LivePool_Survives_AndOnlyDriedOneDrops()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var live = AddPool(s, 1, 4, 4, amount: 1.0f, fresh: 1f);
        AddPool(s, 1, 8, 8, amount: 0.10f, fresh: 1f);   // will dry: 0.10 - 0.15 < floor
        BloodProcessor.Process(s, 10f);   // d = 0.15
        Assert.That(s.BloodByMap[1].Count, Is.EqualTo(1), "only the dried pool is dropped");
        Assert.That(s.BloodByMap[1][0], Is.SameAs(live));
    }
}
