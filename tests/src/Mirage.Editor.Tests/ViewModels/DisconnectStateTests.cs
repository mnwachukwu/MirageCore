using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// What the window shows after a live session ends.
///
/// <para>The content area holds the open editor and the "no world open" message in one grid, and exactly
/// one of them is ever visible. A section left selected on the way out of a connection would put the
/// message on top of a fully populated editor — a right-hand panel, a tile palette and a toolbar, all for
/// a world that is not there.</para>
///
/// <para>Disconnecting closes the world outright — including a folder that was open BEFORE connecting.
/// Falling back to that folder put a different world on screen under the same window with nothing
/// announcing the swap, which is how somebody edits the wrong one.</para>
/// </summary>
[TestFixture]
public class DisconnectStateTests
{
    private string _openedWorld = "";

    [TearDown]
    public void CloseAnyWorld()
    {
        EditorPaths.OpenWorld("");
        if (_openedWorld.Length > 0 && Directory.Exists(_openedWorld)) Directory.Delete(_openedWorld, true);
        _openedWorld = "";
    }

    private static MainWindowViewModel Online()
    {
        var vm = new MainWindowViewModel(new EditorDataService(), new EditorConnection(), new EditorBitmapCache())
        {
            IsOnline = true,
        };
        vm.SelectedSection = vm.Sections.First();
        return vm;
    }

    /// <summary>The reported case: connect from the empty state, then disconnect. There is no world to
    /// fall back to, so the window has to go back to being empty.</summary>
    [Test]
    public async Task DisconnectingWithNoWorldOpen_LeavesTheWindowEmpty()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsOnline, Is.False);
            Assert.That(vm.ShowEmptyWorld, Is.True, "the empty-state message is shown");
            Assert.That(vm.CurrentEditor, Is.Null, "and nothing is shown underneath it");
            Assert.That(vm.SelectedSection, Is.Null, "so no rail row reads as selected either");
        });
    }

    /// <summary>The invariant the content area is built on, stated on its own: the message and an editor
    /// are never both up.</summary>
    [Test]
    public async Task TheMessageAndAnEditor_AreNeverBothShown()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();

        Assert.That(vm.ShowEmptyWorld && vm.CurrentEditor is not null, Is.False);
    }

    /// <summary>The other case: a folder was open before connecting. It does NOT come back — the window
    /// ends on the empty state either way, so what is on screen after a disconnect never depends on what
    /// was on screen before the connection.</summary>
    [Test]
    public async Task DisconnectingWithAWorldOpenedBeforehand_ClosesThatToo()
    {
        _openedWorld = Path.Combine(Path.GetTempPath(), "mirage-disconnect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_openedWorld);
        EditorPaths.OpenWorld(_openedWorld);
        var vm = Online();

        Assume.That(EditorPaths.HasWorld, Is.True, "the folder is open going in");

        await vm.ForceDisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(EditorPaths.HasWorld, Is.False, "the world is closed, not fallen back to");
            Assert.That(vm.ShowEmptyWorld, Is.True);
            Assert.That(vm.CurrentEditor, Is.Null);
            Assert.That(vm.SelectedSection, Is.Null);
        });
    }

    [Test]
    public async Task DisconnectingTwice_IsHarmless()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();
        await vm.ForceDisconnectAsync();

        Assert.That(vm.ShowEmptyWorld, Is.True);
    }
}
