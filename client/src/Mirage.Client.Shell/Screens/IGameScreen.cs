using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Screens;

/// <summary>A full-screen client state (login, character select, gameplay, credits …), driven by
/// <see cref="ScreenManager"/>. Exactly one screen is current at a time.</summary>
public interface IGameScreen
{
    /// <summary>Called when this screen becomes current — on push, or when a screen above it pops.</summary>
    void OnEnter();
    /// <summary>Called when this screen stops being current, so it can release per-visit state.</summary>
    void OnExit();
    /// <summary>Per-frame tick. <paramref name="input"/> is an empty state while a modal dialog is up,
    /// so the screen keeps animating without receiving player input.</summary>
    void Update(GameTime gameTime, InputState input);
    /// <summary>Draw at reference (letterboxed) scale; the caller owns begin/end of the batch.</summary>
    void Draw(SpriteBatch sb, SpriteFont font);
}
