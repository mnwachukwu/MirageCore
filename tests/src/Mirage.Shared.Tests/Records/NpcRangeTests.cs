using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// How far an NPC can notice anything.
///
/// <para>The ceiling sits just past a spell's reach, so a mob acquires a moment after it could be shot at
/// rather than from somewhere off the far edge of the view. It is enforced on the record itself, which is
/// the only place every path goes through: a file on disk, an editor save, a generator, the wire.</para>
/// </summary>
[TestFixture]
public class NpcRangeTests
{
    private static readonly JsonSerializerOptions Json = Serialization.RecordJson.Options;

    /// <summary>The soft cap is what a player can see up or down — the viewport's short half-extent, the
    /// same figure <see cref="WorldCoordHelper.IsInSpellRange"/> works from. Past it a mob acquires from
    /// somewhere its target cannot see.</summary>
    [Test]
    public void TheSoftCap_IsHowFarAPlayerCanSee()
    {
        Assert.That(Constants.NpcRangeSoftCap, Is.EqualTo(WorldCoordHelper.ViewportTilesY / 2));
    }

    /// <summary>Nothing enforces it. An author who wants a mob that notices the whole map has one, and
    /// the editor says what that will feel like rather than refusing.</summary>
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(255)]
    public void AnyRange_IsKeptExactly(int given)
    {
        Assert.That(new NpcRecord { Range = given }.Range, Is.EqualTo(given));
    }

    [Test]
    public void AnOversizedRangeOnDisk_LoadsUntouched()
    {
        var npc = JsonSerializer.Deserialize<NpcRecord>("""{"name":"Old Warden","range":15}""", Json)!;

        Assert.Multiple(() =>
        {
            Assert.That(npc.Name, Is.EqualTo("Old Warden"));
            Assert.That(npc.Range, Is.EqualTo(15));
        });
    }

    /// <summary>Both advisory figures are worth advising about: one sits above what a reach should be, the
    /// other below.</summary>
    [Test]
    public void TheTwoAdvisories_BracketAUsefulRange()
    {
        Assert.That(Constants.MinAggressiveNpcRange, Is.GreaterThan(1)
            .And.LessThan(Constants.NpcRangeSoftCap));
    }
}
