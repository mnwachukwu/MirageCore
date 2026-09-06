using Mirage.Server.Shell.ViewModels;
using NUnit.Framework;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// Shutting down a server you are only attached to is a one-way door: nothing in this window can start
/// it again. The shell refuses the command when remote, so what matters is that the match cannot be
/// walked around by casing or a trailing argument — the server would still act on those.
/// </summary>
[TestFixture]
public sealed class ShutdownGuardTests
{
    [TestCase("/shutdown")]
    [TestCase("/SHUTDOWN")]
    [TestCase("/ShutDown")]
    [TestCase("  /shutdown  ")]
    [TestCase("/shutdown now")]
    [TestCase("/shutdown  with  arguments")]
    public void RecognizesEveryFormTheServerWouldActOn(string line)
    {
        Assert.That(MainWindowViewModel.IsShutdown(line), Is.True);
    }

    [TestCase("/who")]
    [TestCase("/shut")]
    [TestCase("/shutdownsomething")]
    [TestCase("/motd the server shuts down at midnight")]
    [TestCase("")]
    public void LeavesEverythingElseAlone(string line)
    {
        // /motd matters: an announcement ABOUT a shutdown is not a shutdown, and blocking it would make
        // the guard worse than useless.
        Assert.That(MainWindowViewModel.IsShutdown(line), Is.False);
    }
}
