using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using TextCopy;

namespace Mirage.Client.Shell.Panels;

/// <summary>Tab create/remove/reorder, and the per-tab config the options popup edits.</summary>
public sealed partial class ChatPanel
{
    // ── Tab CRUD (used by ChatOptionsPanel + the tab-strip click handler) ──────

    public AccountConfig.ChatTabConfig GetTabConfig(int index) => _tabs[index].Config;

    public int TabCount => _tabs.Count;

    /// <summary>Adds a new tab named "Tab {n}" with all channels enabled and Notify off. The "+"
    /// button calls this; the result is persisted immediately.</summary>
    public void AddTab()
    {
        if (_tabs.Count >= MaxTabs) return;
        var tab = MakeDefaultTab(_tabs.Count + 1);
        // A runtime-added tab is created after login, so seed its log with the active display prefs
        // (SetChatDisplayOptions only reaches tabs that already exist).
        tab.Log.ShowTimestamps = _showTimestamps;
        tab.Log.Use24HourClock = _use24HourClock;
        tab.Log.ShowChannelLabels = _showChannelLabels;
        _tabs.Add(tab);
        SaveTabs();
    }

    /// <summary>Removes the tab at the given index. Blocked when only one tab remains (the X
    /// button is hidden in that state, so this is also a safety net).</summary>
    public void RemoveTab(int index)
    {
        if (_tabs.Count <= 1) return;
        if (index < 0 || index >= _tabs.Count) return;
        _tabs.RemoveAt(index);
        if (_activeTab >= _tabs.Count) _activeTab = _tabs.Count - 1;
        SaveTabs();
    }

    /// <summary>Called by ChatOptionsPanel after any rename / channel-toggle / notify-toggle.
    /// Just persists the current state — the in-memory Config object is shared by reference.</summary>
    public void OnTabConfigChanged() => SaveTabs();
}
