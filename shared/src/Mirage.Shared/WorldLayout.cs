namespace Mirage.Shared;

/// <summary>
/// What a world folder is made of.
///
/// <para>A server runs on two folders, split on one question: <b>does it change while the server runs?</b>
/// A world does not — it is what an author wrote, and it travels whole when a world is copied to another
/// machine. An installation's state does, and belongs to one server on one machine.</para>
///
/// <para>The list lives here because the server and the editor both read it. A folder one of them wrote
/// and the other never read would be a world with a hole in it.</para>
/// </summary>
public static class WorldLayout
{
    /// <summary>The record families an author fills. Not <c>accounts</c>, <c>guilds</c>, <c>market</c>,
    /// <c>trades</c>, <c>seasons</c> or <c>map_items</c>: those change as the server runs and live beside
    /// it in the data folder.</summary>
    public static readonly string[] WorldFolders =
        ["maps", "map_groups", "items", "npcs", "shops", "spells", "classes", "quests", "conversations"];

    /// <summary>What a world folder carries besides its records. The MOTD is not among them — it is the
    /// greeting one server gives, and belongs with that installation's state.</summary>
    public static readonly string[] WorldFiles = [Records.WorldManifest.FileName];
}
