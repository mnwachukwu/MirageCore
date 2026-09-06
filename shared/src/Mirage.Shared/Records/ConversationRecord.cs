using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// An editor-authored NPC conversation (dialogue tree) — 1-based, held in <c>GameWorld.Conversations[]</c>
/// and persisted per entry as JSON, mirroring items/npcs/shops/quests. Attached to an NPC by number via
/// <see cref="SpeakerNpc"/> (a SIDE-MAPPING, so <c>NpcRecord</c> is never touched — exactly like a quest's
/// giver/turn-in roles). A player opens it by interacting with that NPC; the client walks the tree locally
/// from the join-time definition and only round-trips for a terminal hand-off choice. Pure flavor / lore /
/// hints — no economy mutation. A plain serializable POCO.
/// </summary>
public sealed class ConversationRecord
{
    private string _name = "";
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — names come padded; the non-empty trimmed name is the
    /// universal "this slot is a real conversation" predicate (like quests/items).</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    /// <summary>NPC number this conversation is attached to (0 = not attached to any NPC in-world). The
    /// resolver picks the first non-empty conversation whose SpeakerNpc matches the interacted NPC.</summary>
    public int SpeakerNpc { get; set; }

    /// <summary>The node the conversation opens on (a <see cref="ConversationNode.Id"/>). Falls back to the
    /// first node when it doesn't resolve.</summary>
    public int RootNodeId { get; set; }

    /// <summary>The dialogue nodes. Choices reference other nodes by STABLE <see cref="ConversationNode.Id"/>
    /// (assigned on add, never reused), so editing/reordering nodes doesn't silently repoint branches. Capped
    /// at <c>Constants.MaxConversationNodes</c> by the editor.</summary>
    public List<ConversationNode> Nodes { get; set; } = new();

    /// <summary>Find a node by its stable id (linear scan — trees are small). Null if absent.</summary>
    public ConversationNode? NodeById(int id)
    {
        foreach (var n in Nodes) if (n.Id == id) return n;
        return null;
    }

    /// <summary>The node a conversation opens on: <see cref="RootNodeId"/> if it resolves, else the first
    /// node, else null (an empty tree). <b>Derived — never persisted.</b> Without the attribute it
    /// serialized a full duplicate of the root node's subtree into every saved conversation.</summary>
    [JsonIgnore]
    public ConversationNode? RootNode => NodeById(RootNodeId) ?? (Nodes.Count > 0 ? Nodes[0] : null);

    /// <summary>Deep copy for an off-thread snapshot / broadcast (the node + choice lists are mutable refs).</summary>
    public ConversationRecord Clone()
    {
        var c = (ConversationRecord)MemberwiseClone();
        c.Nodes = new List<ConversationNode>(Nodes.Count);
        foreach (var n in Nodes) c.Nodes.Add(n.Clone());
        return c;
    }
}

/// <summary>One dialogue node: a line of speech plus the player's choices from it.</summary>
public sealed class ConversationNode
{
    /// <summary>Stable identifier (assigned on add in the editor, never reused), referenced by
    /// <see cref="ConversationChoice.NextNodeId"/>. 1-based; 0 is not a valid node id.</summary>
    public int Id { get; set; }
    /// <summary>Who is speaking (blank = the attached NPC's name). Authored flavor, not a loc key.</summary>
    public string Speaker { get; set; } = "";
    /// <summary>The line(s) spoken at this node. Authored flavor, not a loc key.</summary>
    public string Text { get; set; } = "";
    /// <summary>The choices offered from this node. Capped at <c>Constants.MaxConversationChoices</c>.</summary>
    public List<ConversationChoice> Choices { get; set; } = new();

    public ConversationNode Clone()
    {
        var c = (ConversationNode)MemberwiseClone();
        c.Choices = new List<ConversationChoice>(Choices.Count);
        foreach (var ch in Choices) c.Choices.Add(ch.Clone());
        return c;
    }
}

/// <summary>One selectable choice on a node: a button label plus what it does.</summary>
public sealed class ConversationChoice
{
    /// <summary>The button text the player picks. Authored flavor, not a loc key.</summary>
    public string Label { get; set; } = "";
    /// <summary>The node to go to when <see cref="Action"/> is <see cref="ConversationAction.None"/> (0 = end
    /// the conversation; an unresolvable id also ends it). Ignored for a hand-off action.</summary>
    public int NextNodeId { get; set; }
    /// <summary>A terminal hand-off into the NPC's other roles, or None for pure text navigation.</summary>
    public ConversationAction Action { get; set; }

    public ConversationChoice Clone() => (ConversationChoice)MemberwiseClone();
}
