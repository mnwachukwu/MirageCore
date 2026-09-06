using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Tests.World;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// Account-name identity in persistence draws a deliberate line:
/// <list type="bullet">
/// <item>CREATION uniqueness (<see cref="JsonPersistenceService.AccountNameTakenAsync"/>) is CANONICAL —
/// case- AND underscore-insensitive — so registering "The_Man" reserves the identity "theman" and blocks
/// "TheMan", "the__man", etc.</item>
/// <item>LOGIN existence (<see cref="JsonPersistenceService.AccountExistsAsync"/>) stays EXACT —
/// case-insensitive but underscore-SENSITIVE — so you sign in with "the_man" but never "theman" or
/// "the__man".</item>
/// </list>
/// A regression guard: it's easy to accidentally route login through the canonical check and let "TheMan"
/// open "The_Man"'s account.
/// </summary>
[TestFixture]
public class AccountNameUniquenessTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-nametest-" + Guid.NewGuid().ToString("N"));
        _svc = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    // Registering "The_Man" reserves the canonical identity "theman", so every underscore/case permutation
    // is reported as taken and can't be created.
    [TestCase("TheMan")]
    [TestCase("theman")]
    [TestCase("THE_MAN")]
    [TestCase("the__man")]
    [TestCase("t_h_e_m_a_n")]
    public async Task Creation_IsCanonical_BlocksPermutations(string attempt)
    {
        await _svc.CreateAccountAsync("The_Man", "secret");
        Assert.That(await _svc.AccountNameTakenAsync(attempt), Is.True);
    }

    // A genuinely different name is free to register.
    [Test]
    public async Task Creation_DifferentIdentity_NotTaken()
    {
        await _svc.CreateAccountAsync("The_Man", "secret");
        Assert.That(await _svc.AccountNameTakenAsync("TheWoman"), Is.False);
    }

    // Login existence is case-insensitive yet underscore-SENSITIVE: the exact underscore layout must match.
    [TestCase("The_Man", ExpectedResult = true)]
    [TestCase("the_man", ExpectedResult = true)]    // case-insensitive
    [TestCase("THE_MAN", ExpectedResult = true)]
    [TestCase("TheMan", ExpectedResult = false)]    // dropped the underscore
    [TestCase("the__man", ExpectedResult = false)]  // different underscore layout
    [TestCase("theman", ExpectedResult = false)]
    public async Task<bool> Login_IsExact_UnderscoreSensitive(string loginName)
    {
        await _svc.CreateAccountAsync("The_Man", "secret");
        return await _svc.AccountExistsAsync(loginName);
    }
}
