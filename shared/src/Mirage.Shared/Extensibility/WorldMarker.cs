namespace Mirage.Shared.Extensibility;

/// <summary>
/// Something drawn on a PLACE rather than over a body.
///
/// <para><b>Core draws over bodies and had nothing for the ground.</b> An <see cref="OverheadBar"/> is a
/// row over somebody's head; a marker is its twin for a square — a flag on a capture point, a ring
/// around a blast, a name over a doorway, a meter counting down on a ritual. Every one of those is a
/// game's, and none of them has a body to hang on.</para>
///
/// <para><b>It is state rather than a declaration.</b> A bar is declared once and read off whoever walks
/// past; a marker is put somewhere and taken away again, so it is placed through
/// <see cref="IWorld.Mark"/> and lives until the game removes it or the server stops. Placing one under
/// an <see cref="Id"/> that is already there replaces it, which is what makes a moving or counting
/// marker one call rather than a remove and a place.</para>
///
/// <para><b>What is drawn is what is set.</b> A marker with no label draws no label, one with no radius
/// draws no ring, and one with no ceiling draws no meter — so the same shape covers a bare pin and a
/// contested point with a name, a circle and a bar over it.</para>
/// </summary>
public sealed class WorldMarker
{
    /// <summary>The game's own name for this marker. Placing under a name already taken replaces what is
    /// there; <see cref="IWorld.Unmark"/> takes it away by the same name.</summary>
    public required string Id { get; init; }

    /// <summary>Which square it sits on, and on which plane — a mark on a bridge and one on the water
    /// under it are different marks, on the same three numbers.</summary>
    public required WorldPlace At { get; init; }

    /// <summary>What is written beside it. Empty draws nothing.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>What color it is drawn in, packed <c>0xRRGGBB</c>.</summary>
    public int Rgb { get; init; }

    /// <summary>How far the ring around it reaches, in tiles. Nothing at or below zero draws no ring.
    ///
    /// <para>⚠ Drawn as the STAIRCASE of tiles actually within that many steps, never as a circle. A
    /// ring that does not agree with the rule it illustrates is worse than none: a player standing on a
    /// tile the ring covers and the rule does not has been lied to.</para></summary>
    public int Radius { get; init; }

    /// <summary>How far along the meter is, against <see cref="Ceiling"/>.</summary>
    public long Value { get; init; }

    /// <summary>What the meter is measured against. Nothing at or below zero draws no meter, which is
    /// how a marker that is only a pin says so.</summary>
    public long Ceiling { get; init; }

    /// <summary>Who can see it. Empty means everybody who can see the square, which is the ordinary
    /// case; naming bodies makes it private to them.
    ///
    /// <para>⚠ A LIST rather than a rule, because Core has no way to evaluate a game's idea of a side.
    /// It is a snapshot: somebody who joins after it was placed is not on it until the game places the
    /// marker again. A game with a changing audience re-places rather than expecting the engine to
    /// notice, which is the same thing it already does to move one or move its meter.</para></summary>
    public IReadOnlyCollection<EntityHandle> SeenBy { get; init; } = [];
}
