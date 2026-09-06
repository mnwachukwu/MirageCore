using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Logic;
using Mirage.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Client.Shell.Config;

/// <summary>
/// Per-account config file (config/{account}.json).
/// Each account entry holds per-character settings (panel layout, etc.).
/// </summary>
public sealed class AccountConfig
{
    [JsonPropertyName("lastCharSlot")]
    public int LastCharSlot { get; set; } = 0;
    [JsonPropertyName("characters")]
    public Dictionary<string, CharacterConfig> Characters { get; set; } = new();
    // Tabbed-chat preferences live at the account level (not per-character) because chat is an
    // account-wide comms choice. Empty list on load = first launch / migrated old config; the
    // ChatPanel then constructs one default tab with all channels enabled.
    [JsonPropertyName("chatTabs")]
    public List<ChatTabConfig> ChatTabs { get; set; } = new();

    /// <summary>One row in the tab strip. Channel names are stored as strings rather than the
    /// `ChatChannel` enum so old config files survive a future enum reordering.</summary>
    public sealed class ChatTabConfig
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("notify")] public bool Notify { get; set; }
        [JsonPropertyName("disabledChannels")] public List<string> DisabledChannels { get; set; } = new();
    }

    public sealed class CharacterConfig
    {
        [JsonPropertyName("panels")]
        public Dictionary<string, PanelBounds> Panels { get; set; } = new();
        [JsonPropertyName("alwaysShowBars")]
        public bool AlwaysShowBars { get; set; } = true;
        [JsonPropertyName("showCombatNumbers")]
        public bool ShowCombatNumbers { get; set; } = true;
        [JsonPropertyName("skipPlayersWithTabTarget")]
        public bool SkipPlayersWithTabTarget { get; set; } = true;
        [JsonPropertyName("showNpcNames")]
        public bool ShowNpcNames { get; set; } = true;
        public bool ShowBlood { get; set; } = true;
        [JsonPropertyName("showOtherPlayerNames")]
        public bool ShowOtherPlayerNames { get; set; } = true;
        [JsonPropertyName("showPlayerName")]
        public bool ShowPlayerName { get; set; } = true;
        [JsonPropertyName("showCooldownBar")]
        public bool ShowCooldownBar { get; set; } = true;
        [JsonPropertyName("showOtherCooldownBars")]
        public bool ShowOtherCooldownBars { get; set; } = false;
        [JsonPropertyName("showChatTimestamps")]
        public bool ShowChatTimestamps { get; set; } = false;
        [JsonPropertyName("use24HourClock")]
        public bool Use24HourClock { get; set; } = false;
        [JsonPropertyName("showChannelLabels")]
        public bool ShowChannelLabels { get; set; } = false;
        // The channel the dropdown left of the chat input is set to (ActiveSpeechChannel enum name).
        // Stored as a string so an unknown/renamed value degrades to Say instead of throwing.
        [JsonPropertyName("chatChannel")]
        public string ActiveChatChannel { get; set; } = "Say";
        // The Social panel's last-open tab (0 = Friends, 1 = Ignore, 2 = Guild), restored so reopening
        // returns to where the player left off.
        [JsonPropertyName("socialTab")]
        public int SocialTab { get; set; }
        // Every Table's saved column layout, keyed by a stable table id (e.g. "social.roster", "mail.messages").
        // Widths + sort persist for all tables; order only for reorderable ones (fixed tables omit it). Restored
        // on world entry. One keyed map covers every table.
        [JsonPropertyName("tableColumns")]
        public Dictionary<string, TableColumnState> TableColumns { get; set; } = new();
    }

    /// <summary>A Table control's persisted column layout: <see cref="Order"/> is display-position → logical
    /// column index (null/omitted for a fixed-order table — only reorderable tables persist it), <see cref="Widths"/>
    /// is each column's width indexed by logical column, and the sort is the logical column + direction
    /// (<see cref="SortColumn"/> -1 = none saved → keep the table's default).</summary>
    public sealed class TableColumnState
    {
        [JsonPropertyName("order")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? Order { get; set; }
        [JsonPropertyName("widths")] public List<int> Widths { get; set; } = new();
        [JsonPropertyName("sortColumn")] public int SortColumn { get; set; } = -1;
        [JsonPropertyName("sortAscending")] public bool SortAscending { get; set; } = true;
    }

    public sealed class PanelBounds
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }

        public Rectangle ToRectangle() => new(X, Y, Width, Height);

        public static PanelBounds From(Rectangle r) =>
            new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public Rectangle? GetPanelBounds(string charName, string panelName)
    {
        if (!Characters.TryGetValue(charName, out var cc)) return null;
        if (!cc.Panels.TryGetValue(panelName, out var b)) return null;
        return b.ToRectangle();
    }

    public void SetPanelBounds(string charName, string panelName, Rectangle bounds)
    {
        if (!Characters.TryGetValue(charName, out var cc))
            Characters[charName] = cc = new CharacterConfig();
        cc.Panels[panelName] = PanelBounds.From(bounds);
    }

    /// <summary>The Social panel's last-open tab for a character (0 = Friends if unset).</summary>
    public int GetSocialTab(string charName)
        => Characters.TryGetValue(charName, out var cc) ? cc.SocialTab : 0;

    public void SetSocialTab(string charName, int tab)
    {
        if (!Characters.TryGetValue(charName, out var cc))
            Characters[charName] = cc = new CharacterConfig();
        cc.SocialTab = tab;
    }

    /// <summary>A table's saved column layout for a character (keyed by table id), or null if none saved yet.</summary>
    public TableColumnState? GetTableColumns(string charName, string tableId)
        => Characters.TryGetValue(charName, out var cc) && cc.TableColumns.TryGetValue(tableId, out var st) ? st : null;

    /// <summary>Persist a table's column layout under <paramref name="tableId"/>. Pass a null
    /// <paramref name="order"/> for a fixed-order table so no order is written (only reorderable tables save
    /// order). Lists are copied defensively, so mutating the caller's lists afterward can't corrupt the store.</summary>
    public void SetTableColumns(string charName, string tableId, IReadOnlyList<int>? order, IReadOnlyList<int> widths, int sortColumn, bool sortAscending)
    {
        if (!Characters.TryGetValue(charName, out var cc))
            Characters[charName] = cc = new CharacterConfig();
        cc.TableColumns[tableId] = new TableColumnState
        {
            Order = order is null ? null : new List<int>(order),
            Widths = new List<int>(widths),
            SortColumn = sortColumn,
            SortAscending = sortAscending,
        };
    }

    public static AccountConfig Load(string accountName)
    {
        string path = FilePath(accountName);
        if (!File.Exists(path)) return new AccountConfig();
        try
        {
            return JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(path), JsonOpts)
                   ?? new AccountConfig();
        }
        catch
        {
            return new AccountConfig();
        }
    }

    public void Save(string accountName)
    {
        string path = FilePath(accountName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    private static string FilePath(string accountName)
    {
        string safe = PortableFileName.Sanitize(accountName);
        return AppPaths.Config("config", $"{safe}.json");
    }
}
