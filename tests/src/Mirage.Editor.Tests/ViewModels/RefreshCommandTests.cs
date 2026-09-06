using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Refresh reads the offline folder, and only the offline folder.
///
/// <para>A live session is told about every record the moment it changes — the server pushes each save to
/// every other editor — so a manual reread has nothing to find. Left enabled it answers "nothing moved"
/// every time, which reads as a broken button rather than as a redundant one.</para>
/// </summary>
[TestFixture]
public class RefreshCommandTests
{
    private static MainWindowViewModel Vm() =>
        new(new EditorDataService(), new EditorConnection(), new EditorBitmapCache());

    [Test]
    public void Offline_TheCommandIsAvailable()
    {
        var vm = Vm();
        vm.IsOnline = false;

        Assert.That(vm.RefreshFromDiskCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void Online_TheCommandIsNot()
    {
        var vm = Vm();
        vm.IsOnline = true;

        Assert.That(vm.RefreshFromDiskCommand.CanExecute(null), Is.False);
    }

    /// <summary>Going online has to re-raise it, or the menu item stays clickable until something else
    /// happens to poke the command.</summary>
    [Test]
    public void GoingOnlineAndBack_MovesTheCommandWithIt()
    {
        var vm = Vm();
        bool raised = false;
        vm.RefreshFromDiskCommand.CanExecuteChanged += (_, _) => raised = true;

        vm.IsOnline = true;

        Assert.That(raised, Is.True, "the menu item is told to re-read CanExecute");
        Assert.That(vm.RefreshFromDiskCommand.CanExecute(null), Is.False);

        vm.IsOnline = false;
        Assert.That(vm.RefreshFromDiskCommand.CanExecute(null), Is.True);
    }
}
