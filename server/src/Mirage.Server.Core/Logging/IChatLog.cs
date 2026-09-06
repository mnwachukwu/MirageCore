namespace Mirage.Server.Core.Logging;

/// <summary>Sink for the in-game chat log. Abstracted so Mirage.Server.Core carries no logging-framework
/// dependency; the host supplies the concrete implementation.</summary>
public interface IChatLog
{
    /// <summary>Record one chat line. <paramref name="chatType"/> is the channel name, kept as a
    /// separate field so the sink can filter or route by channel rather than parsing the text.</summary>
    void Write(string message, string chatType);
}
