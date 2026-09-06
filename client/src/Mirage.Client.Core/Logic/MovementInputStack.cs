using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Resolves the four movement inputs into a single direction by press-order, so the
/// most-recently-pressed key that is still held wins ("input stack") rather than a fixed
/// Up > Down > Left > Right priority. Holding W then D steps right; releasing D while W
/// is still held falls back to up.
///
/// Fed the current held-state once per movement tick (from GameplayScreen.BuildInputSnapshot):
/// it reconciles its stack against that state and returns the dominant direction. The stack is
/// retained across ticks, which is why this is an instance rather than a static helper.
/// </summary>
public sealed class MovementInputStack
{
    // Directions currently held, oldest-first: the last entry is the most-recently-pressed and
    // therefore dominant. At most four entries (one per direction).
    private readonly List<Direction> _held = new(4);

    // Tie-break for directions that first appear on the SAME tick, where no real press-order
    // exists: pushing in this order leaves the last one (Up) on top, matching the legacy fixed
    // precedence Up > Down > Left > Right.
    private static readonly Direction[] SameTickPushOrder =
        { Direction.Right, Direction.Left, Direction.Down, Direction.Up };

    /// <summary>
    /// Reconciles the stack with this tick's held directions and returns the dominant one
    /// (most-recently-pressed still-held), or null when none are held.
    /// </summary>
    public Direction? Resolve(bool up, bool down, bool left, bool right)
    {
        // Drop directions no longer held; the survivors keep their relative press-order.
        _held.RemoveAll(d => !IsHeld(d, up, down, left, right));
        // Append newly-held directions on top, deterministically ordered for same-tick ties.
        foreach (var d in SameTickPushOrder)
        {
            if (IsHeld(d, up, down, left, right) && !_held.Contains(d))
                _held.Add(d);
        }

        return _held.Count > 0 ? _held[^1] : null;
    }

    private static bool IsHeld(Direction d, bool up, bool down, bool left, bool right) => d switch
    {
        Direction.Up => up,
        Direction.Down => down,
        Direction.Left => left,
        Direction.Right => right,
        _ => false
    };
}
