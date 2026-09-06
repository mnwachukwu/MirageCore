using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>Which credential flow the pre-game menu is currently collecting input for.</summary>
public enum AuthFlow { None, Login, NewAccount, ChangePassword, DeleteAccount }

/// <summary>
/// Pre-game state machine that listens to <see cref="IClientEvents"/> and drives
/// <see cref="MenuState"/> transitions.  Shell subscribes to <see cref="MenuLogic.StateChanged"/>
/// and swaps screens accordingly.
/// </summary>
public sealed class MenuLogic
{
    public MenuState CurrentState { get; private set; } = MenuState.MainMenu;

    /// <summary>
    /// Which screen most recently issued an auth request and is awaiting the server response.
    /// Used by the alert handler to route the user back to the correct screen after an error
    /// (e.g. wrong password from the change-password screen returns there, not to Login).
    /// </summary>
    public AuthFlow LastAuthFlow { get; set; } = AuthFlow.None;

    /// <summary>Fires whenever the active state changes.</summary>
    public event Action<MenuState>? StateChanged;

    /// <summary>
    /// Fires when the server sends an alert message (bad password, server full, etc.).
    /// Shell should display a dialog or transition back to the relevant screen.
    /// </summary>
    public event Action<string, AlertCode>? AlertReceived;

    // True only when we entered Loading specifically to fetch classes for the new-char screen.
    private bool _loadingForNewChar;

    public MenuLogic(IClientEvents events)
    {
        events.AlertMessage += (msg, code) => AlertReceived?.Invoke(msg, code);
        events.CharacterListReceived += () => Transition(MenuState.CharSelect);
        events.ClassListReceived += OnClassListReceived;
        events.InGame += () => Transition(MenuState.InGame);
    }

    // ── Server-driven transitions ─────────────────────────────────────────────

    private void OnClassListReceived()
    {
        // Classes are sent both during the new-char flow (explicit GetClasses request)
        // and as part of normal join data when entering the game.  Only advance to the
        // new-char screen when we specifically requested them for that purpose.
        if (CurrentState == MenuState.Loading && _loadingForNewChar)
            Transition(MenuState.NewChar);
    }

    // ── Shell-driven transitions ──────────────────────────────────────────────

    public void GoToMainMenu() => Transition(MenuState.MainMenu);
    public void GoToLogin() => Transition(MenuState.Login);
    public void GoToNewAccount() => Transition(MenuState.NewAccount);
    public void GoToDeleteAccount() => Transition(MenuState.DeleteAccount);
    public void GoToChangePassword() => Transition(MenuState.ChangePassword);
    /// <summary>
    /// Transition to the Loading state while waiting for a server response.
    /// The message is surfaced to the Shell via <see cref="LoadingMessageChanged"/>.
    /// </summary>
    public void GoToLoading(string message = "")
    {
        _loadingForNewChar = false;
        LoadingMessageChanged?.Invoke(message);
        Transition(MenuState.Loading);
    }

    /// <summary>
    /// Transition to the Loading state in preparation for the new-character screen.
    /// When the server responds with the class list, <see cref="MenuState.NewChar"/>
    /// will be entered automatically.
    /// </summary>
    public void GoToLoadingForNewChar(string message = "")
    {
        _loadingForNewChar = true;
        LoadingMessageChanged?.Invoke(message);
        Transition(MenuState.Loading);
    }

    /// <summary>Fires when the loading screen message text should change.</summary>
    public event Action<string>? LoadingMessageChanged;

    // ── Private ───────────────────────────────────────────────────────────────

    private void Transition(MenuState next)
    {
        if (CurrentState == next) return;
        CurrentState = next;
        StateChanged?.Invoke(next);
    }
}
