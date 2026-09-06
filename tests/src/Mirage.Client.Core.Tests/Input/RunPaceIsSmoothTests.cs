using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>
/// A run at any SPD takes the pace the formula says, and takes it evenly.
///
/// <para>🔴 Two things quantise a step, and both are invisible at the base 200 ms tile because it happens
/// to land on a round boundary. The first is the action tick: gating a move on it makes a faster slide
/// finish early and then stand still until the next tick, so investing SPD buys a stutter and no speed at
/// all. The second is the slide itself dropping the fraction of a frame left over when a tile lands
/// part-way through one, which makes the cadence alternate between two lengths.</para>
///
/// <para>These simulate the slide directly at a frame rate deliberately chosen NOT to divide the tile
/// times, since a rate that divides them cleanly hides both faults.</para>
/// </summary>
[TestFixture]
public class RunPaceIsSmoothTests
{
    /// <summary>A frame time that divides neither 200 ms nor the fast tile — 60 fps would hide the problem
    /// at both ends.</summary>
    private static readonly float[] FrameMs = [17f, 15f, 19f, 16f, 21f, 14f];

    /// <summary>The longest of those, which is the most a tile may legitimately differ by. Frame times VARY
    /// the way real ones do: a fixed one hides a dropped remainder, because every tile then loses the same
    /// fraction and the cadence stays even while being wrong.</summary>
    private const float LongestFrame = 21f;

    /// <summary>Runs <paramref name="tiles"/> tiles and reports how long each one took, in ms of simulated
    /// frames. Mirrors the shell: advance the slide every frame, start the next tile as soon as the last
    /// one clears.</summary>
    private static List<float> TileTimes(int spd, int tiles)
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        var p = state.Me;
        p.Name = "Me";
        p.Spd = spd;
        var times = new List<float>();
        int frameIndex = 0;

        for (int t = 0; t < tiles; t++)
        {
            // Begin a tile: the slide starts a full tile behind and runs down to zero.
            p.Moving = MovementType.Running;
            p.XOffset = -Constants.PicX;

            float elapsed = 0f;
            while (p.XOffset != 0f)
            {
                float frame = FrameMs[frameIndex++ % FrameMs.Length];
                MovementProcessor.Process(state, frame);
                elapsed += frame;
                if (elapsed > 5_000f) Assert.Fail("the slide never finished");
            }
            times.Add(elapsed);
        }
        return times;
    }

    /// <summary>The whole point of SPD: a faster run has to actually be faster. Measured over many tiles so
    /// per-tile rounding cannot carry the result.</summary>
    [Test]
    public void InvestingSpd_ActuallyMovesYouFaster()
    {
        float slow = TileTimes(spd: 0, tiles: 40).Sum();
        float fast = TileTimes(spd: 150, tiles: 40).Sum();

        Assert.That(fast, Is.LessThan(slow * 0.85f),
            $"40 tiles at the SPD cap took {fast}ms against {slow}ms at zero SPD — the speed went nowhere");
    }

    /// <summary>A run holds the formula's pace over its whole length, losing nothing on the way.
    ///
    /// <para>This is the measurement that matters, and the one a per-tile check misses. A slide can only
    /// end on a frame, so ANY single tile may overrun by up to one — that is unavoidable and invisible. What
    /// is not invisible is that fraction being DROPPED rather than carried: the loss then compounds, every
    /// tile after the first starts late, and forty of them drift far past a frame.</para></summary>
    [TestCase(0)]
    [TestCase(75)]
    [TestCase(135)]
    [TestCase(150)]
    public void ARunLosesNoTimeOverItsLength(int spd)
    {
        const int Tiles = 40;
        float want = MovementFormulas.RunMsPerTile(spd) * Tiles;
        float got = TileTimes(spd, Tiles).Sum();

        Assert.That(got, Is.EqualTo(want).Within(LongestFrame),
            $"SPD {spd}: {Tiles} tiles should take ~{want:0}ms, took {got:0}ms "
            + $"— {(got - want) / Tiles:0.0}ms lost per tile, which compounds into the stutter");
    }

    /// <summary>Standing still drops any carried remainder, or the first step after a pause jumps.</summary>
    [Test]
    public void APauseDoesNotBankTime()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        var p = state.Me;
        p.Name = "Me";
        p.Spd = 150;
        p.Moving = MovementType.Running;
        p.XOffset = -1f;

        MovementProcessor.Process(state, LongestFrame);   // the tile finishes, banking a carry
        Assume.That(p.SlideCarryMs, Is.GreaterThan(0f));

        MovementProcessor.Process(state, LongestFrame);   // an idle frame

        Assert.That(p.SlideCarryMs, Is.Zero, "a carry survived a pause and would jump the next step");
    }
}
