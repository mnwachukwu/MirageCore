using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The NPC conversation (dialogue-tree) window. Opened server-driven from an NPC interaction that resolved
/// talk-first (or a context-menu "Talk"); the client holds the cached tree and walks it locally — pure-text
/// choices navigate between nodes with NO round-trip, while a terminal hand-off choice closes the panel and
/// re-issues an NpcInteract so the server opens the keeper shop/inn or the quest menu (re-validating proximity).
/// </summary>
public sealed class ConversationPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 320, 240), minH: 160, minW: 260);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    private int _convNum;
    private int _nodeId;   // 0 → resolve the tree's root
    private int _map, _slot;
    private readonly List<Button> _choiceBtns = new();
    private InputState _input = new();
    private string _leaveLabel = "Leave";
    private int _labelsGeneration = -1;

    private const int Pad = 8;
    private const int LineH = 14;
    private const int BtnH = 20;
    private const int BtnGap = 4;

    /// <summary>Open conversation <paramref name="convNum"/> at the NPC (map, slot), starting at its root node.</summary>
    public void Open(int convNum, int map, int slot)
    {
        _convNum = convNum;
        _map = map;
        _slot = slot;
        _nodeId = 0;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    private ConversationRecord? Def(ClientState state) =>
        _convNum >= 1 && _convNum < state.ConvDefs.Length ? state.ConvDefs[_convNum] : null;

    private ConversationNode? CurrentNode(ClientState state)
    {
        var def = Def(state);
        if (def is null) return null;
        return _nodeId > 0 ? def.NodeById(_nodeId) ?? def.RootNode : def.RootNode;
    }

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        var def = Def(state);
        var node = CurrentNode(state);
        if (def is null || node is null)
        {
            IsOpen = false;
            return;
        }

        int count = node.Choices.Count > 0 ? node.Choices.Count : 1;   // no choices → a single "Leave"
        LayoutChoiceButtons(_panel.ContentBounds, count);

        for (int i = 0; i < count; i++)
        {
            if (!_choiceBtns[i].IsClicked(input)) continue;
            if (node.Choices.Count == 0)
            {
                IsOpen = false;
                return;
            }  // the synthesized "Leave"
            var ch = node.Choices[i];
            if (ch.Action == ConversationAction.OpenShop)
            {
                sender.SendNpcInteract(_map, _slot, NpcInteractChoice.Shop);
                IsOpen = false;
            }
            else if (ch.Action == ConversationAction.OpenQuests)
            {
                sender.SendNpcInteract(_map, _slot, NpcInteractChoice.Quest);
                IsOpen = false;
            }
            else if (ch.NextNodeId <= 0 || def.NodeById(ch.NextNodeId) is null)
            {
                IsOpen = false;   // end of conversation
            }
            else
            {
                _nodeId = ch.NextNodeId;   // navigate (local, no round-trip)
            }
            return;   // one click per frame; the button bounds are stale after a node change
        }
    }

    private void LayoutChoiceButtons(Rectangle content, int count)
    {
        while (_choiceBtns.Count < count) _choiceBtns.Add(new Button());
        int stackTop = content.Bottom - Pad - count * (BtnH + BtnGap);
        for (int i = 0; i < count; i++)
        {
            _choiceBtns[i].Bounds = new Rectangle(content.X + Pad, stackTop + i * (BtnH + BtnGap),
                content.Width - Pad * 2, BtnH);
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive)
    {
        if (!IsOpen) return;
        var def = Def(state);
        var node = CurrentNode(state);
        if (def is null || node is null)
        {
            IsOpen = false;
            return;
        }

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _leaveLabel = ClientStrings.Get(ClientStrings.ConversationPanel_Leave);
        }

        // Title = who's speaking: the node's Speaker override, else the attached NPC's name, else a generic.
        string speaker = node.Speaker.TrimEnd();
        if (speaker.Length == 0)
        {
            speaker = def.SpeakerNpc >= 1 && def.SpeakerNpc < state.NpcDefs.Length
                ? (state.NpcDefs[def.SpeakerNpc]?.Name?.TrimEnd() ?? "") : "";
        }

        if (speaker.Length == 0) speaker = ClientStrings.Get(ClientStrings.ConversationPanel_Title);

        _panel.Draw(sb, font, speaker, isActive);
        var c = _panel.ContentBounds;

        // The spoken line (word-wrapped to the content width), above the choice buttons.
        DrawWrapped(sb, font, node.Text, c.X + Pad, c.Y + Pad, c.Width - Pad * 2, Color.White);

        int count = node.Choices.Count > 0 ? node.Choices.Count : 1;
        LayoutChoiceButtons(c, count);
        for (int i = 0; i < count; i++)
        {
            _choiceBtns[i].Label = node.Choices.Count == 0
                ? _leaveLabel
                : (node.Choices[i].Label.TrimEnd().Length > 0 ? node.Choices[i].Label.TrimEnd() : _leaveLabel);
            _choiceBtns[i].Draw(sb, font, _input);
        }
        _panel.DrawOverlay(sb);
    }

    private static void DrawWrapped(SpriteBatch sb, SpriteFont font, string text, float x, float y, float maxWidth, Color color)
    {
        UiHelper.DrawWrapped(sb, font, text, x, y, maxWidth, color, LineH);
    }
}
