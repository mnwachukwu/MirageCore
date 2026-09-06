using Mirage.Server.Host.Management;
using NUnit.Framework;

namespace Mirage.Server.Tests.Management;

/// <summary>
/// The tee is what a remote operator actually sees, and it is the only piece of the management path with
/// no local equivalent — stdout was free. These pin the two things it has to get right: nothing is lost
/// from the real console, and what subscribers get is whole lines.
/// </summary>
[TestFixture]
public sealed class ConsoleTeeTests
{
    private static (ConsoleTee Tee, StringWriter Inner, List<string> Lines) Build()
    {
        var inner = new StringWriter();
        var tee = new ConsoleTee(inner);
        var lines = new List<string>();
        tee.LineWritten += lines.Add;
        return (tee, inner, lines);
    }

    [Test]
    public void PassesEverythingThroughToTheRealConsole()
    {
        var (tee, inner, _) = Build();

        tee.WriteLine("first");
        tee.Write("second");
        tee.WriteLine();

        Assert.That(inner.ToString(), Is.EqualTo($"first{Environment.NewLine}second{Environment.NewLine}"));
    }

    [Test]
    public void RaisesOneEventPerCompletedLine()
    {
        var (tee, _, lines) = Build();

        tee.WriteLine("one");
        tee.WriteLine("two");

        Assert.That(lines, Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void StripsTheNewlineFromWhatSubscribersSee()
    {
        var (tee, _, lines) = Build();

        tee.WriteLine("no trailing break");

        Assert.That(lines[0], Is.EqualTo("no trailing break"));
    }

    [Test]
    public void HoldsAPartialLineUntilItIsFinished()
    {
        var (tee, _, lines) = Build();

        tee.Write("half ");
        Assert.That(lines, Is.Empty, "a line with no break yet is not a line");

        tee.WriteLine("a line");
        Assert.That(lines, Is.EqualTo(new[] { "half a line" }));
    }

    [Test]
    public void SplitsAMultiLineWriteIntoItsLines()
    {
        // How the grouped /help listing arrives: one Write carrying embedded newlines.
        var (tee, _, lines) = Build();

        tee.Write("Players: /who\nWorld: /tod\nServer: /shutdown\n");

        Assert.That(lines, Is.EqualTo(new[] { "Players: /who", "World: /tod", "Server: /shutdown" }));
    }

    [Test]
    public void ReadsTheSameWhicheverNewlineWasUsed()
    {
        var (tee, _, lines) = Build();

        tee.Write("windows\r\nunix\n");

        Assert.That(lines, Is.EqualTo(new[] { "windows", "unix" }));
    }

    [Test]
    public void EmitsEmptyLinesRatherThanSwallowingThem()
    {
        // Blank lines are spacing in the console output, and a remote operator should see the same shape.
        var (tee, _, lines) = Build();

        tee.WriteLine("above");
        tee.WriteLine("");
        tee.WriteLine("below");

        Assert.That(lines, Is.EqualTo(new[] { "above", "", "below" }));
    }

    [Test]
    public void CostsNothingWithNobodyListening()
    {
        // The tee is installed whether or not remote management is on, so the no-subscriber path is the
        // common one and must not throw.
        var inner = new StringWriter();
        var tee = new ConsoleTee(inner);

        Assert.DoesNotThrow(() => tee.WriteLine("alone"));
        Assert.That(inner.ToString(), Is.EqualTo($"alone{Environment.NewLine}"));
    }
}
