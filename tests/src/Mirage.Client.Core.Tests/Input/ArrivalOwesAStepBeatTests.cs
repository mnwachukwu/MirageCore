using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>
/// 🔴 Landing somewhere by a warp is a step, and it owes the beat every other step owes.
///
/// <para>The movement gate opens as soon as the slide offsets are zero — and an arrival sets them to zero
/// outright rather than sliding them there, so it opens the instant the player lands. A warp is asked for
/// by WALKING onto a tile, which means the key that asked for it is still down at that moment: the step
/// that follows would be free, taken on a key the player has not pressed since, and it puts them one tile
/// past the destination in the direction they were facing.</para>
///
/// <para><b>The beat is what tells a tap from a hold</b>, and it does it without asking how long a press
/// is: a tap is over before the beat expires and moves nobody, while a key still down when it expires
/// walks on at the ordinary cadence. Nothing has to be released and pressed again.</para>
/// </summary>
[TestFixture]
public class ArrivalOwesAStepBeatTests
{
    /// <summary>A player standing clear of anything, with a map big enough to step around on.</summary>
    private static (ClientState State, FakeTransport Transport, ClientPacketSender Sender) Standing()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        state.NeighborMapNums[1, 1] = 1;

        var me = state.Me;
        me.Name = "Me";
        me.Map = 1;
        me.X = 5;
        me.Y = 5;
        me.Dir = Direction.Down;

        var transport = new FakeTransport();
        return (state, transport, new ClientPacketSender(transport));
    }

    private static void Step(ClientState state, ClientPacketSender sender, long nowMs, bool running = false) =>
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down, Running = running }, state, sender, nowMs);

    private static int MovesSent(FakeTransport t) => t.Sent.OfType<PlayerMovePacket>().Count();

    /// <summary>The overshoot, as a test: the frame the load ends is the frame the key is still down.</summary>
    [Test]
    public void TheFrameAfterArriving_DoesNotStep()
    {
        var (state, transport, sender) = Standing();
        state.ArrivedAtMs = 1_000;

        Step(state, sender, nowMs: 1_016);   // one frame later, key still down from the warp

        Assert.Multiple(() =>
        {
            Assert.That(MovesSent(transport), Is.Zero, "the arrival already spent this beat");
            Assert.That(state.Me.Y, Is.EqualTo(5), "so the player is still on the tile they landed on");
        });
    }

    /// <summary>A tap is over well inside a walk's beat, so it never produces the extra step.</summary>
    [Test]
    public void ATapIsOverBeforeTheBeatExpires()
    {
        var (state, transport, sender) = Standing();
        state.ArrivedAtMs = 1_000;

        // A human tap is about 100 ms, and the beat outlasts it.
        for (long t = 1_016; t <= 1_100; t += 16) Step(state, sender, t);

        Assert.That(MovesSent(transport), Is.Zero);
    }

    /// <summary>🔴 And a key still held when it expires walks on by itself. This is the half that makes the
    /// beat acceptable: nothing has to be released and pressed again, so holding a direction through a
    /// door keeps walking on the other side.</summary>
    [Test]
    public void AKeyStillHeldWhenTheBeatExpires_WalksOn()
    {
        var (state, transport, sender) = Standing();
        state.ArrivedAtMs = 1_000;

        Step(state, sender, nowMs: 1_150);   // inside the beat
        Assert.That(MovesSent(transport), Is.Zero, "not yet");

        Step(state, sender, nowMs: 1_210);   // past it

        Assert.Multiple(() =>
        {
            Assert.That(MovesSent(transport), Is.EqualTo(1), "the beat is served, so the held key steps");
            Assert.That(state.Me.Y, Is.EqualTo(6));
            Assert.That(state.ArrivedAtMs, Is.Zero, "and the debt is cleared, not re-charged every step");
        });
    }

    /// <summary>🔴 Walking and running owe the SAME beat. What it has to outlast is a tap, and a tap is
    /// the same length either way — a beat derived from pace makes arriving at a walk feel different from
    /// arriving at a run for no reason the player can see.</summary>
    [Test]
    public void WalkingAndRunning_OweTheSameBeat()
    {
        var (walkState, walkTransport, walkSender) = Standing();
        var (runState, runTransport, runSender) = Standing();
        runState.Me.Spd = 120;   // a pace whose own step is far quicker than a walk's
        runState.Me.Sp = 50;
        walkState.ArrivedAtMs = runState.ArrivedAtMs = 1_000;

        // Just inside the beat: neither moves.
        Step(walkState, walkSender, nowMs: 1_150);
        Step(runState, runSender, nowMs: 1_150, running: true);

        Assert.Multiple(() =>
        {
            Assert.That(MovesSent(walkTransport), Is.Zero, "walking is still held");
            Assert.That(MovesSent(runTransport), Is.Zero, "and so is running, at the same moment");
        });

        // Just past it: both move.
        Step(walkState, walkSender, nowMs: 1_210);
        Step(runState, runSender, nowMs: 1_210, running: true);

        Assert.Multiple(() =>
        {
            Assert.That(MovesSent(walkTransport), Is.EqualTo(1), "walking is released");
            Assert.That(MovesSent(runTransport), Is.EqualTo(1), "and so is running, at the same moment");
        });
    }

    /// <summary>With no arrival outstanding the gate is not in the way at all — the ordinary case, and the
    /// control that stops this passing by simply never moving.</summary>
    [Test]
    public void WithNoArrivalOutstanding_AStepIsImmediate()
    {
        var (state, transport, sender) = Standing();

        Step(state, sender, nowMs: 1_016);

        Assert.Multiple(() =>
        {
            Assert.That(MovesSent(transport), Is.EqualTo(1));
            Assert.That(state.Me.Y, Is.EqualTo(6));
        });
    }
}
