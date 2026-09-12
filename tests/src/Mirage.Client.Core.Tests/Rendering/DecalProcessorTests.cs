using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>Client-side stain drying replays the shared linear fade between server events. Amount (size) and
/// Freshness (opacity) fade in lockstep, and a stain that dries below the visibility floor is dropped from the
/// list — matching the server — so both sides converge without a removal wire.</summary>
[TestFixture]
public class DecalProcessorTests
{
    private static ClientState.Decal Add(ClientState s, int mapNum, int x, int y, float amount, float fresh, int size = 1)
    {
        var d = new ClientState.Decal { X = x, Y = y, Size = size, Amount = amount, Freshness = fresh };
        s.DecalsForMap(mapNum).Add(d);
        return d;
    }

    [Test]
    public void Process_DriesAmount_AndFadesFreshnessProportionally()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var d = Add(s, 1, 3, 4, amount: 0.6f, fresh: 1f);

        // dry = DecalDryingPerSec(0.015) * 20 = 0.3; amount → 0.3; freshness *= 0.3/0.6 = 0.5
        DecalProcessor.Process(s, 20f);

        Assert.Multiple(() =>
        {
            Assert.That(d.Amount, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(d.Freshness, Is.EqualTo(0.5f).Within(1e-4f));
        });
    }

    [Test]
    public void Process_ADriedStain_IsDropped()
    {
        var s = new ClientState { CenterMapNum = 1 };
        Add(s, 1, 0, 0, amount: 0.6f, fresh: 1f);

        DecalProcessor.Process(s, 100f);

        Assert.That(s.DecalsByMap.ContainsKey(1), Is.False, "a fully-dried stain, and its now-empty map, are dropped");
    }

    [Test]
    public void Process_NonPositiveDt_ChangesNothing()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var d = Add(s, 1, 1, 1, amount: 0.5f, fresh: 1f);

        DecalProcessor.Process(s, 0f);

        Assert.That(d.Amount, Is.EqualTo(0.5f));
    }

    [Test]
    public void Process_DropsOnlyTheDriedStain()
    {
        var s = new ClientState { CenterMapNum = 1 };
        var live = Add(s, 1, 4, 4, amount: 1.0f, fresh: 1f);
        Add(s, 1, 8, 8, amount: 0.10f, fresh: 1f);   // 0.10 - 0.15 is under the floor

        DecalProcessor.Process(s, 10f);

        Assert.Multiple(() =>
        {
            Assert.That(s.DecalsByMap[1], Has.Count.EqualTo(1));
            Assert.That(s.DecalsByMap[1][0], Is.SameAs(live));
        });
    }
}
