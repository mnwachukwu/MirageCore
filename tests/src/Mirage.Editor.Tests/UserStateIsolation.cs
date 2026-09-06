using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>
/// 🔴 The suite must never touch the developer's own editor settings.
///
/// <para>The editor's <c>appsettings.json</c> is a live user document, not a fixture. A test that creates
/// or opens a world drives the same code the app does, which writes the folder it used into
/// <c>RecentWorlds</c> and <c>LastWorldBrowsePath</c> and saves. Run against the real file, the suite fills
/// the recent-worlds menu with dead temp directories and points the next Open World at whichever temp
/// folder the last test happened to make — which is exactly what it did, until Matt noticed Open World
/// dropping him in <c>%LOCALAPPDATA%\Temp</c>.</para>
///
/// <para>Redirecting the per-user roots is what fixes it for the whole assembly, rather than each fixture
/// remembering to undo its own damage — the old attempt reset <c>LastWorldBrowsePath</c> in memory during
/// teardown, long after <c>Save()</c> had already written it to disk.</para>
///
/// <para>This runs before every fixture in the namespace. Settings are cached on first access, so the
/// redirect has to be in place before any test reads one.</para>
/// </summary>
[SetUpFixture]
public class UserStateIsolation
{
    private string _root = "";

    [OneTimeSetUp]
    public void RedirectUserState()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirage-editor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        UserPaths.RootOverride = _root;
    }

    [OneTimeTearDown]
    public void RestoreUserState()
    {
        UserPaths.RootOverride = null;
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
