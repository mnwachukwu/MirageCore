using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The color a creature SPEAKS in, which is the color it is named in.
///
/// <para>🔴 Whether a line reads as a greeting or as a threat is the same question the name already
/// answers, and only the loaded game can answer it — Core has no hostile. The bubble used to carry its
/// own answer on the wire, and the server only ever sent one value, so every creature in every world
/// growled in red.</para>
///
/// <para>The color is taken WHEN THE LINE ARRIVES rather than read each frame: a body killed
/// mid-sentence has forgotten its values by the time its last words finish drifting away, so a color
/// read at draw would change under them.</para>
/// </summary>
[TestFixture]
public class NpcChatBubbleTintTests
{
    private const int W = 24, H = 20;
    private const int Me = 1;
    private const int Kind = 3;
    private const int Slot = 1;
    private const int Map = 1;

    private const int Yellow = 0xFFFF00;
    private const int White = 0xFFFFFF;
    private const int Green = 0x00FF00;

    private static NameTintSet TheOriginalsRule() => new(
    [
        new NameTint { Key = "guard", Rgb = Yellow, Ordinal = 0 },
        new NameTint { Key = "hostile", Rgb = White, Ordinal = 1 },
    ], otherwiseRgb: Green);

    /// <summary>One creature on the player's map, carrying what the test says it carries.</summary>
    private static (ClientState State, ClientPacketHandler Handler) Scene(params (string Key, bool On)[] carrying)
    {
        var state = new ClientState { MyIndex = Me, InGame = true, CenterMapNum = Map };
        state.NeighborMaps[1, 1] = new MapRecord(W, H);
        state.NeighborMapNums[1, 1] = Map;
        state.Me.Name = "Vandestelka";
        state.Me.Map = Map;
        state.Me.X = 8;
        state.Me.Y = 6;
        state.NameTints = TheOriginalsRule();

        state.NpcDefs[Kind] = new NpcRecord { Name = "Adept", Sprite = 1 };
        var npc = state.MapNpcs[Slot];
        npc.Num = Kind;
        npc.X = 9;
        npc.Y = 6;

        var bag = state.BagFor(EntityHandle.ForNpc(Map, Slot))!;
        foreach (var (key, on) in carrying) bag.Set(key, on);

        return (state, new ClientPacketHandler(state, null!, null!));
    }

    private static void Says(ClientPacketHandler handler, string line)
        => handler.Handle(PacketSerializer.Serialize(PacketBuilder.NpcChatBubble(Map, Slot, line)));

    [TestCase(false, false, Green, TestName = "a shopkeeper greets you in green")]
    [TestCase(false, true, White, TestName = "a wolf growls in white")]
    [TestCase(true, false, Yellow, TestName = "a town guard warns you in yellow")]
    public void ACreatureSpeaksInTheColorItIsNamedIn(bool guard, bool hostile, int expected)
    {
        var (state, handler) = Scene(("guard", guard), ("hostile", hostile));

        Says(handler, "Watch it.");

        Assert.That(state.MapNpcs[Slot].ChatBubbleRgb, Is.EqualTo(expected));
    }

    /// <summary>A game that declared no tints has said nothing about what its creatures are, so they
    /// speak plainly rather than in a color Core picked for them.</summary>
    [Test]
    public void AGameThatDeclaredNoTints_HasItsCreaturesSpeakPlainly()
    {
        var (state, handler) = Scene();
        state.NameTints = NameTintSet.Plain;

        Says(handler, "Hello.");

        Assert.That(state.MapNpcs[Slot].ChatBubbleRgb, Is.EqualTo(NameTintSet.PlainRgb));
    }

    /// <summary>A second line demotes the first into a drifter, which keeps the color it was spoken in
    /// — including after the body is gone, which is the case this freezing exists for.</summary>
    [Test]
    public void ADriftingLineKeepsTheColorItWasSpokenIn()
    {
        var (state, handler) = Scene(("hostile", true));

        Says(handler, "Grr.");
        Says(handler, "GRR.");

        var npc = state.MapNpcs[Slot];
        Assert.Multiple(() =>
        {
            Assert.That(npc.ChatBubbleDrifters, Has.Count.EqualTo(1));
            Assert.That(npc.ChatBubbleDrifters![0].Rgb, Is.EqualTo(White));
            Assert.That(npc.ChatBubbleRgb, Is.EqualTo(White));
        });
    }

    /// <summary>End to end: the color the packet handler took is the color the draw command carries.</summary>
    [Test]
    public void TheBubbleIsDrawnInThatColor()
    {
        var (state, handler) = Scene(("guard", true));
        Says(handler, "Move along.");

        var camera = new Camera();
        camera.Update(state.Me.X, state.Me.Y, 0f, 0f, state.NeighborMapNums, W, H);
        var frame = new RenderFrame();
        RenderCommandBuilder.Build(state, frame, camera, myIndex: Me);

        Assert.That(frame.ChatBubbles, Has.Count.EqualTo(1));
        Assert.That(frame.ChatBubbles[0].BorderRgb, Is.EqualTo(Yellow));
    }
}
