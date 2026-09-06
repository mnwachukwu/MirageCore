using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// That the suite is actually reading and writing somewhere disposable.
///
/// <para>🔴 Without this the isolation is invisible: every test still passes when the redirect is missing,
/// because the settings it corrupts belong to the developer rather than to the run. The damage only shows
/// up later, in the app, as a recent-worlds menu full of deleted temp folders.</para>
/// </summary>
[TestFixture]
public class UserStateIsolationTests
{
    [Test]
    public void TheSuite_ResolvesUserStateSomewhereDisposable()
    {
        string config = new UserPaths("Mirage Source Remastered Editor").Config();

        Assert.Multiple(() =>
        {
            Assert.That(UserPaths.RootOverride, Is.Not.Null.And.Not.Empty,
                "UserStateIsolation should have redirected the per-user roots for the whole assembly.");
            Assert.That(config, Does.StartWith(UserPaths.RootOverride!));
        });
    }

    /// <summary>The real per-user config is where the developer's own editor settings live, and nothing in
    /// a test run has any business resolving to it.</summary>
    [Test]
    public void TheSuite_NeverResolvesTheRealUserConfig()
    {
        string real = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string config = new UserPaths("Mirage Source Remastered Editor").Config();

        Assert.That(config, Does.Not.StartWith(real).IgnoreCase,
            "A test resolved the developer's real editor settings; a world opened or created under it "
            + "would be written into their RecentWorlds and LastWorldBrowsePath.");
    }
}
