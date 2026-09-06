using Mirage.Server.Shell.Localization;
using Mirage.Server.Shell.ViewModels;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// A form does not post a line the server can only refuse.
///
/// <para>🔴 Every argument was optional, so pressing Run on an empty name sent <c>/kick</c> with nothing
/// after it. The server answered with a usage line into the console — which is the one place an operator
/// pressing a button is not looking — so the button read as broken. Run is disabled instead: a control
/// that cannot work should look like it cannot work.</para>
/// </summary>
[TestFixture]
public class CommandFormGuardTests
{
    [OneTimeSetUp]
    public void LoadStrings() =>
        ShellStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang", "shell"), "en");

    static List<ShellCommand> Commands(Action<string>? send = null) =>
        MainWindowViewModel.BuildCommandGroups(send ?? (_ => { })).SelectMany(g => g.Commands).ToList();

    static ShellCommand Find(string verb, Action<string>? send = null) =>
        Commands(send).First(c => c.Verb == verb);

    [TestCase("/kick", "name")]
    [TestCase("/mute", "name")]
    [TestCase("/ban", "name")]
    [TestCase("/hwban", "name")]
    [TestCase("/setaccess", "name")]
    [TestCase("/unban", "account")]
    [TestCase("/unkick", "account")]
    [TestCase("/unmute", "account")]
    [TestCase("/hwunban", "account")]
    [TestCase("/kickeditor", "slot or account")]
    public void CommandsNamingSomeone_WillNotRunWithoutIt(string verb, string field)
    {
        var command = Find(verb);
        var box = command.Parameters.Single(p => p.Name == field);

        Assert.That(command.CanRun, Is.False, $"{verb} would post with an empty {field}");

        box.Value = "somebody";
        Assert.That(command.CanRun, Is.True, $"{verb} still refuses to run with a {field} filled in");
    }

    /// <summary>Whitespace is not an answer. The argument is trimmed on the way out, so a space would
    /// arrive as the same empty command.</summary>
    [Test]
    public void WhitespaceDoesNotCountAsAName()
    {
        var kick = Find("/kick");
        kick.Parameters.Single(p => p.Name == "name").Value = "   ";
        Assert.That(kick.CanRun, Is.False);
    }

    [Test]
    public void RunIsRefusedWhileTheNameIsBlank()
    {
        string? sent = null;
        var ban = Find("/ban", line => sent = line);
        ban.RunCommand.Execute(null);
        Assert.That(sent, Is.Null, "a blank form posted a line anyway");
    }

    /// <summary>The MOTD is the deliberate exception: empty means "take it down", so its box must not be
    /// required or there would be no way to clear one.</summary>
    [Test]
    public void Motd_RunsWithNothingInIt()
    {
        var motd = Find("/motd");
        Assert.That(motd.CanRun, Is.True, "an empty MOTD is how a message is cleared, not a mistake");
    }

    [Test]
    public void Motd_SendsTheBareVerbWhenEmptied()
    {
        string? sent = null;
        var motd = Find("/motd", line => sent = line);
        motd.RunCommand.Execute(null);
        Assert.That(sent, Is.EqualTo("/motd"), "clearing must post the verb alone for the server to read as a clear");
    }

    // ── /management: the value belongs to one action ─────────────────────────

    static (ShellCommand cmd, CommandParameter action, CommandParameter value) Management(Action<string>? send = null)
    {
        var cmd = Find("/management", send);
        return (cmd, cmd.Parameters.First(p => p.Name == "action"), cmd.Parameters.First(p => p.Name == "port"));
    }

    [TestCase("")]
    [TestCase("token")]
    [TestCase("off")]
    public void TheValueBoxIsHidden_ForActionsThatTakeNone(string action)
    {
        var (cmd, chosen, value) = Management();
        chosen.Value = action;

        Assert.Multiple(() =>
        {
            Assert.That(value.IsShown, Is.False, $"'{action}' takes no value, so the box should not be there");
            Assert.That(cmd.CanRun, Is.True, $"'{action}' needs nothing typed, so Run must be available");
        });
    }

    [Test]
    public void TheValueBoxAppears_AndIsRequired_ForPort()
    {
        var (cmd, action, value) = Management();
        action.Value = "port";

        Assert.Multiple(() =>
        {
            Assert.That(value.IsShown, Is.True, "'port' takes a value, so the box has to appear");
            Assert.That(cmd.CanRun, Is.False, "'port' with no number would post a line the server refuses");
        });

        value.Value = "4001";
        Assert.That(cmd.CanRun, Is.True);
    }

    /// <summary>A value typed for "port" is not carried into an action that takes none.</summary>
    [Test]
    public void SwitchingAwayFromPort_DropsTheValue()
    {
        string? sent = null;
        var (cmd, action, value) = Management(line => sent = line);

        action.Value = "port";
        value.Value = "4001";
        action.Value = "off";
        cmd.RunCommand.Execute(null);

        Assert.That(sent, Is.EqualTo("/management off"),
            "the port typed a moment ago rode along into an action that does not take one");
    }

    [Test]
    public void ManagementComposesTheLineTheConsoleParses()
    {
        string? sent = null;
        var (cmd, action, value) = Management(line => sent = line);

        action.Value = "port";
        value.Value = "4001";
        cmd.RunCommand.Execute(null);

        Assert.That(sent, Is.EqualTo("/management port 4001"));
    }
}
