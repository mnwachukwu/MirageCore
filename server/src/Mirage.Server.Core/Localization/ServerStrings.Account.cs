using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Account and character auth, editor auth, and the join/leave announcements.</summary>
public static partial class ServerStrings
{
    // ── Auth / Account ────────────────────────────────────────────────────────
    public const string Auth_ShortNameAndPass = nameof(Auth_ShortNameAndPass);
    public const string Auth_ShortDeleteNameAndPass = nameof(Auth_ShortDeleteNameAndPass);
    public const string Auth_ShortNameOrPass = nameof(Auth_ShortNameOrPass);
    public const string Auth_NameTooLong = nameof(Auth_NameTooLong);
    public const string Auth_InvalidName = nameof(Auth_InvalidName);
    public const string Auth_AccountTaken = nameof(Auth_AccountTaken);
    public const string Auth_AccountCreated = nameof(Auth_AccountCreated);
    public const string Auth_AccountCreateFailed = nameof(Auth_AccountCreateFailed);
    public const string Auth_AccountNotFound = nameof(Auth_AccountNotFound);
    public const string Auth_IncorrectPassword = nameof(Auth_IncorrectPassword);
    public const string Auth_AccountLoggedInDelete = nameof(Auth_AccountLoggedInDelete);
    public const string Auth_AccountLoggedIn = nameof(Auth_AccountLoggedIn);
    public const string Auth_AccountDeleted = nameof(Auth_AccountDeleted);
    public const string Auth_PasswordChanged = nameof(Auth_PasswordChanged);
    public const string Auth_ClientOutdated = nameof(Auth_ClientOutdated);
    public const string Auth_LoadFailed = nameof(Auth_LoadFailed);
    public const string Auth_MultiAccount = nameof(Auth_MultiAccount);
    public const string Auth_Banned = nameof(Auth_Banned);
    public const string Auth_BannedCannotDelete = nameof(Auth_BannedCannotDelete);
    public const string Auth_KickedTryAgain = nameof(Auth_KickedTryAgain);
    public const string Auth_CharNameTooShort = nameof(Auth_CharNameTooShort);
    public const string Auth_CharNameTooLong = nameof(Auth_CharNameTooLong);
    public const string Auth_CharSlotsFull = nameof(Auth_CharSlotsFull);
    public const string Auth_CharAlreadyExists = nameof(Auth_CharAlreadyExists);
    public const string Auth_CharNameInUse = nameof(Auth_CharNameInUse);
    public const string Auth_CharNotFound = nameof(Auth_CharNotFound);
    public const string Auth_CombatGhostWarning = nameof(Auth_CombatGhostWarning);

    // ── Editor Auth ───────────────────────────────────────────────────────────
    public const string EditorAuth_InvalidCredentials = nameof(EditorAuth_InvalidCredentials);
    public const string EditorAuth_InsufficientAccess = nameof(EditorAuth_InsufficientAccess);
    public const string EditorAuth_Authenticated = nameof(EditorAuth_Authenticated);
    public const string EditorLock_HeldByAnother = nameof(EditorLock_HeldByAnother);
    public const string EditorLock_HeldByYourOtherSession = nameof(EditorLock_HeldByYourOtherSession);
    public const string Console_EditorsTotal = nameof(Console_EditorsTotal);
    public const string Console_KickEditorUsage = nameof(Console_KickEditorUsage);
    public const string Console_EditorNotFound = nameof(Console_EditorNotFound);
    public const string Console_EditorKicked = nameof(Console_EditorKicked);

    // ── Editor account browser — what a refused character edit says back ───────
    public const string EditorAccounts_Renamed = nameof(EditorAccounts_Renamed);
    public const string EditorAccounts_RenameBadChars = nameof(EditorAccounts_RenameBadChars);
    public const string EditorAccounts_RenameTooShort = nameof(EditorAccounts_RenameTooShort);
    public const string EditorAccounts_RenameTooLong = nameof(EditorAccounts_RenameTooLong);
    public const string EditorAccounts_RenameTaken = nameof(EditorAccounts_RenameTaken);
    public const string EditorAccounts_RenameOnline = nameof(EditorAccounts_RenameOnline);
    public const string EditorAccounts_RenameNoCharacter = nameof(EditorAccounts_RenameNoCharacter);
    public const string EditorAccounts_RenameUnchanged = nameof(EditorAccounts_RenameUnchanged);
    public const string EditorAccounts_BagEdited = nameof(EditorAccounts_BagEdited);
    public const string EditorAccounts_BagSlotEmpty = nameof(EditorAccounts_BagSlotEmpty);
    public const string EditorAccounts_SpellKnown = nameof(EditorAccounts_SpellKnown);
    public const string EditorAccounts_SpellWrongClass = nameof(EditorAccounts_SpellWrongClass);
    public const string EditorAccounts_SpellLevelReq = nameof(EditorAccounts_SpellLevelReq);
    public const string EditorAccounts_SpellIntReq = nameof(EditorAccounts_SpellIntReq);
    public const string EditorAccounts_QuestSet = nameof(EditorAccounts_QuestSet);
    public const string EditorAccounts_QuestLevelReq = nameof(EditorAccounts_QuestLevelReq);
    public const string EditorAccounts_QuestStatReq = nameof(EditorAccounts_QuestStatReq);
    public const string EditorAccounts_QuestWrongClass = nameof(EditorAccounts_QuestWrongClass);
    public const string EditorAccounts_QuestPrereq = nameof(EditorAccounts_QuestPrereq);
    public const string EditorAccounts_QuestNotInLog = nameof(EditorAccounts_QuestNotInLog);
    public const string EditorAccounts_BookFull = nameof(EditorAccounts_BookFull);
    public const string EditorAccounts_BookSlotEmpty = nameof(EditorAccounts_BookSlotEmpty);
    public const string EditorAccounts_BankFull = nameof(EditorAccounts_BankFull);
    public const string EditorAccounts_BankSlotEmpty = nameof(EditorAccounts_BankSlotEmpty);

    // ── JoinLeaveSystem ───────────────────────────────────────────────────────
    public const string JoinLeave_JoinBroadcast = nameof(JoinLeave_JoinBroadcast);
    public const string JoinLeave_LeaveBroadcast = nameof(JoinLeave_LeaveBroadcast);
    public const string JoinLeave_Welcome = nameof(JoinLeave_Welcome);
    public const string JoinLeave_HelpHint = nameof(JoinLeave_HelpHint);
    public const string JoinLeave_Motd = nameof(JoinLeave_Motd);
    public const string JoinLeave_NoOtherPlayers = nameof(JoinLeave_NoOtherPlayers);
    public const string JoinLeave_OtherPlayers = nameof(JoinLeave_OtherPlayers);
}
