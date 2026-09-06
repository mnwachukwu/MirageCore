using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// What the window calls the world it has open.
///
/// <para>Connecting is how an online world is opened, so a live session has a world exactly as an opened
/// folder does — and the name shown is the SERVER's, since the folder that may also be open is not what is
/// being edited. Telling a live world from a test copy of it is the whole reason a world carries a name,
/// and the title bar is where that has to be legible.</para>
/// </summary>
[TestFixture]
public class WorldLabelTests
{
    private static (MainWindowViewModel Vm, EditorDataService Data) Build()
    {
        var data = new EditorDataService();
        return (new MainWindowViewModel(data, new EditorConnection(), new EditorBitmapCache()), data);
    }

    private static MainWindowViewModel Connected(string worldName)
    {
        var (vm, data) = Build();
        // The packet lands before the flag flips, which is the order both connect paths use.
        data.LoadOnline(new EditorDataPacket { WorldName = worldName });
        vm.IsOnline = true;
        return vm;
    }

    [Test]
    public void ConnectedToANamedWorld_ShowsThatName()
    {
        Assert.That(Connected("Demo Landia").WorldLabel, Is.EqualTo("Demo Landia"));
    }

    [Test]
    public void ConnectedToAnUnnamedWorld_SaysUntitled()
    {
        Assert.That(Connected("").WorldLabel, Does.Contain("Untitled"));
    }

    /// <summary>A live session is a world open, so the window shows the editor rather than the prompt to
    /// open a folder.</summary>
    [Test]
    public void Connecting_CountsAsAWorldBeingOpen()
    {
        var vm = Connected("Demo Landia");

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.True);
            Assert.That(vm.ShowEmptyWorld, Is.False);
        });
    }

    [Test]
    public void WithNothingOpen_ThereIsNoLabelAtAll()
    {
        var (vm, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(vm.ShowEmptyWorld, Is.True);
            Assert.That(vm.WorldLabel, Is.Empty, "no world means no name, not a placeholder name");
        });
    }

    /// <summary>Disconnecting is how an online world is closed, so its name goes with it.</summary>
    [Test]
    public async Task Disconnecting_TakesTheNameWithIt()
    {
        var vm = Connected("Demo Landia");

        await vm.ForceDisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasWorld, Is.False);
            Assert.That(vm.WorldLabel, Is.Empty);
        });
    }

    /// <summary>The title bar is set in code rather than bound, so it repaints on the label's change
    /// notification and on nothing else.</summary>
    [Test]
    public void GoingOnlineAndOff_RaisesTheLabel()
    {
        var (vm, data) = Build();
        int raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.WorldLabel)) raised++; };

        data.LoadOnline(new EditorDataPacket { WorldName = "Demo Landia" });
        vm.IsOnline = true;
        vm.IsOnline = false;

        Assert.That(raised, Is.GreaterThanOrEqualTo(2));
    }
}
