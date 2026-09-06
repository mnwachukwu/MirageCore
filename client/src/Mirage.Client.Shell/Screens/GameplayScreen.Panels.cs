using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>Opening, raising and closing panels: the activate/toggle entry points the keybinds and
/// chat commands call, and the config restore on entering the screen.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // Single entry point for every panel show/hide path (slash commands, sidebar buttons,
    // hotkeys, links). Closing only happens when the panel is already the topmost open one;
    // a buried panel is raised to the front instead of toggled, and a closed panel is opened.
    private void ActivatePanel(int slot)
    {
        if (PanelIsOpen(slot))
        {
            if (TopOpenPanel() == slot)
            {
                SetPanelOpen(slot, false);         // already on top → close
            }
            else
            {
                BringToFront(slot);
                _panelFocused = true;
            }  // buried → raise, keep open
        }
        else
        {
            SetPanelOpen(slot, true);              // closed → open
            if (PanelIsOpen(slot))
            {
                BringToFront(slot);
                _panelFocused = true;
            }
        }
    }

    // Help panel open/close: closes when already on top, otherwise opens (if closed) and brings to
    // front. The panel's content is a pure function of the viewer's access level, so /help, H, and the
    // help link all open the same view — admins always get the admin command and admin-social sections,
    // regardless of which entry point opened it, and there is nothing a separate admin help would show.
    private void ActivateHelpPanel()
    {
        if (_help.IsOpen && TopOpenPanel() == PanelHelp)
        {
            _help.Toggle();  // already on top → close
        }
        else
        {
            if (!_help.IsOpen) _help.Toggle();
            _help.Populate(_ctx.State.Me.Access, _ctx.State.Me.GuildId > 0);
            BringToFront(PanelHelp);
            _panelFocused = true;
        }
    }

    // Topmost open panel index in Z-order, or -1 if none are open.
    private int TopOpenPanel()
    {
        for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
            if (PanelIsOpen(_zOrder[zi])) return _zOrder[zi];
        return -1;
    }

    // Opens or closes a single panel. The Shop panel is NOT listed here: it's purely server-driven now (a keeper
    // interact pushes the trades → ActiveShopNum, which opens it; see the ActiveShopNum poll in Update), so it has
    // no toggle entry point. The help panel refreshes its gamepad hints whenever it opens.
    // Registry rows without a Toggle are the server-driven panels — quest DIALOG, conversation, shop
    // and trade — which have no player-facing toggle entry point, so this is a no-op for them. (The
    // quest LOG does have one: it opens on J / the HUD button.)
    private void SetPanelOpen(int slot, bool open)
    {
        var s = _panels[slot];
        if (s.Toggle is not null && s.Panel.IsOpen != open) s.Toggle();
    }

    public void OnEnter()
    {
        _ctx.State.ActiveShopNum = 0;

        string account = _ctx.State.AccountName;
        string charName = _ctx.State.CurrentCharName;
        if (account.Length > 0 && charName.Length > 0)
        {
            _config = AccountConfig.Load(account);
            // Mirror of SavePanelConfig — same registry, same keys, so the two can never drift.
            foreach (var slot in _panels)
            {
                if (slot.Policy.ConfigKey is not null && _config.GetPanelBounds(charName, slot.Policy.ConfigKey) is Rectangle r)
                    slot.Panel.SetBounds(r);
            }

            _social.SetActiveTab(_config.GetSocialTab(charName));
            foreach (var (id, table) in AllColumnTables())
            {
                if (_config.GetTableColumns(charName, id) is { } cols)
                    table.ApplyColumnLayout(cols.Order ?? (IReadOnlyList<int>)Array.Empty<int>(), cols.Widths, cols.SortColumn, cols.SortAscending);
            }

            if (_config.GetPanelBounds(charName, "Death") is Rectangle deathR) _death.SetBounds(deathR);
            _chat.LoadTabs(_config, account);
            var prefs = _config.Characters.GetValueOrDefault(charName) ?? new AccountConfig.CharacterConfig();
            ApplyCharPrefs(prefs);
            // Not part of ApplyCharPrefs: Restore Defaults shares that method, and moving the player's
            // speech channel back to Say is not something restoring display options should do.
            _chat.SetActiveChannel(prefs.ActiveChatChannel);
        }

        // PlayerSpellsPacket arrives before this screen is created, so sync from state now.
        _spells.SetPreparedSlot(_ctx.State.Me.PreparedSpell);
    }

    public void OnExit() => CloseAllPanels();
}
