using Microsoft.Extensions.Logging;

namespace Mirage.Server.Core.Localization;

/// <summary>
/// Logs a <see cref="ServerStrings"/> key through <see cref="ILogger"/> with the template's
/// <c>{Name}</c> placeholders left intact. Serilog's console sink colorizes the substituted
/// values and structured sinks capture them as named properties — both of which are lost when
/// the template is pre-baked via <see cref="ServerStrings.Format"/> before logging.
/// </summary>
public static class LocalizedLog
{
    public static void Info(ILogger logger, string key,
        params (string Key, object? Value)[] args)
    {
        var (template, values) = ServerStrings.GetTemplate(key, args);
        logger.LogInformation(template, values);
    }

    public static void Warn(ILogger logger, string key,
        params (string Key, object? Value)[] args)
    {
        var (template, values) = ServerStrings.GetTemplate(key, args);
        logger.LogWarning(template, values);
    }

    public static void Error(ILogger logger, string key,
        params (string Key, object? Value)[] args)
    {
        var (template, values) = ServerStrings.GetTemplate(key, args);
        logger.LogError(template, values);
    }
}
