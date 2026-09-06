using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Security;

/// <summary>One server an app has connected to, or one a game creator shipped.</summary>
public sealed record ServerEntry
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("host")] public string Host { get; init; } = "";
    [JsonPropertyName("port")] public int Port { get; init; }

    /// <summary>Key form, matching how <see cref="ServerBook"/> compares entries.</summary>
    [JsonIgnore] public string Key => ServerBook.KeyFor(Host, Port);
}

/// <summary>
/// The servers this installation knows about, in the order they were added. Keyed by host and port, so
/// two entries may share a name but never an address. Ships as a plain JSON array a game creator can
/// pre-fill and distribute.
/// </summary>
public sealed class ServerBook
{
    /// <summary>The address a fresh install starts with. The port is the caller's, because an app that
    /// speaks to the management socket starts somewhere else than one that logs in.</summary>
    public const string DefaultName = "Default localhost";
    public const string DefaultHost = "localhost";
    public const int DefaultPort = Constants.GamePort;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly int _defaultPort;
    private readonly Lock _gate = new();
    private List<ServerEntry> _entries;

    public static string KeyFor(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    public ServerBook(string path, int defaultPort = DefaultPort)
    {
        _path = path;
        _defaultPort = defaultPort;
        _entries = Load(path, defaultPort, out bool created);
        if (created) Save();
    }

    /// <summary>Every known server, in insertion order.</summary>
    public IReadOnlyList<ServerEntry> All
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    public ServerEntry? Find(string host, int port)
    {
        string key = KeyFor(host, port);
        lock (_gate) { return _entries.FirstOrDefault(e => e.Key == key); }
    }

    /// <summary>Records a server that was just reached, under the name it reported. An entry that already
    /// carries a name keeps it: a name in this book belongs to whoever put it there — the player, or the
    /// creator who shipped the list — and a server does not get to relabel itself in someone else's book.
    /// Only a blank name is filled in.</summary>
    public void Remember(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        string key = KeyFor(host, port);
        lock (_gate)
        {
            int at = _entries.FindIndex(e => e.Key == key);
            if (at < 0)
                _entries.Add(new ServerEntry { Name = name.Trim(), Host = host.Trim(), Port = port });
            else if (_entries[at].Name.Length == 0 && !string.IsNullOrWhiteSpace(name))
                _entries[at] = _entries[at] with { Name = name.Trim() };
            else
                return;
            Save();
        }
    }

    /// <summary>Adds or names a server the USER asked for, replacing whatever name is there. A blank name
    /// still adds the entry, but never erases a name already on one.</summary>
    public void Rename(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        string key = KeyFor(host, port);
        string wanted = name.Trim();
        lock (_gate)
        {
            int at = _entries.FindIndex(e => e.Key == key);
            if (at < 0)
                _entries.Add(new ServerEntry { Name = wanted, Host = host.Trim(), Port = port });
            else if (wanted.Length > 0 && _entries[at].Name != wanted)
                _entries[at] = _entries[at] with { Name = wanted };
            else
                return;
            Save();
        }
    }

    /// <summary>Drops the entry. True if there was one.</summary>
    public bool Forget(string host, int port)
    {
        string key = KeyFor(host, port);
        lock (_gate)
        {
            int at = _entries.FindIndex(e => e.Key == key);
            if (at < 0) return false;
            _entries.RemoveAt(at);
            Save();
            return true;
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _entries = Load(_path, _defaultPort, out bool created);
            if (created) Save();
        }
    }

    /// <summary>Reads the book. No file means a fresh install: the default entry is created and
    /// <paramref name="created"/> says to write it out. A file that parses to nothing is a book someone
    /// emptied, and stays empty; one that fails to parse is read as empty and left on disk untouched.</summary>
    private static List<ServerEntry> Load(string path, int defaultPort, out bool created)
    {
        created = false;
        if (!File.Exists(path))
        {
            created = true;
            return [new ServerEntry { Name = DefaultName, Host = DefaultHost, Port = defaultPort }];
        }

        List<ServerEntry> read;
        try { read = JsonSerializer.Deserialize<List<ServerEntry>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return read.Where(e => !string.IsNullOrWhiteSpace(e.Host) && seen.Add(e.Key)).ToList();
    }

    private void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
