using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>
/// A dead player is scenery, not an obstacle — walked over, not around.
///
/// <para>The client predicts collision so a clear step moves without waiting for the server, which means it
/// has to agree with the server about what blocks. Disagreeing either way is worse than either rule: the
/// client refusing a step the server allows means the move is never even sent, so nothing rubber-bands and
/// nothing explains itself — the key simply stops working on that one tile.</para>
///
/// <para>Both halves of the prediction are checked, because a corpse can be on the map you are standing on
/// or on the one across the seam, and those are separate code paths.</para>
/// </summary>
[TestFixture]
public class CorpsesAreWalkedOverTests
{
    /// <summary>Map 1 is Moral null, so players collide — a safe zone would let them pass through for an
    /// unrelated reason and prove nothing.</summary>
    private static (ClientState s, FakeTransport t, ClientPacketSender sender) Setup()
    {
        var s = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        s.NeighborMapNums[1, 1] = 1;
        var me = s.Me;
        me.Name = "Me";
        me.Map = 1;
        me.X = 5;
        me.Y = 5;
        me.Dir = Direction.Up;
        var t = new FakeTransport();
        return (s, t, new ClientPacketSender(t));
    }

    private static void PutSomeoneBelowMe(ClientState s, bool dead)
    {
        var other = s.Players[2];
        other.Name = "Blocker";
        other.Map = 1;
        other.X = 5;
        other.Y = 6;
        other.Dead = dead;
    }

    /// <summary>The control: a living player on the tile still blocks.</summary>
    [Test]
    public void ALivingPlayer_StillBlocks()
    {
        var (s, t, sender) = Setup();
        PutSomeoneBelowMe(s, dead: false);

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Y, Is.EqualTo(5));
            Assert.That(t.Sent.OfType<PlayerMovePacket>(), Is.Empty);
        });
    }

    [Test]
    public void ACorpse_IsWalkedOver()
    {
        var (s, t, sender) = Setup();
        PutSomeoneBelowMe(s, dead: true);

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Y, Is.EqualTo(6), "the step onto the corpse was predicted");
            Assert.That(t.Sent.OfType<PlayerMovePacket>().Count(), Is.EqualTo(1), "and sent");
        });
    }

    // ── Across a map seam ────────────────────────────────────────────────────

    /// <summary>Standing on the bottom edge, stepping onto the map below. A corpse over there is reached by
    /// the seam-crossing half of the prediction, which is a separate check.</summary>
    private static (ClientState s, FakeTransport t, ClientPacketSender sender) AtTheSeam(bool dead)
    {
        var (s, t, sender) = Setup();
        s.Map.Down = 2;
        s.NeighborMapNums[1, 2] = 2;
        // The map RECORD too, not just its number: an unloaded neighbour is allowed through on purpose
        // (the server corrects it), which would let this pass without deciding anything.
        s.NeighborMaps[1, 2] = new MapRecord { Down = 0, Up = 1 };
        s.Me.Y = s.Map.Height - 1;

        var other = s.Players[2];
        other.Name = "Blocker";
        other.Map = 2;
        other.X = 5;
        other.Y = 0;                                     // the landing tile on the far side
        other.Dead = dead;
        return (s, t, sender);
    }

    [Test]
    public void ALivingPlayer_AcrossTheSeam_StillBlocks()
    {
        var (s, t, sender) = AtTheSeam(dead: false);
        int wasY = s.Me.Y;

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Y, Is.EqualTo(wasY));
            Assert.That(t.Sent.OfType<PlayerMovePacket>(), Is.Empty);
        });
    }

    [Test]
    public void ACorpse_AcrossTheSeam_IsWalkedOver()
    {
        var (s, t, sender) = AtTheSeam(dead: true);

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);

        Assert.That(t.Sent.OfType<PlayerMovePacket>().Count(), Is.EqualTo(1),
            "the seam crossing onto a corpse was never sent");
    }
}
