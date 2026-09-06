using Microsoft.Xna.Framework;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The part of a floating in-game panel that does not vary in shape: whether it is showing, where it
/// sits, and whether the pointer is over it.
///
/// <para>Every panel already declared four of these five members with these exact signatures, so
/// implementing this interface adds almost no code to any of them. What it buys is that
/// <see cref="Screens.GameplayScreen"/>'s z-order, focus, hit-testing and bounds-persistence
/// plumbing can address the panels as a set instead of through a separate <c>switch</c> per
/// operation — there were twelve such constructs, each of which had to be edited in lockstep
/// whenever a panel was added.</para>
///
/// <para>Deliberately NOT on this interface: <c>Update</c> and <c>Draw</c>. Each panel needs a
/// different slice of the frame — most want the client state and the packet sender, some want the
/// item atlas, one wants the HUD's smoothed vital values, two want gamepad flags, and the options
/// panel returns a tuple of changed settings. Flattening that into one context object would hand
/// every panel arguments it has no business reading, and would trade twelve switches for one fat
/// parameter list. That variation lives in <c>GameplayScreen</c>'s <c>PanelSlot</c> registry
/// instead, alongside the other things that differ per panel: what closing means, whether the
/// player may toggle it, and the key its bounds persist under.</para>
/// </summary>
public interface IGamePanel
{
    /// <summary>Whether the panel is currently showing.</summary>
    bool IsOpen { get; }

    /// <summary>The panel's current screen rectangle — read when persisting its position.</summary>
    Rectangle Bounds { get; }

    /// <summary>Restores a persisted position. Called before the first draw, never per frame.</summary>
    void SetBounds(Rectangle b);

    /// <summary>Drops the panel back to its declared position and size — the Options panel's Reset
    /// Panels button. On the interface rather than done by the caller because the default rectangle is
    /// the panel's own, declared in its constructor and not visible from the registry.</summary>
    void ResetBounds();

    /// <summary>Whether <paramref name="p"/> is over this panel while it is open. Panels return
    /// false when closed, so this doubles as the "is the pointer over floating UI" test.</summary>
    bool ContainsMouse(Point p);
}
