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

    /// <summary>Fire whatever a game bound to a key the player just pressed.
    ///
    /// <para>Panels first, and then at most one action: a key holds one thing, which the engine enforces
    /// when the declaration is made, so there is nothing here to resolve between.</para></summary>
    private void ProcessGameKeys(InputState input)
    {
        foreach (GamePanel panel in _ctx.State.Panels.All)
        {
            if (!GameKeyMap.TryResolve(panel.Key, out var panelKey)) continue;
            if (!input.IsKeyPressed(panelKey)) continue;

            TogglePanel(panel.Id);
            return;
        }

        foreach (GameAction action in _ctx.State.Actions.All)
        {
            if (!GameKeyMap.TryResolve(action.Key, out var actionKey)) continue;
            if (!input.IsKeyPressed(actionKey)) continue;

            // A verb with nowhere to point still runs: the square is what the server is TOLD, and a game
            // whose rule does not care about the place is an ordinary game. Facing the edge of the world
            // is the only way this fails, and refusing the press there would be a shortcut that stops
            // working in a corner.
            if (!TryFacedSquare(out int mapNum, out int tileX, out int tileY))
                (mapNum, tileX, tileY) = (_ctx.State.CenterMapNum, _ctx.State.Me.X, _ctx.State.Me.Y);

            InvokeGameAction(action.Id, action.OpensPanel, mapNum, tileX, tileY);
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

    /// <summary>The square in front of the player, as the server names it.
    ///
    /// <para>Resolved in world space rather than on the center map, so facing across a seam names the
    /// neighbour's tile instead of one past the edge of your own.</para></summary>
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
