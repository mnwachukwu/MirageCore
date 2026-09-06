using Mirage.Server.Core.Logging;

namespace Mirage.Server.Host.Logging;

/// <summary>Serilog-backed <see cref="IChatLog"/>. Attaches the channel as a structured
/// <c>ChatType</c> property rather than folding it into the message, so log sinks can filter on it.</summary>
internal sealed class SerilogChatLog : IChatLog
{
    private readonly Serilog.ILogger _logger;
    internal SerilogChatLog(Serilog.ILogger logger) => _logger = logger;
    public void Write(string message, string chatType) =>
        _logger.ForContext("ChatType", chatType).Information("{Message}", message);
}
