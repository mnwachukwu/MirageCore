using Mirage.Client.Core.Logic;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The mask is a signed distance field, and this is why.
///
/// <para>A mask of 0s and 1s sampled with LINEAR filtering ramps from lit to dark across the gap between two
/// texel CENTRES — four world pixels, centred on the boundary, so half of it lands on the art itself and
/// every silhouette wears a hairline of light. Shifting the mask cannot fix that: the artifact is a spread
/// around the boundary, not an offset from it, and a texel is the smallest thing you can shift by.</para>
///
/// <para>Interpolating a DISTANCE is different. The blend of two distances is still very nearly the distance,
/// so the shader thresholds it and lands the edge on the boundary to a fraction of a texel. What has to hold
/// for that to work is checked here: the encoding is centred on the edge, the field crosses it exactly at the
/// boundary between a lit texel and a dark one, and the halfway sample of that pair reads as the edge.</para>
/// </summary>
[TestFixture]
public class LightDistanceFieldTests
{
    private const int N = 16;

    /// <summary>The byte the shader treats as exactly on the edge.</summary>
    private const byte Edge = 128;

    private static byte[] FieldOf(Func<int, int, bool> lit)
    {
        var traced = new bool[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++) traced[y * N + x] = lit(x, y);
        }

        var field = new byte[N * N];
        LightOcclusion.EncodeDistanceField(traced, N, field);
        return field;
    }

    [Test]
    public void TheEncodingIsCentredOnTheEdge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.Encode(0f), Is.EqualTo(Edge), "zero distance is the edge itself");
            Assert.That(LightOcclusion.IsLit(LightOcclusion.Encode(0.1f)), Is.True);
            Assert.That(LightOcclusion.IsLit(LightOcclusion.Encode(-0.1f)), Is.False);
            Assert.That(LightOcclusion.Encode(99f), Is.EqualTo(byte.MaxValue), "far inside saturates");
            Assert.That(LightOcclusion.Encode(-99f), Is.EqualTo(byte.MinValue), "and far outside");
        });
    }

    /// <summary>
    /// 🔴 The whole point. Across a straight edge, the two texels either side sit the SAME distance from it,
    /// so the linear blend the sampler takes halfway between their centres — which is where the art's edge
    /// is — comes out exactly on the threshold.
    /// </summary>
    [Test]
    public void TheFieldCrossesTheThreshold_HalfwayBetweenTheTexelsEitherSide()
    {
        var field = FieldOf((_, y) => y < 8);          // lit above row 8, dark from it down

        for (int x = 2; x < N - 2; x++)
        {
            byte lastLit = field[7 * N + x], firstDark = field[8 * N + x];
            Assert.Multiple(() =>
            {
                Assert.That(lastLit, Is.GreaterThan(Edge), $"column {x}: the texel above the edge is lit");
                Assert.That(firstDark, Is.LessThan(Edge), $"column {x}: the texel below it is dark");
                Assert.That((lastLit + firstDark) / 2.0, Is.EqualTo((double)Edge).Within(0.51),
                    $"column {x}: the sampler's halfway blend must land on the edge, not beside it");
            });
        }
    }

    /// <summary>The field grows with distance in both directions, so the shader's threshold has something to
    /// bite on rather than a plateau — and so a softened edge softens over a predictable width.</summary>
    [Test]
    public void TheFieldDeepensWithDistanceFromTheEdge()
    {
        var field = FieldOf((_, y) => y < 8);

        Assert.Multiple(() =>
        {
            Assert.That(field[6 * N + 8], Is.GreaterThan(field[7 * N + 8]), "two texels into the light");
            Assert.That(field[9 * N + 8], Is.LessThan(field[8 * N + 8]), "two texels into the shadow");
        });
    }

    /// <summary>Which side of the threshold a texel lands on still matches what the trace decided, so the
    /// field is a sharper rendering of the same shadow and not a different one.</summary>
    [TestCase(1)]
    [TestCase(3)]
    public void EveryTexelKeepsTheSideTheTraceGaveIt(int seed)
    {
        Func<int, int, bool> lit = (x, y) => (x * seed + y * 3) % 5 != 0;
        var field = FieldOf(lit);

        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                Assert.That(LightOcclusion.IsLit(field[y * N + x]), Is.EqualTo(lit(x, y)),
                    $"texel ({x},{y}) changed sides when it was encoded");
            }
        }
    }

    [Test]
    public void AFieldWithNoEdgeAtAll_SaturatesOnTheRightSide()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldOf((_, _) => true)[N * N / 2], Is.EqualTo(byte.MaxValue), "all lit");
            Assert.That(FieldOf((_, _) => false)[N * N / 2], Is.EqualTo(byte.MinValue), "all dark");
        });
    }
}
