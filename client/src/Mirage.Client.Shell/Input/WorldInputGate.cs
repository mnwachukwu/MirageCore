namespace Mirage.Client.Shell.Input;

/// <summary>
/// Pure decision for when world input (player movement, item pickup, and the potion + menu hotkeys)
/// must be ignored because a UI surface currently owns typed input. Centralizing the rule keeps every
/// keyboard-capturing surface blocking world input the same way, and lets the behavior be unit-tested
/// without standing up MonoGame. <see cref="Screens.GameplayScreen"/> feeds it the live flags each frame.
/// </summary>
public static class WorldInputGate
{
    /// <param name="chatFocused">The chat input box or chat log has keyboard focus.</param>
    /// <param name="chatOptionsTyping">A chat-tab rename field is open and owns the keyboard.</param>
    /// <param name="anyPanelCapturingInput">An open panel is showing a modal sub-surface that owns
    /// input: a number-prompt text box, a right-click context menu, or a confirm overlay.</param>
    /// <returns>True when world input must be suppressed this frame.</returns>
    public static bool IsSuppressed(bool chatFocused, bool chatOptionsTyping, bool anyPanelCapturingInput)
        => chatFocused || chatOptionsTyping || anyPanelCapturingInput;
}
