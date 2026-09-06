using NUnit.Framework;

namespace Mirage.Shared.Tests.Social;

/// <summary>The chat overhaul gives every labeled channel and every name rank its own palette slot so
/// they read as visually distinct on the near-black chat background. This locks that invariant: a future
/// re-tint that accidentally collided two of them (or reused a slot) fails here. Emote deliberately reuses
/// Say's slot (7) and PK deliberately reuses Combat's pure red (12), so each of those appears once below.</summary>
[TestFixture]
public class ChatPaletteDistinctnessTests
{
    static readonly (string Name, int Index)[] Slots =
    {
        // Speech / social channels (referenced via their role aliases)
        ("Say/Emote", GameColor.Say), ("Yell", GameColor.Yellow), ("Broadcast", GameColor.Pink),
        ("Tell", GameColor.Tell), ("AdminChat", GameColor.AdminChat), ("Notice", GameColor.Notice),
        ("Roll", GameColor.Roll), ("Guild", GameColor.Guild), ("GuildOfficer", GameColor.GuildOfficer),
        ("War", GameColor.War), ("GuildWar", GameColor.GuildWar), ("Warning", GameColor.Warning),
        // Name ranks (as PlayerNameColor assigns them) + NPC dialogue
        ("Player", GameColor.Tan), ("Monitor", GameColor.Orange), ("Mapper", GameColor.Turquoise),
        ("Developer", GameColor.RoyalBlue), ("Creator", GameColor.Amethyst), ("PK/Combat", GameColor.BrightRed),
        ("NpcSpeech", GameColor.Npc),
    };

    // Squared-RGB Euclidean distance. The closest intended pairs (Dev royal-blue vs Roll cornflower,
    // War crimson vs GuildWar brick, Monitor orange vs NPC olive-gold) all sit ~53-60 apart, so a floor
    // of 40 passes them while catching an accidental near-duplicate.
    const int MinDistanceSq = 40 * 40;

    [Test]
    public void EveryChannelAndNameColor_IsMutuallyDistinct()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            for (int j = i + 1; j < Slots.Length; j++)
            {
                Assert.That(Slots[i].Index, Is.Not.EqualTo(Slots[j].Index),
                    $"{Slots[i].Name} and {Slots[j].Name} share palette slot {Slots[i].Index}");
                int a = GameColor.Rgb[Slots[i].Index], b = GameColor.Rgb[Slots[j].Index];
                int dr = GameColor.RedOf(a) - GameColor.RedOf(b);
                int dg = GameColor.GreenOf(a) - GameColor.GreenOf(b);
                int db = GameColor.BlueOf(a) - GameColor.BlueOf(b);
                int distSq = dr * dr + dg * dg + db * db;
                Assert.That(distSq, Is.GreaterThanOrEqualTo(MinDistanceSq),
                    $"{Slots[i].Name} (0x{a:X6}) and {Slots[j].Name} (0x{b:X6}) are too close: dist^2={distSq}");
            }
        }
    }
}
