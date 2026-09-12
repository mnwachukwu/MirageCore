using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>
/// The pace the server bills a step at reaching the client that predicts it.
///
/// <para>🔴 This is the bug the field was added to close, and it is invisible from either side alone.
/// The client draws its own step the moment the key goes down and the server confirms it a round trip
/// later; both compute the pace from the same formula, but only the server had the number. A client
/// predicting the baseline while the server bills a faster pace does not error — it rubber-bands, which
/// reads as lag rather than as a missing field, and every test that exercises one side alone passes.</para>
/// </summary>
[TestFixture]
public class MoveSpeedReachesTheClientTests
{
    private const int Me = 1;

    private static readonly MethodInfo Handle = typeof(ClientPacketHandler)
        .GetMethod("HandleSendPlayerData", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The handler touches neither sender nor mapCache, so both are null! (per the client-test convention).
    private static ClientState Apply(SendPlayerDataPacket packet)
    {
        var state = new ClientState { MyIndex = Me };
        Handle.Invoke(new ClientPacketHandler(state, null!, null!), [packet]);
        return state;
    }

    [Test]
    public void TheJoinBroadcast_CarriesTheMoveSpeed()
    {
        var state = Apply(new SendPlayerDataPacket { Index = Me, Name = "Matt", MoveSpeed = 150 });

        Assert.That(state.Players[Me].MoveSpeed, Is.EqualTo(150));
    }

    /// <summary>What the field is FOR: the local prediction reads it, so the client's pace is the pace
    /// the server will bill. Asserted against the formula rather than a number, because the two sides
    /// agreeing is the property — not any particular cadence.</summary>
    [Test]
    public void TheLocalPrediction_UsesThePaceTheServerWillBill()
    {
        var state = Apply(new SendPlayerDataPacket { Index = Me, Name = "Matt", MoveSpeed = 150 });

        float predicted = MovementProcessor.MyRunMsPerTile(state);

        Assert.Multiple(() =>
        {
            Assert.That(predicted, Is.EqualTo(MovementFormulas.RunMsPerTile(150)));
            Assert.That(predicted, Is.LessThan(MovementFormulas.BaseRunMsPerTile),
                "a body with speed must actually predict faster, or the field changed nothing");
        });
    }

    [Test]
    public void AWorldThatSetsNoSpeed_PredictsTheBaseline()
    {
        var state = Apply(new SendPlayerDataPacket { Index = Me, Name = "Matt" });

        Assert.That(MovementProcessor.MyRunMsPerTile(state), Is.EqualTo(MovementFormulas.BaseRunMsPerTile));
    }
}
