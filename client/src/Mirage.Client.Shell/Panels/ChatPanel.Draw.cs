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

/// <summary>Painting the chat window: the log, the input line with its caret and selection, the
/// channel dropdown, and the scrollbar.</summary>
public sealed partial class ChatPanel
{
    public void Draw(SpriteBatch sb, SpriteFont font, long nowMs)
    {
        // Standard window chrome (background + border + title bar) — a locked fixed dock. Its PanelBg
        // supplies the chat's dark backdrop (it sits over the black UI strip); the tab strip / input row
        // paint their own backgrounds over it below.
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ChatPanel_Title), isActive: _focused);

        // Tab strip — drawn between title and log so the layout stays predictable. Hit-testing
        // for the strip happens in HandleTabStripInput (called from Update) against the rects
        // this draw caches.
        DrawTabStrip(sb, font, nowMs);

        // Log area (lines, selection, caret, scrollbar)
        _log.SetBounds(LogAreaBounds());
        _log.Draw(sb, font, nowMs);

        // Bottom row background
        var contentB = _panel.ContentBounds;
        UiHelper.DrawFilledRect(sb,
            new Rectangle(contentB.X, contentB.Bottom - InputH, contentB.Width, InputH),
            UiHelper.ChatInputRowBg);

        // Input box — always drawn so text persists while unfocused
        {
            var inputRect = InputRect();
            UiHelper.DrawFilledRect(sb, inputRect, UiHelper.TextInputBg);
            if (_focused)
                UiHelper.DrawBorder(sb, inputRect, Color.CornflowerBlue);

            const string Prefix = "> ";
            float prefixW = font.MeasureString(Prefix).X;
            float textStartX = inputRect.X + 4 + prefixW;
            float availW = inputRect.Width - 8 - prefixW;

            if (_focused)
            {
                // Clamp caret and anchor to valid range
                _caretIndex = Math.Clamp(_caretIndex, 0, _inputText.Length);
                if (_anchorIndex >= 0) _anchorIndex = Math.Clamp(_anchorIndex, 0, _inputText.Length);

                // Resolve pending click → caret index (deferred from Update because we need font metrics here)
                if (_pendingClickX >= 0)
                {
                    float relX = _pendingClickX - textStartX;
                    _caretIndex = _viewOffset;
                    if (_inputText.Length > _viewOffset)
                    {
                        string vis = _inputText[_viewOffset..];
                        for (int ci = 0; ci < vis.Length; ci++)
                        {
                            float le = ci > 0 ? font.MeasureString(vis[..ci]).X : 0f;
                            float re = font.MeasureString(vis[..(ci + 1)]).X;
                            if (relX < (le + re) / 2f) break;
                            _caretIndex = _viewOffset + ci + 1;
                        }
                    }
                    _pendingClickX = -1;
                }

                // Resolve drag anchor (once, on the first Draw after drag starts)
                if (_inputDragAnchorX >= 0)
                {
                    float relX = _inputDragAnchorX - textStartX;
                    _inputDragAnchorPos = _viewOffset;
                    if (_inputText.Length > _viewOffset)
                    {
                        string vis = _inputText[_viewOffset..];
                        for (int ci = 0; ci < vis.Length; ci++)
                        {
                            float le = ci > 0 ? font.MeasureString(vis[..ci]).X : 0f;
                            float re = font.MeasureString(vis[..(ci + 1)]).X;
                            if (relX < (le + re) / 2f) break;
                            _inputDragAnchorPos = _viewOffset + ci + 1;
                        }
                    }
                    _inputDragAnchorX = -1;
                }
                // While dragging: set anchor when caret diverges, clear it when they meet
                if (_inputDragging && _inputDragAnchorPos >= 0)
                    _anchorIndex = _inputDragAnchorPos != _caretIndex ? _inputDragAnchorPos : -1;

                // Ensure viewOffset <= caretIndex
                _viewOffset = Math.Clamp(_viewOffset, 0, Math.Max(0, _inputText.Length));
                _viewOffset = Math.Min(_viewOffset, _caretIndex);

                // Scroll viewOffset right until the caret fits within availW
                while (availW > 0 && _viewOffset < _caretIndex &&
                       font.MeasureString(_inputText[_viewOffset.._caretIndex]).X > availW)
                {
                    _viewOffset++;
                }
            }

            // Build visible text: trim right until it fits
            string allVis = _inputText.Length > _viewOffset ? _inputText[_viewOffset..] : "";
            int visCnt = allVis.Length;
            while (visCnt > 0 && font.MeasureString(allVis[..visCnt]).X > availW)
                visCnt--;
            string visText = allVis[..visCnt];

            // Selection highlight (drawn behind text, only when focused)
            if (_focused && _anchorIndex >= 0 && _anchorIndex != _caretIndex)
            {
                int selS = Math.Clamp(Math.Min(_caretIndex, _anchorIndex) - _viewOffset, 0, visCnt);
                int selE = Math.Clamp(Math.Max(_caretIndex, _anchorIndex) - _viewOffset, 0, visCnt);
                if (selS < selE)
                {
                    float hx = textStartX + (selS > 0 ? font.MeasureString(visText[..selS]).X : 0f);
                    float hw = font.MeasureString(visText[selS..selE]).X;
                    UiHelper.DrawFilledRect(sb,
                        new Rectangle((int)hx, inputRect.Y + 2, Math.Max(1, (int)hw), inputRect.Height - 4),
                        UiHelper.ChatInputSelectionHighlight);
                }
            }

            // Prefix (only when focused or when there is text) and visible input text
            if (_focused || _inputText.Length > 0)
                sb.DrawString(font, Prefix, new Vector2(inputRect.X + 4, inputRect.Y + 2), Color.White);
            sb.DrawString(font, visText, new Vector2(textStartX, inputRect.Y + 2), Color.White);

            // Blinking caret (1px vertical line, only when focused)
            if (_focused && (nowMs / 500) % 2 == 0)
            {
                int caretOff = Math.Clamp(_caretIndex - _viewOffset, 0, visCnt);
                float cx = textStartX + (caretOff > 0 ? font.MeasureString(visText[..caretOff]).X : 0f);
                UiHelper.DrawFilledRect(sb,
                    new Rectangle((int)cx, inputRect.Y + 2, 1, inputRect.Height - 4),
                    Color.White);
            }
        }

        // Channel dropdown left of the input box: header in the normal layer, popup LAST so an
        // upward-opening list renders on top of the log. Uses the input cached in Update.
        if (_lastInput is not null)
        {
            _channelDropDown.DrawHeader(sb, font, ChannelDropRect(), _lastInput);
            _channelDropDown.DrawPopup(sb, font, ChannelDropRect(), _lastInput);
        }
    }
}
