using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared.Extensibility;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// Turning one body's values into the rows drawn over its head.
///
/// <para>🔴 Every failure here is silent. A row that reads the wrong key, a fraction the wrong way up, a
/// color dropped on the way through — none of them throw, and all of them look like a rendering choice
/// somebody made on purpose. The projection is the whole of what a game gets for declaring a bar, so it
/// is pinned from the outside: given these values, exactly these rows.</para>
/// </summary>
[TestFixture]
public class OverheadBarRowTests
{
    private static readonly MethodInfo Projection =
        typeof(RenderCommandBuilder).GetMethod("OverheadRows", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static (BarRow, BarRow, BarRow) RowsFor(ClientState state, EntityHandle who)
        => ((BarRow, BarRow, BarRow))Projection.Invoke(null, [state, who])!;

    private const int Me = 1;

    private static ClientState WithBars(params OverheadBar[] bars)
    {
        var state = new ClientState { MyIndex = Me };
        state.OverheadBars = new OverheadBarSet(bars);
        return state;
    }

    private static OverheadBar Bar(string key, int rgb = 0, int ordinal = 0)
        => new() { ValueKey = key, MaxKey = key + "Max", Rgb = rgb, Ordinal = ordinal };

    [Test]
    public void AGameThatDeclaredNoBars_DrawsNoRows()
    {
        var (a, b, c) = RowsFor(new ClientState { MyIndex = Me }, EntityHandle.ForPlayer(Me));

        Assert.That(new[] { a, b, c }.Select(r => r.Shown), Is.All.False);
    }

    [Test]
    public void ARowCarriesTheFractionAndTheDeclaredColor()
    {
        var state = WithBars(Bar("hull", rgb: 0xDC2828));
        state.Players[Me].Attributes.Set("hull", 5).Set("hullMax", 20);

        var (first, _, _) = RowsFor(state, EntityHandle.ForPlayer(Me));

        Assert.Multiple(() =>
        {
            Assert.That(first.Frac, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(first.Rgb, Is.EqualTo(0xDC2828));
            Assert.That(first.Shown, Is.True);
        });
    }

    /// <summary>The positions are the game's declaration order, not "whichever ones this body has" — so
    /// two bodies carrying different halves of the same game still agree on which row is which.</summary>
    [Test]
    public void ARowAGameDeclaredButThisBodyLacks_IsAbsentInItsOwnPosition()
    {
        var state = WithBars(Bar("hull", ordinal: 0), Bar("fuel", ordinal: 1), Bar("heat", ordinal: 2));
        state.Players[Me].Attributes.Set("hull", 1).Set("hullMax", 2).Set("heat", 3).Set("heatMax", 4);

        var (hull, fuel, heat) = RowsFor(state, EntityHandle.ForPlayer(Me));

        Assert.Multiple(() =>
        {
            Assert.That(hull.Shown, Is.True);
            Assert.That(fuel.Shown, Is.False, "declared, and this body carries nothing for it");
            Assert.That(heat.Frac, Is.EqualTo(0.75f).Within(0.0001f), "still the third row, not promoted to the second");
        });
    }

    [Test]
    public void AnNpcIsReadFromWhatTheServerSentAboutIt()
    {
        var state = WithBars(Bar("hull"));
        var who = EntityHandle.ForNpc(spawnMap: 3, spawnSlot: 7);
        state.BagFor(who)!.Set("hull", 9).Set("hullMax", 10);

        var (first, _, _) = RowsFor(state, who);

        Assert.That(first.Frac, Is.EqualTo(0.9f).Within(0.0001f));
    }

    /// <summary>🔴 This runs once per visible body per frame. A get-or-create here would leave an empty
    /// bag behind every NPC that ever crossed the screen, and nothing ever clears one that holds nothing.</summary>
    [Test]
    public void ReadingABodyThatCarriesNothing_DoesNotStartKeepingValuesForIt()
    {
        var state = WithBars(Bar("hull"));
        var stranger = EntityHandle.ForNpc(spawnMap: 2, spawnSlot: 4);

        RowsFor(state, stranger);

        Assert.That(state.AttributesOf(stranger), Is.Null, "reading invented a bag for a body nobody synced");
    }

    [Test]
    public void ABodyTheClientIsNotTrackingAtAll_DrawsNoRows()
    {
        var state = WithBars(Bar("hull"));

        var (a, b, c) = RowsFor(state, EntityHandle.None);

        Assert.That(new[] { a, b, c }.Select(r => r.Shown), Is.All.False);
    }

    // ── What the draw layer is handed ─────────────────────────────────────────

    [Test]
    public void TheCommandCountsOnlyTheRowsItWillActuallyDraw()
    {
        var cmd = new BarDrawCmd(0, 0, new BarRow(0.5f, 0), BarRow.None, new BarRow(1f, 0),
                                 CdFrac: OverheadBar.Absent, ShowCombatBorder: false);

        Assert.Multiple(() =>
        {
            Assert.That(cmd.ShownRows, Is.EqualTo(2));
            Assert.That(cmd.RowAt(1).Shown, Is.False);
            Assert.That(cmd.RowAt(2).Frac, Is.EqualTo(1f));
        });
    }

    /// <summary>The cooldown is the engine's own row and shares the group's outline, so it counts toward
    /// the height even though no game declared it.</summary>
    [Test]
    public void TheCooldownRowCountsTowardTheGroupHeight()
    {
        var cmd = new BarDrawCmd(0, 0, new BarRow(0.5f, 0), BarRow.None, BarRow.None,
                                 CdFrac: 0.3f, ShowCombatBorder: false);

        Assert.That(cmd.ShownRows, Is.EqualTo(2));
    }
}
