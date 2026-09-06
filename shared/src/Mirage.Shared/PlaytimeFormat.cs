namespace Mirage.Shared;

/// <summary>Formats a playtime duration (seconds) as a compact "hours + minutes" string for the
/// <c>/played</c> and <c>/info</c> readouts. Pure, so it's directly unit-testable and shared by
/// every caller. The "h"/"m" abbreviations are numeric-formatter chrome (like a percent sign), not
/// user-facing prose to localize.</summary>
public static class PlaytimeFormat
{
    private const int SecondsPerHour = 3600;
    private const int SecondsPerMinute = 60;

    /// <summary>"{H}h {M}m" (e.g. "12h 34m"), dropping the hours segment below one hour ("34m") and reading
    /// "0m" for a zero/negative duration. Hours are uncapped (a long-lived character can exceed a day).</summary>
    public static string HoursMinutes(long seconds)
    {
        if (seconds < 0) seconds = 0;
        long hours = seconds / SecondsPerHour;
        long minutes = seconds % SecondsPerHour / SecondsPerMinute;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }
}
