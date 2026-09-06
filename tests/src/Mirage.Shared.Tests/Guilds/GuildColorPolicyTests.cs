using NUnit.Framework;

namespace Mirage.Shared.Tests.Guilds;

/// <summary>Guild overhead colors are free 24-bit RGB except for the 16 named palette colors (and a small
/// tolerance sphere around each). <see cref="GuildColorPolicy"/> is the authority both the server and the
/// client's color picker consult; it reads the palette + packing helpers from <see cref="GameColor"/> (the
/// single source of truth). This locks the "reserved" boundary down.</summary>
[TestFixture]
public class GuildColorPolicyTests
{
    [Test]
    public void EveryPaletteColor_IsReserved()
    {
        foreach (int rgb in GameColor.Rgb)
            Assert.That(GuildColorPolicy.IsReserved(rgb), Is.True, $"palette color 0x{rgb:X6} must be reserved");
    }

    [Test]
    public void ColorFarFromEveryPaletteEntry_IsAllowed()
    {
        // A mid mustard-brown that isn't near any of the 16 corners/primaries/grays.
        Assert.That(GuildColorPolicy.IsReserved(GameColor.Pack(150, 110, 40)), Is.False);
    }

    [Test]
    public void ColorWithinToleranceOfAPaletteEntry_IsReserved()
    {
        // A hair off pure red (0xFF0000) — inside the tolerance sphere, so still reserved.
        Assert.That(GuildColorPolicy.IsReserved(GameColor.Pack(250, 4, 4)), Is.True);
    }

    [Test]
    public void ColorJustBeyondTolerance_IsAllowed()
    {
        // Offset from pure red by more than the tolerance distance on one channel — now a legal shade.
        int offset = (int)System.Math.Sqrt(Constants.GuildColorReservedDistanceSq) + 5;
        Assert.That(GuildColorPolicy.IsReserved(GameColor.Pack(255 - offset, 0, 0)), Is.False);
    }

    [Test]
    public void PaletteRgb_MatchesIndexConstants()
    {
        // The Rgb table must line up with the index constants it documents (a reorder would silently
        // repaint chat text and shift the reserved set).
        Assert.That(GameColor.Rgb[GameColor.Black], Is.EqualTo(0x000000));
        Assert.That(GameColor.Rgb[GameColor.White], Is.EqualTo(0xFFFFFF));
        Assert.That(GameColor.Rgb[GameColor.BrightRed], Is.EqualTo(0xFF0000));
        // Original QBColor slots must be PRESERVED verbatim (the overhaul must never re-tint them).
        Assert.That(GameColor.Rgb[GameColor.Blue], Is.EqualTo(0x000080));    // Navy
        Assert.That(GameColor.Rgb[GameColor.Cyan], Is.EqualTo(0x008080));    // Teal
        Assert.That(GameColor.Rgb[GameColor.Red], Is.EqualTo(0x800000));     // Maroon
        Assert.That(GameColor.Rgb[GameColor.Magenta], Is.EqualTo(0x800080)); // Purple
        Assert.That(GameColor.Rgb[GameColor.Brown], Is.EqualTo(0x808000));   // Olive
        // Extended chat-overhaul colors (16-29), each named for its color.
        Assert.That(GameColor.Rgb[GameColor.Cornflower], Is.EqualTo(0x6495ED));
        Assert.That(GameColor.Rgb[GameColor.Rose], Is.EqualTo(0xFF5C9E));
        Assert.That(GameColor.Rgb[GameColor.OliveGold], Is.EqualTo(0xB5A03C));
        Assert.That(GameColor.Rgb[GameColor.Orange], Is.EqualTo(0xE8843C));
        Assert.That(GameColor.Rgb[GameColor.Amethyst], Is.EqualTo(0xC74DE0));
        Assert.That(GameColor.Rgb[GameColor.Emerald], Is.EqualTo(0x43C46A));
        Assert.That(GameColor.Rgb[GameColor.Mint], Is.EqualTo(0x86E3B0));
        Assert.That(GameColor.Rgb[GameColor.Crimson], Is.EqualTo(0xE5484D));
        Assert.That(GameColor.Rgb[GameColor.Brick], Is.EqualTo(0xB5352F));
        Assert.That(GameColor.Rgb[GameColor.RoyalBlue], Is.EqualTo(0x3B6FE6));
        Assert.That(GameColor.Rgb[GameColor.Turquoise], Is.EqualTo(0x1BA89C));
        Assert.That(GameColor.Rgb[GameColor.Coral], Is.EqualTo(0xFF6B6B));
        Assert.That(GameColor.Rgb[GameColor.Periwinkle], Is.EqualTo(0xB39DFF));
        Assert.That(GameColor.Rgb[GameColor.Tan], Is.EqualTo(0xC2AE86));
        // Role aliases point at the intended colors (change-in-one-place).
        Assert.That(GameColor.Roll, Is.EqualTo(GameColor.Cornflower));
        Assert.That(GameColor.AdminChat, Is.EqualTo(GameColor.Rose));
        Assert.That(GameColor.Notice, Is.EqualTo(GameColor.Periwinkle));
        Assert.That(GameColor.Guild, Is.EqualTo(GameColor.Emerald));
        Assert.That(GameColor.War, Is.EqualTo(GameColor.Crimson));
        Assert.That(GameColor.Warning, Is.EqualTo(GameColor.Coral));
        Assert.That(GameColor.Npc, Is.EqualTo(GameColor.OliveGold));
        Assert.That(GameColor.Rgb.Length, Is.EqualTo(30));
    }
}
