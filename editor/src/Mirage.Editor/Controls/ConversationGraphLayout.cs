using System;
using System.Collections.Generic;

namespace Mirage.Editor.Controls;

/// <summary>Where a branch leaves the conversation. <see cref="None"/> means it goes to another node.</summary>
public enum ConversationEndKind
{
    None = 0,
    Ends,
    OpensShop,
    OpensQuests,
}

/// <summary>One choice as the layout sees it: the node it leads to, or the way it leaves the conversation.
/// A branch with <see cref="End"/> of <see cref="ConversationEndKind.None"/> whose target names no node
/// ends the conversation too — that is the runtime's own rule, applied here so the picture matches it.</summary>
public readonly record struct ConversationGraphBranch(int TargetNodeId, ConversationEndKind End);

/// <summary>One node as the layout sees it: a stable id and its choices, in choice order.</summary>
public readonly record struct ConversationGraphNode(int Id, IReadOnlyList<ConversationGraphBranch> Branches);

/// <summary>Where one node sits, in grid slots rather than pixels — the renderer owns box size and spacing.
/// <c>IsReachable</c> is false for a node the root cannot walk to, which is an authoring mistake worth
/// showing rather than hiding.</summary>
public readonly record struct ConversationGraphPlacement(int NodeId, int Column, int Row, bool IsRoot, bool IsReachable);

/// <summary>One way out of a node, and how many of its choices take it.</summary>
public readonly record struct ConversationGraphEnding(ConversationEndKind Kind, int Count);

/// <summary>The endings of one node, gathered into a single slot below it. Distinct KINDS get their own
/// row in the slot; several choices ending the same way are counted rather than repeated, so a node whose
/// four choices all say goodbye draws one marker instead of four.</summary>
public readonly record struct ConversationGraphTerminal(int OwnerNodeId, int Column, int Row,
    IReadOnlyList<ConversationGraphEnding> Endings, int AnchorIndex, int AnchorCount);

/// <summary>One drawn connection between two nodes. <c>IsBackward</c> marks a link to a node at the same
/// depth or higher — a loop back into the conversation, routed around the boxes rather than through
/// them.</summary>
public readonly record struct ConversationGraphLink(int FromNodeId, int ToNodeId, int ChoiceIndex,
    bool IsBackward, bool IsSelf, int AnchorIndex, int AnchorCount);

/// <summary>A laid-out conversation: every node and every ending placed, every link classified, and the
/// grid extent.</summary>
public sealed class ConversationGraph
{
    public static readonly ConversationGraph Empty = new([], [], [], 0, 0);

    public ConversationGraph(IReadOnlyList<ConversationGraphPlacement> nodes,
        IReadOnlyList<ConversationGraphTerminal> terminals,
        IReadOnlyList<ConversationGraphLink> links, int columns, int rows)
    {
        Nodes = nodes;
        Terminals = terminals;
        Links = links;
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<ConversationGraphPlacement> Nodes { get; }
    public IReadOnlyList<ConversationGraphTerminal> Terminals { get; }
    public IReadOnlyList<ConversationGraphLink> Links { get; }

    /// <summary>Grid extent. Rows counts the blank separator band before any orphans, so the renderer gets
    /// the gap for free.</summary>
    public int Columns { get; }
    public int Rows { get; }

    public ConversationGraphPlacement? Find(int nodeId)
    {
        foreach (var p in Nodes) if (p.NodeId == nodeId) return p;
        return null;
    }

    public ConversationGraphTerminal? FindTerminal(int ownerNodeId)
    {
        foreach (var t in Terminals) if (t.OwnerNodeId == ownerNodeId) return t;
        return null;
    }
}

/// <summary>
/// Places a dialogue graph on a grid. Split out of the control the way <see cref="RampOverlay"/> is split out
/// of the tile grid: no Avalonia type appears here, so the whole placement can be unit-tested.
///
/// <para>Positions are DERIVED, never authored — a breadth-first sweep from the root puts each node one row
/// below whichever node first reached it, and columns fall out of the order branches are taken. Nothing is
/// stored on the record, so a conversation authored in the text form draws correctly the first time it is
/// opened as a graph, and rearranging choices rearranges the picture rather than leaving stale coordinates
/// behind.</para>
///
/// <para>Every branch lands somewhere visible. One that leaves the conversation — a goodbye, a shop, a quest
/// list — gets a slot of its own below its node, so no choice simply stops at the edge of the drawing.</para>
///
/// <para>Nodes the root cannot reach are still drawn — below the graph, past a blank band, flagged
/// unreachable. A node nothing points at is the commonest authoring mistake in a conversation and the one
/// a picture is best placed to catch.</para>
/// </summary>
public static class ConversationGraphLayout
{
    /// <summary>Blank rows between the reachable graph and the orphan band.</summary>
    public const int OrphanGapRows = 1;

    /// <summary>The order endings are listed in a node's terminal slot.</summary>
    private static readonly ConversationEndKind[] EndOrder =
        [ConversationEndKind.Ends, ConversationEndKind.OpensShop, ConversationEndKind.OpensQuests];

    // A slot on the grid: a node, or the gathered endings of one.
    private readonly record struct Slot(int NodeId, bool IsTerminal);

    public static ConversationGraph Build(IReadOnlyList<ConversationGraphNode> nodes, int rootNodeId)
    {
        if (nodes.Count == 0) return ConversationGraph.Empty;

        // First id wins on a duplicate. Ids are handed out monotonically and never reused, so this only
        // guards a hand-edited file.
        var byId = new Dictionary<int, ConversationGraphNode>(nodes.Count);
        foreach (var n in nodes) byId.TryAdd(n.Id, n);

        // The same fallback the runtime uses: the named root if it resolves, else the first node.
        int rootId = byId.ContainsKey(rootNodeId) ? rootNodeId : nodes[0].Id;

        var row = new Dictionary<int, int>(nodes.Count);
        var rowSlots = new Dictionary<int, List<Slot>>();

        int deepest = Sweep(byId, row, rowSlots, rootId, 0);
        var reachable = new HashSet<int>(row.Keys);

        // Every orphan component starts on the same band, so several of them sit side by side instead of
        // marching down the canvas one below the next.
        int orphanRow = deepest + 1 + OrphanGapRows;
        foreach (var n in nodes)
        {
            if (row.ContainsKey(n.Id)) continue;
            deepest = Math.Max(deepest, Sweep(byId, row, rowSlots, n.Id, orphanRow));
        }

        // Where each thing leaving a node hangs off its bottom edge. One anchor per link, plus one shared by
        // all of that node's endings, in choice order — so four branches leave from four places rather than
        // fanning out of a single point.
        var anchors = new Dictionary<int, Dictionary<int, int>>();   // node → choice index → anchor index
        var anchorCounts = new Dictionary<int, int>();
        var endingAnchor = new Dictionary<int, int>();               // node → the anchor its endings share
        foreach (var n in nodes)
        {
            if (!row.ContainsKey(n.Id) || anchors.ContainsKey(n.Id)) continue;
            var slots = new Dictionary<int, int>();
            int next = 0;
            for (int i = 0; i < n.Branches.Count; i++)
            {
                var branch = n.Branches[i];
                if (branch.End == ConversationEndKind.None && byId.ContainsKey(branch.TargetNodeId))
                {
                    slots[i] = next++;
                }
                else if (!endingAnchor.ContainsKey(n.Id))
                {
                    endingAnchor[n.Id] = next++;
                }
            }
            anchors[n.Id] = slots;
            anchorCounts[n.Id] = next;
        }

        var links = new List<ConversationGraphLink>();
        foreach (var n in nodes)
        {
            if (!row.TryGetValue(n.Id, out int fromRow)) continue;
            for (int i = 0; i < n.Branches.Count; i++)
            {
                var branch = n.Branches[i];
                if (branch.End != ConversationEndKind.None) continue;
                if (!byId.ContainsKey(branch.TargetNodeId)) continue;   // ends the conversation instead
                bool self = branch.TargetNodeId == n.Id;
                links.Add(new ConversationGraphLink(n.Id, branch.TargetNodeId, i,
                    !self && row[branch.TargetNodeId] <= fromRow, self,
                    anchors[n.Id].GetValueOrDefault(i), anchorCounts[n.Id]));
            }
        }

        // A loop needs room to be drawn in. A link between two neighbors on one row, or a node answering
        // itself, is a curve that has to live somewhere other than through a box — so the neighbor is pushed
        // one column further along and the curve gets a whole column to itself.
        var loops = new HashSet<int>();                    // node ids that answer themselves
        var sideBySide = new HashSet<(int, int)>();        // unordered pairs joined along a row
        foreach (var link in links)
        {
            if (link.IsSelf) loops.Add(link.FromNodeId);
            else if (row[link.FromNodeId] == row[link.ToNodeId])
                sideBySide.Add(Pair(link.FromNodeId, link.ToNodeId));
        }

        var placements = new List<ConversationGraphPlacement>(nodes.Count);
        var terminals = new List<ConversationGraphTerminal>();
        int columns = 0;
        foreach (var (r, slots) in rowSlots)
        {
            int column = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.IsTerminal)
                {
                    terminals.Add(new ConversationGraphTerminal(slot.NodeId, column, r,
                        EndingsOf(byId[slot.NodeId], byId),
                        endingAnchor.GetValueOrDefault(slot.NodeId), anchorCounts.GetValueOrDefault(slot.NodeId, 1)));
                }
                else
                {
                    placements.Add(new ConversationGraphPlacement(slot.NodeId, column, r,
                        slot.NodeId == rootId, reachable.Contains(slot.NodeId)));
                }

                column++;
                if (NeedsRoomAfter(slots, i, loops, sideBySide)) column++;
            }
            columns = Math.Max(columns, column);
        }

        return new ConversationGraph(placements, terminals, links, columns, deepest + 1);
    }

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Whether the slot at <paramref name="i"/> should be followed by an empty column: it answers
    /// itself, or it is joined along the row to whatever comes next.</summary>
    private static bool NeedsRoomAfter(List<Slot> slots, int i, HashSet<int> loops, HashSet<(int, int)> sideBySide)
    {
        var slot = slots[i];
        if (slot.IsTerminal) return false;
        if (loops.Contains(slot.NodeId)) return true;
        if (i + 1 >= slots.Count) return false;
        var next = slots[i + 1];
        return !next.IsTerminal && sideBySide.Contains(Pair(slot.NodeId, next.NodeId));
    }

    /// <summary>How a node's choices leave the conversation, in a fixed order, counted by kind. Empty when
    /// every choice leads to another node.</summary>
    private static IReadOnlyList<ConversationGraphEnding> EndingsOf(ConversationGraphNode node,
        Dictionary<int, ConversationGraphNode> byId)
    {
        var counts = new int[EndOrder.Length];
        foreach (var branch in node.Branches)
        {
            var kind = branch.End;
            // A branch pointing at nothing is a goodbye, which is what the runtime does with it.
            if (kind == ConversationEndKind.None)
            {
                if (byId.ContainsKey(branch.TargetNodeId)) continue;
                kind = ConversationEndKind.Ends;
            }
            for (int i = 0; i < EndOrder.Length; i++) if (EndOrder[i] == kind) counts[i]++;
        }

        var endings = new List<ConversationGraphEnding>(EndOrder.Length);
        for (int i = 0; i < EndOrder.Length; i++)
            if (counts[i] > 0) endings.Add(new ConversationGraphEnding(EndOrder[i], counts[i]));
        return endings;
    }

    private static bool HasEndings(ConversationGraphNode node, Dictionary<int, ConversationGraphNode> byId) =>
        EndingsOf(node, byId).Count > 0;

    // Breadth-first from one node, assigning rows to everything it reaches that has no row yet, and a slot
    // below each node for wherever its choices leave the conversation. Returns the deepest row it touched.
    private static int Sweep(Dictionary<int, ConversationGraphNode> byId, Dictionary<int, int> row,
        Dictionary<int, List<Slot>> rowSlots, int startId, int startRow)
    {
        if (!byId.ContainsKey(startId)) return startRow - 1;

        var queue = new Queue<int>();
        row[startId] = startRow;
        SlotsFor(rowSlots, startRow).Add(new Slot(startId, false));
        queue.Enqueue(startId);
        int deepest = startRow;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            var node = byId[current];
            int below = row[current] + 1;

            // Children first, in choice order, then this node's endings — so the real branches stay in the
            // order the player is offered them and the endings trail them.
            foreach (var branch in node.Branches)
            {
                if (branch.End != ConversationEndKind.None) continue;
                if (!byId.ContainsKey(branch.TargetNodeId) || row.ContainsKey(branch.TargetNodeId)) continue;
                row[branch.TargetNodeId] = below;
                SlotsFor(rowSlots, below).Add(new Slot(branch.TargetNodeId, false));
                queue.Enqueue(branch.TargetNodeId);
                deepest = Math.Max(deepest, below);
            }

            if (HasEndings(node, byId))
            {
                SlotsFor(rowSlots, below).Add(new Slot(current, true));
                deepest = Math.Max(deepest, below);
            }
        }
        return deepest;
    }

    private static List<Slot> SlotsFor(Dictionary<int, List<Slot>> rowSlots, int row)
    {
        if (!rowSlots.TryGetValue(row, out var slots)) rowSlots[row] = slots = [];
        return slots;
    }
}
