using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The color a creature's name is drawn in, end to end: what the loaded game declared, read against
/// the values that creature is carrying.
///
/// <para>🔴 Silent in every direction. A name colored off the wrong thing does not throw, does not log,
/// and looks like somebody's styling decision — so a world where every creature comes out one color
/// reads as finished rather than broken. Pinned from the outside: given these declarations and these
/// values, exactly this color on the draw command.</para>
///
/// <para>⚠ Behavior must NOT reach this. Core's behaviors describe locomotion, so an archer keeping its
/// distance and a deer keeping its distance are both <see cref="NpcBehavior.Shadow"/>, and a rule that
/// read one would name them the same.</para>
/// </summary>
[TestFixture]
public class NpcNameTintTests
{
    private const int W = 24, H = 20;
    private const int Me = 1;
    private const int Kind = 3;
    private const int Slot = 1;

    private const int Yellow = 0xFFFF00;
    private const int White = 0xFFFFFF;
    private const int Green = 0x00FF00;

    /// <summary>One creature standing beside the player, named and visible.</summary>
    private static (ClientState State, Camera Camera) Scene(NpcBehavior behavior, params (string Key, bool On)[] carrying)
    {
        var state = new ClientState { MyIndex = Me };
        state.NeighborMaps[1, 1] = new MapRecord(W, H);
        state.NeighborMapNums[1, 1] = 1;
        state.CenterMapNum = 1;

        var me = state.Players[Me];
        me.Name = "Vandestelka";
        me.Sprite = 1;
        me.Map = 1;
        me.X = 8;
        me.Y = 6;

        state.NpcDefs[Kind] = new NpcRecord { Name = "Adept", Behavior = behavior, Sprite = 1 };

        var npc = state.MapNpcs[Slot];
        npc.Num = Kind;
        npc.X = 9;
        npc.Y = 6;

        var bag = state.BagFor(EntityHandle.ForNpc(1, Slot))!;
        foreach (var (key, on) in carrying) bag.Set(key, on);

        var camera = new Camera();
        camera.Update(me.X, me.Y, 0f, 0f, state.NeighborMapNums, W, H);
        return (state, camera);
    }

    private static int NameRgb(ClientState state, Camera camera)
    {
        var frame = new RenderFrame();
        RenderCommandBuilder.Build(state, frame, camera, myIndex: Me);

        var name = frame.Names.Find(n => n.Text == "Adept");
        Assert.That(name.Text, Is.EqualTo("Adept"), "the creature's name was never drawn at all");
        return name.RgbOverride;
    }

    private static NameTintSet TheOriginalsRule() => new(
    [
        new NameTint { Key = "guard", Rgb = Yellow, Ordinal = 0 },
        new NameTint { Key = "hostile", Rgb = White, Ordinal = 1 },
    ], otherwiseRgb: Green);

    [Test]
    public void AGameThatDeclaredNoTints_NamesACreaturePlainly()
    {
        var (state, camera) = Scene(NpcBehavior.Wander);

        Assert.That(NameRgb(state, camera), Is.EqualTo(NameTintSet.PlainRgb));
    }

    [TestCase(false, false, Green, TestName = "a shopkeeper is green")]
    [TestCase(false, true, White, TestName = "a wolf is white")]
    [TestCase(true, false, Yellow, TestName = "a town guard is yellow")]
    public void ACreatureIsNamedByWhatItCarries(bool guard, bool hostile, int expected)
    {
        var (state, camera) = Scene(NpcBehavior.Wander, ("guard", guard), ("hostile", hostile));
        state.NameTints = TheOriginalsRule();

        Assert.That(NameRgb(state, camera), Is.EqualTo(expected));
    }

    /// <summary>🔴 The case that broke: two creatures that move identically and mean opposite things.
    /// Both keep their distance, so anything reading <see cref="NpcBehavior"/> names them the same.</summary>
    [Test]
    public void AnArcherAndADeerKeepTheSameDistance_AndAreNamedDifferently()
    {
        var (archer, archerCam) = Scene(NpcBehavior.Shadow, ("hostile", true));
        archer.NameTints = TheOriginalsRule();

        var (deer, deerCam) = Scene(NpcBehavior.Shadow, ("hostile", false));
        deer.NameTints = TheOriginalsRule();

        Assert.Multiple(() =>
        {
            Assert.That(NameRgb(archer, archerCam), Is.EqualTo(White));
            Assert.That(NameRgb(deer, deerCam), Is.EqualTo(Green));
        });
    }

    /// <summary>A creature the server has said nothing about yet — its values have not arrived, or the
    /// game keeps none on it — is named plainly rather than left uncolored.</summary>
    [Test]
    public void ACreatureCarryingNothing_TakesThePlainColor()
    {
        var (state, camera) = Scene(NpcBehavior.Stationary);
        state.NameTints = TheOriginalsRule();

        Assert.That(NameRgb(state, camera), Is.EqualTo(Green));
    }
}
