using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Client.Shell.Screens;

/// <summary>
/// The keys a game bound, pressed by a client that has never heard of the game.
///
/// <para><b>Nothing here knows what any of them mean.</b> A game names a key and an action id; pressing
/// the key sends the id back with the square the player is facing — the same square the square menu
/// would have acted on, so the key and the menu item are one verb with two ways in. A panel key opens
/// and closes a window whose contents are attributes the client already holds, so it costs no round
/// trip at all.</para>
///
/// <para>Which keys a game may take is <see cref="GameKey.Offered"/>, and the engine refuses a
/// declaration naming anything else. What it may NOT take is everything the player already learned:
/// moving, running, picking up, the action bar, and every window Core opens itself.</para>
/// </summary>
public sealed partial class GameplayScreen
{
    /// <summary>Whether a game has taken Core's own reaching key for itself. Core then stops reaching:
    /// two things on one key is worse than either alone, and the game's declaration is the more
    /// specific of the two.</summary>
    private bool GameHoldsInteractKey()
    {
        const string Reach = "E";

        foreach (GameAction action in _ctx.State.Actions.All)
            if (string.Equals(action.Key, Reach, System.StringComparison.Ordinal))
                return true;

        foreach (GamePanel panel in _ctx.State.Panels.All)
            if (string.Equals(panel.Key, Reach, System.StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>Which action-bar slot the player just asked for with a digit, or 0.
    ///
    /// <para>1 through 9 in order, then 0 for a tenth slot — the row a keyboard has. A game may declare
    /// more than that; those are reached by clicking them, because there is no eleventh digit to bind
    /// and taking a letter for one would be Core claiming a key a game might want.</para>
    ///
    /// <para>Nothing is read past the declared width, so a world with three slots leaves 4 through 0 to
    /// whatever else wants them.</para></summary>
    private static int DigitPressed(InputState input, int slots)
    {
        for (int slot = 1; slot <= slots && slot <= HotkeyBar.Keyed; slot++)
        {
            // Keys.D1..D9 and NumPad1..NumPad9 run in order; the tenth slot is 0, which sits after 9 on
            // a keyboard and before 1 in the enum.
            var (top, pad) = slot == HotkeyBar.Keyed
                ? (Keys.D0, Keys.NumPad0)
                : (Keys.D1 + (slot - 1), Keys.NumPad1 + (slot - 1));

            if (input.IsKeyPressed(top) || input.IsKeyPressed(pad)) return slot;
        }
        return 0;
    }

    /// <summary>Fire whatever a game bound to a key the player just pressed.
    ///
    /// <para>Panels first, and then at most one action: a key holds one thing, which the engine enforces
    /// when the declaration is made, so there is nothing here to resolve between.</para></summary>
    private void ProcessGameKeys(InputState input)
    {
        // 🔴 Held with Ctrl it is somebody else's shortcut, not the game's. Ctrl+C copies, and a
        // game that bound C would otherwise open its window every time the player copied a line - the
        // two firing together, with nothing anywhere saying they collided. A game binds a KEY, and
        // that is the key on its own.
        if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl)
            || input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl)) return;

        foreach (GamePanel panel in _ctx.State.Panels.All)
        {
            if (!GameKeyMap.TryResolve(panel.Key, out var panelKey)) continue;
            if (!input.IsKeyPressed(panelKey)) continue;

            // A shortcut obeys the panel's condition too, or it is the way around a grayed-out
            // button. Silently, since there is nothing to gray out on a keyboard.
            if (!panel.While.Holds(_ctx.State.Me.Attributes)) return;

            TogglePanel(panel.Id);
            return;
        }

        foreach (GameAction action in _ctx.State.Actions.All)
        {
            if (!GameKeyMap.TryResolve(action.Key, out var actionKey)) continue;
            if (!input.IsKeyPressed(actionKey)) continue;

            // A shortcut obeys the verb's condition, or it would be the way around a grayed-out menu
            // entry. Silently: there is nothing to gray out on a keyboard.
            if (!action.When.Holds(
                _ctx.State.AttributesOf(EntityHandle.ForPlayer(_ctx.State.MyIndex)))) return;

            // A verb with nowhere to point still runs: the square is all the server is TOLD, and a game
            // whose rule does not care about the place is an ordinary game. Facing the edge of the world
            // is the only way this fails, and refusing the press there would be a shortcut that stops
            // working in a corner.
            if (!TryFacedSquare(out int mapNum, out int tileX, out int tileY))
                (mapNum, tileX, tileY) = (_ctx.State.CenterMapNum, _ctx.State.Me.X, _ctx.State.Me.Y);

            // 🔴 The body standing there, named the way the menu would have named it. Without this a
            // key reaches a SQUARE and the same verb picked off a menu reaches a creature, so the two
            // are not the same verb after all - and a verb that hands Core's reaching back does it from
            // the menu and silently not from its own key, which is the half of the pair a player
            // actually presses.
            InvokeGameAction(action.Id, action.OpensPanel, mapNum, tileX, tileY,
                             npcSlot: NpcSlotOn(mapNum, tileX, tileY));
            return;
        }
    }

    /// <summary>Show a declared panel, or hide the one already showing. One slot holds them all, so a
    /// second key opens its own panel over the first rather than beside it.</summary>
    private void TogglePanel(string panelId)
    {
        if (string.Equals(_gamePanel.OpenId, panelId, System.StringComparison.Ordinal))
        {
            _gamePanel.Close();
            return;
        }

        _gamePanel.Open(_ctx.State, panelId);
        BringToFront(PanelGame);
    }

    /// <summary>Which creature is standing on that square, or 0 for an empty one. The map's own slot,
    /// which an invoke carries - the server turns it into an identity before a game sees
    /// it.</summary>
    private int NpcSlotOn(int mapNum, int tileX, int tileY)
    {
        var npcs = _ctx.State.NpcsForMap(mapNum);
        if (npcs is null) return 0;

        for (int slot = 1; slot < npcs.Length; slot++)
        {
            var body = npcs[slot];
            if (body.Num > 0 && body.X == tileX && body.Y == tileY) return slot;
        }

        return 0;
    }

    /// <summary>The square in front of the player, as the server names it.
    ///
    /// <para>Resolved in world space rather than on the center map, so facing across a seam names the
    /// neighbor's tile instead of one past the edge of your own.</para></summary>
    private bool TryFacedSquare(out int mapNum, out int tileX, out int tileY)
    {
        mapNum = tileX = tileY = 0;

        var state = _ctx.State;
        var me = state.Me;
        var (dx, dy) = DirDelta(me.Dir);
        var (worldX, worldY) = state.CenterToWorld(me.X + dx, me.Y + dy);
        var (col, row, localX, localY) = state.FromWorld(worldX, worldY);

        if (col is < 0 or > 2 || row is < 0 or > 2) return false;

        mapNum = state.NeighborMapNums[col, row];
        if (mapNum <= 0) return false;

        tileX = localX;
        tileY = localY;
        return true;
    }
}
