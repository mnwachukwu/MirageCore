using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>The pre-game screens: login, main menu, account create/change/delete, character
/// select and creation, credits, loading, and the gameplay screen itself.</summary>
public static partial class ClientStrings
{
    // ── LoginScreen ───────────────────────────────────────────────────────────
    public const string LoginScreen_Title = nameof(LoginScreen_Title);
    public const string LoginScreen_Instruction = nameof(LoginScreen_Instruction);
    public const string LoginScreen_ChangePasswordLink = nameof(LoginScreen_ChangePasswordLink);
    public const string LoginScreen_RememberMe = nameof(LoginScreen_RememberMe);
    public const string LoginScreen_RememberMeWarning = nameof(LoginScreen_RememberMeWarning);
    public const string LoginScreen_ConnectButton = nameof(LoginScreen_ConnectButton);
    public const string LoginScreen_LoggingIn = nameof(LoginScreen_LoggingIn);

    // ── MainMenuScreen ────────────────────────────────────────────────────────
    public const string MainMenuScreen_LoginButton = nameof(MainMenuScreen_LoginButton);
    public const string MainMenuScreen_NewAccountButton = nameof(MainMenuScreen_NewAccountButton);
    public const string MainMenuScreen_DeleteAccountButton = nameof(MainMenuScreen_DeleteAccountButton);
    public const string MainMenuScreen_CreditsButton = nameof(MainMenuScreen_CreditsButton);
    public const string MainMenuScreen_QuitButton = nameof(MainMenuScreen_QuitButton);

    // ── NewAccountScreen ──────────────────────────────────────────────────────
    public const string NewAccountScreen_Title = nameof(NewAccountScreen_Title);
    public const string NewAccountScreen_Instruction = nameof(NewAccountScreen_Instruction);
    public const string NewAccountScreen_ConfirmLabel = nameof(NewAccountScreen_ConfirmLabel);
    public const string NewAccountScreen_CreatingAccount = nameof(NewAccountScreen_CreatingAccount);

    // ── ChangePasswordScreen ──────────────────────────────────────────────────
    public const string ChangePasswordScreen_Title = nameof(ChangePasswordScreen_Title);
    public const string ChangePasswordScreen_Instruction = nameof(ChangePasswordScreen_Instruction);
    public const string ChangePasswordScreen_NewPasswordLabel = nameof(ChangePasswordScreen_NewPasswordLabel);
    public const string ChangePasswordScreen_ConfirmNewLabel = nameof(ChangePasswordScreen_ConfirmNewLabel);
    public const string ChangePasswordScreen_ChangeButton = nameof(ChangePasswordScreen_ChangeButton);
    public const string ChangePasswordScreen_NewPasswordTooShort = nameof(ChangePasswordScreen_NewPasswordTooShort);
    public const string ChangePasswordScreen_NewPasswordsDoNotMatch = nameof(ChangePasswordScreen_NewPasswordsDoNotMatch);
    public const string ChangePasswordScreen_ChangingPassword = nameof(ChangePasswordScreen_ChangingPassword);

    // ── DeleteAccountScreen ───────────────────────────────────────────────────
    public const string DeleteAccountScreen_Title = nameof(DeleteAccountScreen_Title);
    public const string DeleteAccountScreen_Instruction = nameof(DeleteAccountScreen_Instruction);
    public const string DeleteAccountScreen_Warning = nameof(DeleteAccountScreen_Warning);
    public const string DeleteAccountScreen_DeletingAccount = nameof(DeleteAccountScreen_DeletingAccount);

    // ── CharSelectScreen ──────────────────────────────────────────────────────
    public const string CharSelectScreen_Title = nameof(CharSelectScreen_Title);
    public const string CharSelectScreen_Instruction = nameof(CharSelectScreen_Instruction);
    public const string CharSelectScreen_CharFormat = nameof(CharSelectScreen_CharFormat);
    public const string CharSelectScreen_PlayButton = nameof(CharSelectScreen_PlayButton);
    public const string CharSelectScreen_NewCharButton = nameof(CharSelectScreen_NewCharButton);
    public const string CharSelectScreen_DeleteCharButton = nameof(CharSelectScreen_DeleteCharButton);
    public const string CharSelectScreen_LogoutButton = nameof(CharSelectScreen_LogoutButton);
    public const string CharSelectScreen_QuitButton = nameof(CharSelectScreen_QuitButton);
    public const string CharSelectScreen_EnteringWorld = nameof(CharSelectScreen_EnteringWorld);
    public const string CharSelectScreen_Returning = nameof(CharSelectScreen_Returning);
    public const string CharSelectScreen_LoadingClasses = nameof(CharSelectScreen_LoadingClasses);

    // ── DeleteConfirmScreen ───────────────────────────────────────────────────
    public const string DeleteConfirmScreen_PromptFormat = nameof(DeleteConfirmScreen_PromptFormat);
    public const string DeleteConfirmScreen_Warning = nameof(DeleteConfirmScreen_Warning);
    public const string DeleteConfirmScreen_DeletingCharacter = nameof(DeleteConfirmScreen_DeletingCharacter);

    // ── NewCharScreen ─────────────────────────────────────────────────────────
    public const string NewCharScreen_Title = nameof(NewCharScreen_Title);
    public const string NewCharScreen_SexLabel = nameof(NewCharScreen_SexLabel);
    public const string NewCharScreen_ClassLabel = nameof(NewCharScreen_ClassLabel);
    public const string NewCharScreen_MaleButton = nameof(NewCharScreen_MaleButton);
    public const string NewCharScreen_FemaleButton = nameof(NewCharScreen_FemaleButton);
    public const string NewCharScreen_NameTooShort = nameof(NewCharScreen_NameTooShort);
    public const string NewCharScreen_SelectClass = nameof(NewCharScreen_SelectClass);
    public const string NewCharScreen_CritLabel = nameof(NewCharScreen_CritLabel);
    public const string NewCharScreen_SpellCritLabel = nameof(NewCharScreen_SpellCritLabel);
    public const string NewCharScreen_CreatingCharacter = nameof(NewCharScreen_CreatingCharacter);
    public const string NewCharScreen_WornLabel = nameof(NewCharScreen_WornLabel);
    public const string NewCharScreen_CarriedLabel = nameof(NewCharScreen_CarriedLabel);
    public const string NewCharScreen_SpellsLabel = nameof(NewCharScreen_SpellsLabel);
    public const string NewCharScreen_LoadoutNone = nameof(NewCharScreen_LoadoutNone);

    // ── CreditsScreen ─────────────────────────────────────────────────────────
    public const string CreditsScreen_Title = nameof(CreditsScreen_Title);
    public const string CreditsScreen_CloseButton = nameof(CreditsScreen_CloseButton);

    // ── LoadingScreen ─────────────────────────────────────────────────────────
    public const string LoadingScreen_DefaultMessage = nameof(LoadingScreen_DefaultMessage);
    // The server sends a position and a count; the sentence around them is written here, so waiting reads
    // in the language the menus are already in.
    public const string LoadingScreen_QueuePosition = nameof(LoadingScreen_QueuePosition);   // "{Position}" "{Total}"
    public const string LoadingScreen_QueueHint = nameof(LoadingScreen_QueueHint);

    // ── GameplayScreen ────────────────────────────────────────────────────────
    public const string GameplayScreen_DebugOverlayOn = nameof(GameplayScreen_DebugOverlayOn);
    public const string GameplayScreen_DebugOverlayOff = nameof(GameplayScreen_DebugOverlayOff);
    public const string GameplayScreen_NoPotionFormat = nameof(GameplayScreen_NoPotionFormat);
    // Refusal when the player tries to interact with an NPC standing on the other plane of a two-layer map.
    public const string GameplayScreen_NpcOtherLayer = nameof(GameplayScreen_NpcOtherLayer);

    // ── Self-describing key present in every *.json language file ─────────────
    // Each translation file sets this to its own language's native name, e.g. "Español".
    // GetAvailableLanguages() reads only this key to build the language picker list.
    public const string LanguageName = nameof(LanguageName);
}
