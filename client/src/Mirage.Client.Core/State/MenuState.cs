namespace Mirage.Client.Core.State;

/// <summary>Which part of the client flow is active. <c>MenuLogic</c> raises a change here and the
/// shell swaps in the matching <c>IGameScreen</c>.</summary>
public enum MenuState
{
    MainMenu,
    Login,
    NewAccount,
    DeleteAccount,
    ChangePassword,
    CharSelect,
    NewChar,
    Loading,
    Credits,
    InGame,
}
