using Mirage.Shared;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// One row of the account browser, as persistence sees it — no notion of who is online, which is
/// game-thread state the caller adds.
/// </summary>
/// <param name="Login">The account name, which is also its file name and its identity everywhere else.</param>
/// <param name="Access">The account's admin level.</param>
/// <param name="CharNames">Named characters on the account, in slot order. Empty slots are left out.</param>
public readonly record struct AccountSummary(string Login, AdminLevel Access, IReadOnlyList<string> CharNames);
