using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// Which graphics sheet a record draws from.
///
/// <para>Sprites and items are numbered collections, so a row number alone does not identify art — the
/// sheet travels with it. Two rules hold that together: a file naming no sheet reads as sheet 0, and a
/// record written from here always states its sheet, including when it is 0, so nothing on disk leaves a
/// reader inferring it.</para>
///
/// <para>The second rule is the one that needs a test. Absent-reads-as-zero is what plain deserialization
/// does anyway, but "always written" is a property of the record's attributes and would be quietly undone
/// by a <c>WhenWritingDefault</c> added for tidiness.</para>
/// </summary>
[TestFixture]
public class SheetFieldTests
{
    private static string Write<T>(T record) => JsonSerializer.Serialize(record, RecordJson.Options);

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, RecordJson.Options)!;

    /// <summary>Every record that names a sheet writes it even at 0, so a file states which sheet it draws
    /// from rather than leaving it to be inferred.</summary>
    [Test]
    public void AZeroSheetIsStillWritten()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Write(new NpcRecord { Name = "Rat", Sprite = 8 }), Does.Contain("\"spriteSheet\": 0"));
            Assert.That(Write(new ItemRecord { Name = "Dagger", Pic = 14 }), Does.Contain("\"itemSheet\": 0"));
            // A class names one per sex, and both are written at 0.
            string cls = Write(new ClassRecord { Name = "Warrior", SpriteMale = 34 });
            Assert.That(cls, Does.Contain("\"spriteSheetMale\": 0"));
            Assert.That(cls, Does.Contain("\"spriteSheetFemale\": 0"));
            Assert.That(Write(new PlayerRecord { Name = "Matt", Sprite = 34 }),
                Does.Contain("\"spriteSheet\": 0"));
        });
    }

    /// <summary>A non-zero sheet survives a write and a read, which is the whole point of the field.</summary>
    [Test]
    public void ANamedSheetRoundTrips()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read<NpcRecord>(Write(new NpcRecord { Sprite = 8, SpriteSheet = 3 })).SpriteSheet,
                Is.EqualTo(3));
            Assert.That(Read<ItemRecord>(Write(new ItemRecord { Pic = 14, ItemSheet = 2 })).ItemSheet,
                Is.EqualTo((short)2));
            // Distinct values, so a projection carrying one to both fields fails here.
            var cls = Read<ClassRecord>(Write(new ClassRecord
            {
                SpriteMale = 1, SpriteSheetMale = 5, SpriteFemale = 2, SpriteSheetFemale = 9,
            }));
            Assert.That(cls.SpriteSheetMale, Is.EqualTo(5));
            Assert.That(cls.SpriteSheetFemale, Is.EqualTo(9));
            Assert.That(Read<PlayerRecord>(Write(new PlayerRecord { Sprite = 1, SpriteSheet = 7 })).SpriteSheet,
                Is.EqualTo(7));
        });
    }

    /// <summary>🔴 A world authored before sheets existed still loads, and reads as sheet 0 — the sheet every
    /// one of those files meant. This is what makes the field safe to add with no migration step, and it is
    /// the half that would break silently: art would simply come from the wrong sheet.</summary>
    [Test]
    public void AFileThatNamesNoSheetReadsAsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read<NpcRecord>("""{"name":"Rat","sprite":8}""").SpriteSheet, Is.EqualTo(0));
            Assert.That(Read<ItemRecord>("""{"name":"Dagger","pic":14}""").ItemSheet, Is.EqualTo((short)0));
            var cls = Read<ClassRecord>("""{"name":"Warrior","spriteMale":34}""");
            Assert.That(cls.SpriteSheetMale, Is.EqualTo(0));
            Assert.That(cls.SpriteSheetFemale, Is.EqualTo(0));
            Assert.That(Read<PlayerRecord>("""{"name":"Matt","sprite":34}""").SpriteSheet, Is.EqualTo(0));
        });
    }

    /// <summary>The row number is untouched by any of this. A sheet is which picture book, not which page,
    /// and confusing the two would repoint every NPC in the world.</summary>
    [Test]
    public void TheRowNumberIsUnaffected()
    {
        var npc = Read<NpcRecord>(Write(new NpcRecord { Sprite = 8, SpriteSheet = 3 }));
        var item = Read<ItemRecord>(Write(new ItemRecord { Pic = 14, ItemSheet = 2 }));

        Assert.That(npc.Sprite, Is.EqualTo(8));
        Assert.That(item.Pic, Is.EqualTo((short)14));
    }
}
