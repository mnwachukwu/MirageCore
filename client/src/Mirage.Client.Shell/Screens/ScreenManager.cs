using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Screens;

/// <summary>A stack of <see cref="IGameScreen"/>s where only the top one updates and draws.
/// <para>Screens are stacked rather than swapped so a screen can be layered over another and popped
/// back to it; <see cref="IGameScreen.OnEnter"/> fires again on the revealed screen.</para></summary>
public sealed class ScreenManager
{
    private readonly Stack<IGameScreen> _stack = new();

    /// <summary>The screen on top of the stack, or null before the first push.</summary>
    public IGameScreen? Current => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>Layer a screen on top, keeping the one beneath it for a later <see cref="Pop"/>.</summary>
    public void Push(IGameScreen screen)
    {
        _stack.Push(screen);
        screen.OnEnter();
    }

    /// <summary>Drop the top screen and re-enter the one below. No-op on an empty stack.</summary>
    public void Pop()
    {
        if (_stack.Count == 0) return;
        var old = _stack.Pop();
        old.OnExit();
        Current?.OnEnter();
    }

    /// <summary>Swap the top screen for a new one without growing the stack — the usual transition
    /// between peer screens such as login to character select.</summary>
    public void Replace(IGameScreen screen)
    {
        if (_stack.Count > 0)
        {
            var old = _stack.Pop();
            old.OnExit();
        }
        _stack.Push(screen);
        screen.OnEnter();
    }

    public void Update(GameTime gameTime, InputState input) => Current?.Update(gameTime, input);
    public void Draw(SpriteBatch sb, SpriteFont font) => Current?.Draw(sb, font);
}
