using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// <see cref="LightOcclusion.Fill"/> against <see cref="LightOcclusion.Reaches"/>, which answers the same
/// question by a separate route.
///
/// <para>Fill traces a whole square at once, laying the shadow grid out around the light and walking every
/// texel of it. Reaches sizes a grid to one target and walks one line. They share the tracer and nothing
/// else — not the radius, not the layout, not the indexing — so where they disagree, one of them has the
/// mask the wrong way round or off by a texel, which is exactly the sort of mistake that looks fine until a
/// shadow falls on the wrong side of the thing casting it.</para>
///
/// <para>Walls are laid down in patterns rather than at random so a failure names a case rather than a seed.
/// No tile sheets are loaded here, so every occluder covers its whole square — this is about geometry, and
/// <see cref="TileOpacityTests"/> is about shape.</para>
/// </summary>
[TestFixture]
public class LightFillEquivalenceTests
{
    private const int W = 24, H = 20;

    private static ClientState StateWhere(Func<int, int, bool> wall)
    {
        var state = new ClientState();
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                var map = new MapRecord(W, H);
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                        if (wall(x, y)) map.EditTile(x, y, t => t with { Type = TileType.Blocked });
                }

                state.NeighborMaps[col, row] = map;
            }
        }

        return state;
    }

    private static void AssertMatches(string what, Func<int, int, bool> wall, int r)
    {
        var state = StateWhere(wall);
        int lx = W + W / 2, ly = H + H / 2;                 // stand in the centre map
        var mask = new byte[LightOcclusion.MaskCells(r)];
        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted: true);

        int texels = LightOcclusion.MaskTexels(r);
        int sub = LightOcclusion.SubSamples, half = sub / 2;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int tx = (dx + r) * sub + half, ty = (dy + r) * sub + half;
                bool filled = LightOcclusion.IsLit(mask[ty * texels + tx]);
                bool reaches = LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, lx + dx, ly + dy);
                if (filled == reaches) continue;
                Assert.Fail($"{what} (r={r}): the mask says {filled} at the centre of tile ({dx},{dy}) "
                          + $"but tracing to it alone says {reaches}");
            }
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    public void OpenGround(int r) => AssertMatches("open ground", (_, _) => false, r);

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    public void SolidEverywhere(int r) => AssertMatches("nothing but wall", (_, _) => true, r);

    /// <summary>A wall on each side in turn — the four cases where a whole edge of the square is in shadow.</summary>
    [TestCase(3)]
    public void WallsOnEachSide(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("wall east", (x, y) => x == cx + 2 && y == cy, r);
        AssertMatches("wall west", (x, y) => x == cx - 2 && y == cy, r);
        AssertMatches("wall south", (x, y) => x == cx && y == cy + 2, r);
        AssertMatches("wall north", (x, y) => x == cx && y == cy - 2, r);
    }

    /// <summary>Diagonal-only contact, where two occluders meeting at a corner must close it.</summary>
    [TestCase(3)]
    public void WallTouchingOnlyAtACorner(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("wall on the diagonal", (x, y) => x == cx + 2 && y == cy + 2, r);
    }

    /// <summary>The light boxed in on every side at once.</summary>
    [TestCase(3)]
    public void BoxedIn(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("boxed in", (x, y) =>
            Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1 && !(x == cx && y == cy), r);
    }

    /// <summary>A one-tile corridor and a one-tile doorway, where shadow meets itself.</summary>
    [TestCase(3)]
    public void CorridorsAndDoorways(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("east-west corridor", (x, y) => y == cy - 1 || y == cy + 1, r);
        AssertMatches("north-south corridor", (x, y) => x == cx - 1 || x == cx + 1, r);
        AssertMatches("doorway", (x, y) => x == cx + 2 && y != cy, r);
    }

    /// <summary>Scattered walls, which is what a real map looks like: every relative direction at once.</summary>
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void ScatteredWalls(int r)
    {
        AssertMatches("scatter A", (x, y) => (x * 7 + y * 3) % 11 == 0, r);
        AssertMatches("scatter B", (x, y) => (x * 5 + y * 13) % 7 == 0, r);
        AssertMatches("scatter C", (x, y) => (x / 2 + y / 3) % 3 == 0, r);
    }

    /// <summary>Nothing in range to cast a shadow means every texel is lit — the contract of the one-pass
    /// fill that open ground takes, and the reason a torch in a field costs nothing to trace.</summary>
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(6)]
    public void WithNothingToCastAShadow_EveryTexelIsLit(int r)
    {
        var state = StateWhere((_, _) => false);
        var mask = new byte[LightOcclusion.MaskCells(r)];
        LightOcclusion.Fill(state, W + W / 2, H + H / 2, WorldLayer.Ground, r, mask, mounted: true);

        int cells = LightOcclusion.MaskCells(r);
        for (int i = 0; i < cells; i++)
            Assert.That(LightOcclusion.IsLit(mask[i]), Is.True, $"texel {i} of {cells} is dark on open ground");
    }

    /// <summary>
    /// 🔴 Whether a light's own tile shades it depends on whether the light BELONGS there.
    ///
    /// <para>A sconce is authored ON the wall it hangs from, and a tile that shaded itself would leave one lit
    /// texel and darkness everywhere else — four of the authored lights in the world are exactly this.</para>
    ///
    /// <para>A spell effect only PASSES OVER a tile. Exempt those and a burst scattering against a wall lights
    /// the wall as though it stopped nothing, which is most visible firing at something backed against it.</para>
    /// </summary>
    [TestCase(true, true, TestName = "MountedOnAnOccluder_LightsItsOwnTile")]
    [TestCase(false, false, TestName = "PassingOverAnOccluder_IsStoppedByIt")]
    public void ALightOnAnOccluder_ShadesItselfOnlyWhenPassingOver(bool mounted, bool expectLit)
    {
        var state = StateWhere((_, _) => false);
        int lx = W + W / 2, ly = H + H / 2;
        state.NeighborMaps[1, 1]!.EditTile(W / 2, H / 2, t => t with { Type = TileType.Blocked });

        const int r = 2;
        var mask = new byte[LightOcclusion.MaskCells(r)];
        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted);

        int texels = LightOcclusion.MaskTexels(r);
        int sub = LightOcclusion.SubSamples;
        for (int ty = r * sub; ty < (r + 1) * sub; ty++)
        {
            for (int tx = r * sub; tx < (r + 1) * sub; tx++)
            {
                Assert.That(LightOcclusion.IsLit(mask[ty * texels + tx]), Is.EqualTo(expectLit),
                    $"texel ({tx},{ty}) of the light's own tile");
            }
        }
    }
}
