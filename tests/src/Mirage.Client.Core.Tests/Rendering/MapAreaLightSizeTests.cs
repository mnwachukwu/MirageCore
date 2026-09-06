using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// A map-wide area light covers the map, not the viewport.
///
/// <para>The AlwaysLit / AlwaysDark / Indoors overrides each paint one soft box over a whole map cell. A box
/// sized to the viewport instead of to the map leaves the rest of a larger cell on the wrong lighting — a lit
/// town bright in its top-left corner and dark everywhere else. These pin the box to the map's own pixel
/// size, which equals the viewport only when the map happens to be the viewport's size.</para>
/// </summary>
[TestFixture]
public class MapAreaLightSizeTests
{
    private const int W = 24;
    private const int H = 20;

    private static ClientState StateOf(MapRecord center)
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = c == 1 && r == 1 ? center : new MapRecord(W, H);
        }

        return state;
    }

    private static (ClientState State, Camera Camera) Scene(MapRecord center)
    {
        var state = StateOf(center);
        var grid = new int[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                grid[c, r] = c * 3 + r + 1;
        }

        var camera = new Camera();
        camera.Update(0, 0, 0f, 0f, grid, W, H);
        return (state, camera);
    }

    [Test]
    public void AlwaysLit_BoxIsTheMapsSize_NotTheViewports()
    {
        var (state, camera) = Scene(new MapRecord(W, H) { AlwaysLit = true });
        var frame = RenderCommandBuilder.Build(state, new RenderFrame(), camera);

        Assert.That(frame.AlwaysLitMapLights, Is.Not.Empty, "a lit map must emit its area light");
        Assert.Multiple(() =>
        {
            Assert.That(frame.AlwaysLitMapLights[0].PxW, Is.EqualTo(W * Constants.PicX));
            Assert.That(frame.AlwaysLitMapLights[0].PxH, Is.EqualTo(H * Constants.PicY));
        });
    }

    [Test]
    public void AlwaysDark_BoxIsTheMapsSize_NotTheViewports()
    {
        var (state, camera) = Scene(new MapRecord(W, H) { AlwaysDark = true });
        var frame = RenderCommandBuilder.Build(state, new RenderFrame(), camera);

        Assert.That(frame.AlwaysDarkMapLights, Is.Not.Empty, "a dark map must emit its area light");
        Assert.Multiple(() =>
        {
            Assert.That(frame.AlwaysDarkMapLights[0].PxW, Is.EqualTo(W * Constants.PicX));
            Assert.That(frame.AlwaysDarkMapLights[0].PxH, Is.EqualTo(H * Constants.PicY));
        });
    }

    [Test]
    public void Indoors_BoxIsTheMapsSize_NotTheViewports()
    {
        var (state, camera) = Scene(new MapRecord(W, H) { Indoors = true });
        var frame = RenderCommandBuilder.Build(state, new RenderFrame(), camera);

        Assert.That(frame.IndoorsMapLights, Is.Not.Empty, "an indoor map must emit its area light");
        Assert.Multiple(() =>
        {
            Assert.That(frame.IndoorsMapLights[0].PxW, Is.EqualTo(W * Constants.PicX));
            Assert.That(frame.IndoorsMapLights[0].PxH, Is.EqualTo(H * Constants.PicY));
        });
    }

    /// <summary>The box reaches the map's far corner. At 24x20 that tile sits well outside a viewport-sized
    /// rectangle, so this is the check that separates the two sizings.</summary>
    [Test]
    public void TheBoxCoversTheMapsFarCorner()
    {
        var (state, camera) = Scene(new MapRecord(W, H) { AlwaysDark = true });
        var frame = RenderCommandBuilder.Build(state, new RenderFrame(), camera);
        var box = frame.AlwaysDarkMapLights[0];

        var (cornerX, cornerY) = camera.WorldTileToScreen(W + W - 1, H + H - 1, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cornerX, Is.GreaterThanOrEqualTo(box.ScreenX).And.LessThan(box.ScreenX + box.PxW));
            Assert.That(cornerY, Is.GreaterThanOrEqualTo(box.ScreenY).And.LessThan(box.ScreenY + box.PxH));
        });
    }

    /// <summary>At the default map size the box is still exactly the viewport, so nothing about the
    /// shipped world's lighting moved.</summary>
    [Test]
    public void AtTheDefaultSize_TheBoxIsStillTheViewport()
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = new MapRecord();
        }

        state.NeighborMaps[1, 1]!.AlwaysLit = true;

        var grid = new int[3, 3];
        grid[1, 1] = 1;
        var camera = new Camera();
        camera.Update(0, 0, 0f, 0f, grid, Constants.DefaultMapWidth, Constants.DefaultMapHeight);

        var frame = RenderCommandBuilder.Build(state, new RenderFrame(), camera);

        Assert.Multiple(() =>
        {
            Assert.That(frame.AlwaysLitMapLights[0].PxW, Is.EqualTo(Camera.ViewW));
            Assert.That(frame.AlwaysLitMapLights[0].PxH, Is.EqualTo(Camera.ViewH));
        });
    }
}
