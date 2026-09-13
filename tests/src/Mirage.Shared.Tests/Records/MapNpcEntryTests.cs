using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// One authored NPC placement: which NPC, where it stands, on which plane, and which way it faces.
///
/// <para>Every part past the NPC number is optional and absent means "decide at spawn", so the file a
/// map writes stays as small as what its author actually chose.</para>
/// </summary>
[TestFixture]
public class MapNpcEntryTests
{
    private static string Write(MapNpcEntry e) => JsonSerializer.Serialize(e, RecordJson.Options);
    private static MapNpcEntry Read(string json) => JsonSerializer.Deserialize<MapNpcEntry>(json, RecordJson.Options);

    [Test]
    public void APlacementWithNoPin_ChoosesNothingForItself()
    {
        var entry = new MapNpcEntry(4, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(entry.HasPin, Is.False);
            Assert.That(entry.HasPinnedFacing, Is.False);
        });
    }

    /// <summary>🔴 A facing an author chose survives the file. It is one value on a struct that is written
    /// positionally, so a member added without a name reads back as whatever sat in that position.</summary>
    [Test]
    public void APinnedFacingSurvivesTheFile()
    {
        var authored = new MapNpcEntry(1, 5, 5, WorldLayer.Ground, Direction.Down);

        var back = Read(Write(authored));

        Assert.Multiple(() =>
        {
            Assert.That(back.PinDir, Is.EqualTo(Direction.Down));
            Assert.That(back.HasPinnedFacing, Is.True);
            Assert.That((back.Npc, back.PinX, back.PinY), Is.EqualTo((1, 5, 5)), "and the rest of it too");
        });
    }

    /// <summary>Facing north and facing any way are different statements, so the one that means "any way"
    /// writes nothing. A default member here would line every unauthored NPC on a map up the same way.</summary>
    [Test]
    public void APlacementThatChoosesNoFacing_WritesNone()
    {
        Assert.That(Write(new MapNpcEntry(1, 5, 5)), Does.Not.Contain("pinDir"));
    }

    [Test]
    public void AFacingOfUpIsStillAChoice_AndIsWritten()
    {
        // Up is the enum's zero, which is exactly the value a "skip the default" rule throws away by
        // accident. It has to survive, or "face north" silently becomes "face anywhere".
        var back = Read(Write(new MapNpcEntry(1, 5, 5, WorldLayer.Ground, Direction.Up)));

        Assert.Multiple(() =>
        {
            Assert.That(Write(new MapNpcEntry(1, 5, 5, WorldLayer.Ground, Direction.Up)), Does.Contain("pinDir"));
            Assert.That(back.PinDir, Is.EqualTo(Direction.Up));
        });
    }

    [Test]
    public void AFileWithNoFacing_ReadsAsNoChoice()
    {
        Assert.That(Read("""{ "npc": 1, "pinX": 5, "pinY": 5 }""").PinDir, Is.Null);
    }
}
