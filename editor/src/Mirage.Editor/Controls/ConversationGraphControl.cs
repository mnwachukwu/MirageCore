using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;

namespace Mirage.Editor.Controls;

/// <summary>
/// A conversation drawn as the branching thing it is: one box per dialogue node, one curve per choice that
/// leads somewhere, and a marker for every choice that leaves the conversation. Clicking a box raises
/// <see cref="NodeActivatedCommand"/> with that node's view model, which is how the node is edited —
/// nothing is authored on the canvas itself, so there is no drag-to-connect and no coordinates to keep in
/// the record.
///
/// <para>The wheel zooms about the pointer and a drag on the canvas pans it, so the view is a camera over a
/// drawing rather than a scrolled page. A click that never moved is what opens a node, which lets the drag
/// start anywhere, over a box or not.</para>
///
/// <para>Everything drawn is derived from the conversation on every layout pass
/// (<see cref="ConversationGraphLayout"/>), so the picture cannot disagree with the data. The two things
/// worth spotting at a glance are drawn differently rather than annotated: the opening node wears the
/// accent, and a node the opening node cannot reach sits below a gap in the bad color.</para>
///
/// <para>Rendered rather than composed from templates. The editor resolves its bindings by reflection, so a
/// canvas built out of an ItemsControl and a DataTemplate would fail silently a control at a time; here the
/// only binding surface is the two properties below.</para>
/// </summary>
public sealed class ConversationGraphControl : Control
{
    // ── Metrics ───────────────────────────────────────────────────────────────
    private const double NodeW = 210;
    private const double NodeH = 118;
    private const double HGap = 34;
    private const double VGap = 76;
    // Wide enough to hold a loop that bows off the leftmost column or over the top row.
    private const double Pad = 60;
    private const double Corner = 6;
    private const double InnerPad = 12;

    // The node box, top to bottom: an id row, a rule, the speech, and a footer pinned to the bottom.
    private const double HeadHeight = 20;
    private const double FootHeight = 18;
    private const double SpeakerHeight = 16;

    private const double IdFontSize = 11;
    private const double BodyFontSize = 12;
    private const double BadgeFontSize = 10;
    private const double LinkLabelFontSize = 10;

    // An ending marker: small, stacked inside one grid slot under the node it belongs to.
    private const double EndW = 128;
    private const double EndH = 26;
    private const double EndGap = 6;

    private const int LinkLabelMaxChars = 20;
    private const double LabelPlatePadX = 4;
    private const double LabelPlatePadY = 1;
    private const double ArrowLength = 8;
    private const double ArrowHalfWidth = 4.5;
    // How far out a loop back up the graph bows before it comes down again.
    private const double BackLinkBow = 52;
    // How far an arc between two nodes on one row bows off the row. Leftward bows below, rightward above.
    private const double SameRowArc = 34;
    private const double SelfLinkRadius = 24;
    // Room past the last column for a loop hanging off its right edge.
    private const double LoopGutter = 80;

    private const double MinZoom = 0.3;
    private const double MaxZoom = 2.0;
    private const double ZoomStep = 1.12;
    private const double DragSlop = 4;

    // ── Palette (the Mirage ramp; a rendered control resolves no resources) ───
    private static readonly IBrush CanvasBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x16, 0x34));
    private static readonly IBrush BoxBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x1B, 0x3D));
    private static readonly IBrush BoxHoverBrush = new SolidColorBrush(Color.FromRgb(0x29, 0x21, 0x48));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xEA, 0xF8));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xA2, 0xC9));
    private static readonly IBrush FaintBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x73, 0x9F));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xF5));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));

    private static readonly Pen LinePen = new(new SolidColorBrush(Color.FromRgb(0x3B, 0x32, 0x66)), 1);
    private static readonly Pen RulePen = new(new SolidColorBrush(Color.FromRgb(0x30, 0x2A, 0x55)), 1);
    private static readonly Pen RootPen = new(new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xF5)), 1.6);
    private static readonly Pen OrphanPen = new(new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), 1.4);
    private static readonly Pen LinkPen = new(new SolidColorBrush(Color.FromArgb(0xBB, 0x7A, 0x73, 0x9F)), 1.4);
    private static readonly Pen LinkHotPen = new(new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xF5)), 2.0);
    private static readonly IBrush ArrowBrush = new SolidColorBrush(Color.FromArgb(0xBB, 0x7A, 0x73, 0x9F));
    private static readonly IBrush ArrowHotBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xF5));
    // A label may land on a box; the edge is what keeps it reading as a label rather than as box text.
    private static readonly Pen LabelPlatePen = new(new SolidColorBrush(Color.FromArgb(0x77, 0x3B, 0x32, 0x66)), 1);

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor GrabCursor = new(StandardCursorType.SizeAll);

    // ── Bound state ───────────────────────────────────────────────────────────

    public static readonly StyledProperty<ConversationRowViewModel?> ConversationProperty =
        AvaloniaProperty.Register<ConversationGraphControl, ConversationRowViewModel?>(nameof(Conversation));

    /// <summary>Invoked with the clicked <see cref="ConversationNodeRowViewModel"/>.</summary>
    public static readonly StyledProperty<ICommand?> NodeActivatedCommandProperty =
        AvaloniaProperty.Register<ConversationGraphControl, ICommand?>(nameof(NodeActivatedCommand));

    public ConversationRowViewModel? Conversation
    {
        get => GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    public ICommand? NodeActivatedCommand
    {
        get => GetValue(NodeActivatedCommandProperty);
        set => SetValue(NodeActivatedCommandProperty, value);
    }

    static ConversationGraphControl()
    {
        ConversationProperty.Changed.AddClassHandler<ConversationGraphControl>((c, e) =>
        {
            if (e.OldValue is ConversationRowViewModel old) old.GraphChanged -= c.OnGraphChanged;
            if (e.NewValue is ConversationRowViewModel fresh) fresh.GraphChanged += c.OnGraphChanged;
            // A different conversation is a different drawing; the camera from the last one means nothing.
            c._framed = false;
            c.OnGraphChanged();
        });
    }

    public ConversationGraphControl()
    {
        ClipToBounds = true;
        Focusable = false;
        Cursor = ArrowCursor;
    }

    private ConversationGraph _graph = ConversationGraph.Empty;
    private readonly Dictionary<int, Rect> _boxes = [];
    private int _hoverNodeId;

    // The camera. World coordinates are what the layout produces; these put them on screen.
    private double _zoom = 1.0;
    private Point _pan;
    private bool _framed;

    private bool _dragging;
    private bool _dragMoved;
    private Point _dragFrom;
    private int _pressedNodeId;

    private void OnGraphChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // The badges and the ending markers are resolved inside Render, and Avalonia is retained-mode — without
    // a forced repaint they would keep the old language until some unrelated interaction invalidated the
    // control.
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        EditorStrings.LanguageChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        EditorStrings.LanguageChanged -= InvalidateVisual;
        base.OnDetachedFromLogicalTree(e);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var conv = Conversation;
        _graph = conv is null
            ? ConversationGraph.Empty
            : ConversationGraphLayout.Build(conv.GraphNodes(), conv.RootNodeId);

        _boxes.Clear();
        foreach (var p in _graph.Nodes) _boxes[p.NodeId] = SlotRect(p.Column, p.Row);

        // The canvas is a viewport, not a scrolled page: it takes whatever room it is given and the camera
        // moves over the drawing.
        return new Size(
            double.IsInfinity(availableSize.Width) ? WorldExtent().Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? WorldExtent().Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!_framed && finalSize.Width > 0 && finalSize.Height > 0) FrameGraph(finalSize);
        return finalSize;
    }

    private static Rect SlotRect(int column, int row) =>
        new(Pad + column * (NodeW + HGap), Pad + row * (NodeH + VGap), NodeW, NodeH);

    private Size WorldExtent()
    {
        if (_graph.Nodes.Count == 0) return new Size(0, 0);

        // A loop hanging off the rightmost column needs to be inside the extent, or framing the graph would
        // leave part of the curve off screen.
        double gutter = 0;
        foreach (var link in _graph.Links)
        {
            if (link.IsSelf || link.IsBackward) { gutter = LoopGutter; break; }
        }

        return new Size(Pad * 2 + _graph.Columns * (NodeW + HGap) - HGap + gutter,
                        Pad * 2 + _graph.Rows * (NodeH + VGap) - VGap);
    }

    /// <summary>Puts the whole drawing in view when a conversation is first shown: shrink to fit if it is
    /// larger than the canvas, never magnify, and center what is left over.</summary>
    private void FrameGraph(Size viewport)
    {
        _framed = true;
        var extent = WorldExtent();
        if (extent.Width <= 0 || extent.Height <= 0)
        {
            _zoom = 1.0;
            _pan = default;
            return;
        }

        _zoom = Math.Clamp(Math.Min(viewport.Width / extent.Width, viewport.Height / extent.Height), MinZoom, 1.0);
        _pan = new Point(
            (viewport.Width - extent.Width * _zoom) / 2,
            Math.Max(0, (viewport.Height - extent.Height * _zoom) / 2));
    }

    private Point ToWorld(Point screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    // ── Input ─────────────────────────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;

        var screen = e.GetPosition(this);
        var anchor = ToWorld(screen);
        double zoom = Math.Clamp(_zoom * Math.Pow(ZoomStep, e.Delta.Y), MinZoom, MaxZoom);
        if (Math.Abs(zoom - _zoom) < double.Epsilon) return;

        _zoom = zoom;
        // Keep whatever was under the pointer under the pointer.
        _pan = new Point(screen.X - anchor.X * _zoom, screen.Y - anchor.Y * _zoom);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var screen = e.GetPosition(this);

        if (_dragging)
        {
            var delta = screen - _dragFrom;
            if (!_dragMoved && Math.Abs(delta.X) + Math.Abs(delta.Y) < DragSlop) return;
            _dragMoved = true;
            Cursor = GrabCursor;
            _pan += delta;
            _dragFrom = screen;
            InvalidateVisual();
            return;
        }

        int hit = NodeAt(ToWorld(screen));
        if (hit == _hoverNodeId) return;
        _hoverNodeId = hit;
        Cursor = hit == 0 ? ArrowCursor : HandCursor;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverNodeId == 0) return;
        _hoverNodeId = 0;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;

        // A double click on bare canvas puts the camera back where it started, which is the way out of a
        // zoom that went too far.
        if (e.ClickCount == 2 && NodeAt(ToWorld(point.Position)) == 0)
        {
            _framed = false;
            InvalidateArrange();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragFrom = point.Position;
        _pressedNodeId = NodeAt(ToWorld(point.Position));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        Cursor = _pressedNodeId == 0 ? ArrowCursor : HandCursor;

        // A press that never moved is a click, wherever it started. That is what lets a drag begin over a
        // node without opening it.
        if (!_dragMoved && _pressedNodeId != 0 && NodeAt(ToWorld(e.GetPosition(this))) == _pressedNodeId)
            Activate(_pressedNodeId);

        _pressedNodeId = 0;
        e.Handled = true;
    }

    private void Activate(int nodeId)
    {
        var node = NodeVm(nodeId);
        if (node is null) return;
        var command = NodeActivatedCommand;
        if (command is not null && command.CanExecute(node)) command.Execute(node);
    }

    private int NodeAt(Point world)
    {
        foreach (var (id, rect) in _boxes) if (rect.Contains(world)) return id;
        return 0;
    }

    private ConversationNodeRowViewModel? NodeVm(int nodeId)
    {
        var conv = Conversation;
        if (conv is null) return null;
        foreach (var n in conv.Nodes) if (n.NodeId == nodeId) return n;
        return null;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    // Choice labels, held back until the boxes are down. A label is an annotation on the whole picture, so
    // it goes on top of it — drawn in the link pass it would be painted over by the very node it points at.
    private readonly List<(Rect Plate, FormattedText Text)> _labels = [];

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(CanvasBrush, new Rect(Bounds.Size));
        if (_graph.Nodes.Count == 0) return;

        using var _ = ctx.PushTransform(
            Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y));

        _labels.Clear();
        // Links first: a curve passing near a box should read as going behind it.
        foreach (var link in _graph.Links) DrawLink(ctx, link);
        foreach (var terminal in _graph.Terminals) DrawTerminal(ctx, terminal);
        foreach (var placement in _graph.Nodes) DrawNode(ctx, placement);

        foreach (var (plate, text) in _labels)
        {
            ctx.DrawRectangle(CanvasBrush, LabelPlatePen, plate, 3, 3);
            ctx.DrawText(text, new Point(plate.X + LabelPlatePadX, plate.Y + LabelPlatePadY));
        }
    }

    /// <summary>Where something leaving <paramref name="box"/> hangs off its bottom edge. Anchors are spread
    /// across the edge so each branch leaves from its own place rather than every one of them fanning out of
    /// the middle.</summary>
    private static Point ExitAnchor(Rect box, int index, int count)
    {
        int total = Math.Max(count, 1);
        return new Point(box.X + box.Width * (index + 1) / (total + 1), box.Bottom);
    }

    private void DrawNode(DrawingContext ctx, ConversationGraphPlacement placement)
    {
        if (!_boxes.TryGetValue(placement.NodeId, out var box)) return;
        var node = NodeVm(placement.NodeId);

        bool hot = placement.NodeId == _hoverNodeId;
        var pen = !placement.IsReachable ? OrphanPen : placement.IsRoot ? RootPen : LinePen;
        ctx.DrawRectangle(hot ? BoxHoverBrush : BoxBrush, pen, box, Corner, Corner);

        double left = box.X + InnerPad;
        double innerW = box.Width - InnerPad * 2;

        // Head: the id, and the one word worth saying about this node.
        Draw(ctx, $"#{placement.NodeId}", left, box.Y + 6, innerW, IdFontSize, FaintBrush);
        string? badge = !placement.IsReachable
            ? EditorStrings.Get(EditorStrings.ConversationEditor_GraphUnreachable)
            : placement.IsRoot ? EditorStrings.Get(EditorStrings.ConversationEditor_GraphStart) : null;
        if (badge is not null)
        {
            var text = Format(badge, BadgeFontSize, placement.IsReachable ? AccentBrush : BadBrush);
            ctx.DrawText(text, new Point(box.Right - InnerPad - text.Width, box.Y + 7));
        }

        double bodyTop = box.Y + HeadHeight;
        ctx.DrawLine(RulePen, new Point(left, bodyTop), new Point(box.Right - InnerPad, bodyTop));
        bodyTop += 6;

        // The speaker override only when it is set: blank means the attached NPC, which the conversation
        // header already names.
        string speaker = node?.Speaker.Trim() ?? "";
        if (speaker.Length > 0)
        {
            Draw(ctx, speaker, left, bodyTop, innerW, IdFontSize, MutedBrush);
            bodyTop += SpeakerHeight;
        }

        double footTop = box.Bottom - FootHeight;
        string spoken = node?.Text.Trim() ?? "";
        if (spoken.Length > 0)
        {
            var body = Format(spoken, BodyFontSize, TextBrush, innerW);
            body.MaxTextHeight = Math.Max(BodyFontSize, footTop - bodyTop - 2);
            ctx.DrawText(body, new Point(left, bodyTop));
        }

        int choices = node?.Choices.Count ?? 0;
        Draw(ctx, EditorStrings.Format(EditorStrings.ConversationEditor_GraphChoiceCount, ("Count", choices)),
            left, footTop, innerW, IdFontSize, FaintBrush);
    }

    /// <summary>The ways one node's choices leave the conversation, stacked in the slot below it.</summary>
    private void DrawTerminal(DrawingContext ctx, ConversationGraphTerminal terminal)
    {
        var slot = SlotRect(terminal.Column, terminal.Row);
        double x = slot.X + (slot.Width - EndW) / 2;
        double y = slot.Y;
        bool hot = terminal.OwnerNodeId == _hoverNodeId;

        // One stub from the owning node down to the first marker; the rest sit under it.
        if (_boxes.TryGetValue(terminal.OwnerNodeId, out var owner))
        {
            var start = ExitAnchor(owner, terminal.AnchorIndex, terminal.AnchorCount);
            var end = new Point(x + EndW / 2, y);
            double lift = (end.Y - start.Y) * 0.55;
            DrawCurve(ctx, start, new Point(start.X, start.Y + lift), new Point(end.X, end.Y - lift), end,
                hot ? LinkHotPen : LinkPen, new Point(0, 1), hot ? ArrowHotBrush : ArrowBrush);
        }

        foreach (var ending in terminal.Endings)
        {
            var chip = new Rect(x, y, EndW, EndH);
            var ink = ending.Kind switch
            {
                ConversationEndKind.OpensShop => OkBrush,
                ConversationEndKind.OpensQuests => WarnBrush,
                _ => FaintBrush,
            };
            ctx.DrawRectangle(BoxBrush, new Pen(ink, hot ? 1.4 : 1.0), chip, EndH / 2, EndH / 2);

            string label = EditorStrings.Get(ending.Kind switch
            {
                ConversationEndKind.OpensShop => EditorStrings.ConversationEditor_GraphOpensShop,
                ConversationEndKind.OpensQuests => EditorStrings.ConversationEditor_GraphOpensQuests,
                _ => EditorStrings.ConversationEditor_GraphEnds,
            });
            if (ending.Count > 1) label = $"{label} ×{ending.Count}";

            var text = Format(label, IdFontSize, ink, EndW - 12);
            ctx.DrawText(text, new Point(chip.X + (EndW - text.Width) / 2, chip.Y + (EndH - text.Height) / 2));
            y += EndH + EndGap;
        }
    }

    private void DrawLink(DrawingContext ctx, ConversationGraphLink link)
    {
        if (!_boxes.TryGetValue(link.FromNodeId, out var from)) return;
        if (!_boxes.TryGetValue(link.ToNodeId, out var to)) return;

        bool hot = link.FromNodeId == _hoverNodeId || link.ToNodeId == _hoverNodeId;
        var pen = hot ? LinkHotPen : LinkPen;
        var arrow = hot ? ArrowHotBrush : ArrowBrush;

        // Stagger by choice index so two branches out of one node do not lay their labels on each other.
        double along = 0.5 + (link.ChoiceIndex % 3 - 1) * 0.13;

        Point start, end, control1, control2, tip;
        if (link.IsSelf)
        {
            // A loop off the right edge and back into it — a choice that returns to the node offering it.
            start = new Point(from.Right, from.Y + from.Height * 0.35);
            end = new Point(from.Right, from.Y + from.Height * 0.65);
            control1 = new Point(from.Right + SelfLinkRadius * 2, start.Y - SelfLinkRadius);
            control2 = new Point(from.Right + SelfLinkRadius * 2, end.Y + SelfLinkRadius);
            tip = new Point(-1, 0);
        }
        else if (link.IsBackward)
        {
            // Leave by whichever side faces the target. Going the other way round would run the curve
            // straight under the boxes between them, where it is invisible. Two nodes in the same column go
            // RIGHT: the leftmost column has only the margin to its left, and a loop off it would sit outside
            // the drawing.
            bool leftward = to.Center.X < from.Center.X;
            int side = leftward ? -1 : 1;
            start = new Point(leftward ? from.X : from.Right, from.Center.Y);

            if (Math.Abs(to.Y - from.Y) < 1)
            {
                // A sibling along the row, which the layout has left a clear column for. The control points
                // sit BETWEEN the two edges, so this reads as a shallow arc rather than as a V bulging away
                // from the node it is heading for.
                end = new Point(leftward ? to.Right : to.X, to.Center.Y);
                // Each direction takes its own side of the row, so two siblings answering each other draw as
                // two arcs instead of one on top of the other.
                double reach = (leftward ? SameRowArc : -SameRowArc) + side * link.ChoiceIndex * 8;
                double span = end.X - start.X;
                control1 = new Point(start.X + span * 0.25, start.Y + reach);
                control2 = new Point(start.X + span * 0.75, end.Y + reach);
                tip = new Point(side, 0);
            }
            else
            {
                // Climbing back up: out the side, past the rows between, and in through the target's floor.
                double bow = BackLinkBow + link.ChoiceIndex * 12;
                end = new Point(leftward ? to.X + to.Width * 0.3 : to.Right - to.Width * 0.3, to.Bottom);
                control1 = new Point(start.X + side * bow, start.Y);
                control2 = new Point(end.X + side * bow, end.Y + bow);
                tip = new Point(0, -1);
            }
        }
        else
        {
            start = ExitAnchor(from, link.AnchorIndex, link.AnchorCount);
            end = new Point(to.Center.X, to.Y);
            double lift = (end.Y - start.Y) * 0.55;
            control1 = new Point(start.X, start.Y + lift);
            control2 = new Point(end.X, end.Y - lift);
            tip = new Point(0, 1);
        }

        DrawCurve(ctx, start, control1, control2, end, pen, tip, arrow);

        string label = LinkLabel(link);
        if (label.Length == 0) return;
        var text = Format(label, LinkLabelFontSize, hot ? TextBrush : MutedBrush);
        var at = Bezier(start, control1, control2, end, along);
        // Held back for the final pass. On a plate, because a label crossing the curve it belongs to is
        // unreadable and a label crossing ANOTHER branch's curve is worse.
        _labels.Add((new Rect(
            at.X - text.Width / 2 - LabelPlatePadX, at.Y - text.Height / 2 - LabelPlatePadY,
            text.Width + LabelPlatePadX * 2, text.Height + LabelPlatePadY * 2), text));
    }

    private static void DrawCurve(DrawingContext ctx, Point start, Point control1, Point control2, Point end,
        Pen pen, Point tip, IBrush arrow)
    {
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(start, false);
            sink.CubicBezierTo(control1, control2, end);
            sink.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geometry);
        DrawArrowHead(ctx, end, tip, arrow);
    }

    private string LinkLabel(ConversationGraphLink link)
    {
        var node = NodeVm(link.FromNodeId);
        if (node is null || link.ChoiceIndex >= node.Choices.Count) return "";
        string label = node.Choices[link.ChoiceIndex].Label.Trim();
        if (label.Length <= LinkLabelMaxChars) return label;
        return label[..LinkLabelMaxChars] + "…";
    }

    private static void DrawArrowHead(DrawingContext ctx, Point at, Point direction, IBrush brush)
    {
        // direction is one of the four axis unit vectors, so the perpendicular is a swap of components.
        var back = new Point(at.X - direction.X * ArrowLength, at.Y - direction.Y * ArrowLength);
        var side = new Point(direction.Y * ArrowHalfWidth, direction.X * ArrowHalfWidth);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(at, true);
            sink.LineTo(new Point(back.X + side.X, back.Y + side.Y));
            sink.LineTo(new Point(back.X - side.X, back.Y - side.Y));
            sink.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geometry);
    }

    private static Point Bezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
        return new Point(
            a * p0.X + b * p1.X + c * p2.X + d * p3.X,
            a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    private static void Draw(DrawingContext ctx, string text, double x, double y, double width,
        double size, IBrush brush)
    {
        if (text.Length == 0) return;
        var formatted = Format(text, size, brush, width);
        formatted.MaxTextHeight = size * 1.6;
        ctx.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText Format(string text, double size, IBrush brush, double? width = null)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush)
        {
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (width is { } w) formatted.MaxTextWidth = w;
        return formatted;
    }
}
