using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Mirage.Server.Shell.Localization;

/// <summary>
/// Renders the two unix timestamps on a moderation row. They stay raw seconds on the wire — the report
/// is data, and a server that formatted dates for a shell would be formatting them in its OWN language,
/// not the operator's.
/// </summary>
public sealed class UnixDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A missing or non-positive stamp reads as unknown rather than as 1970, which is what a
        // hand-edited banlist would otherwise put in front of an operator.
        if (value is not long seconds || seconds <= 0) return ShellStrings.Get(ShellStrings.Mod_Unknown);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("g", culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Whole minutes left on a kick or mute, rounded UP so a penalty with seconds to run never reads as
/// zero and looks already over.
///
/// <para>Fixed at the moment the report was gathered — a row does not tick down. Refreshing
/// re-reads it, and a penalty that ran out drops off the list entirely rather than showing zero.</para>
/// </summary>
public sealed class MinutesLeftConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long expiresUtc) return "";
        long left = expiresUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (left <= 0) return ShellStrings.Get(ShellStrings.Mod_Unknown);
        return ShellStrings.Format(ShellStrings.Mod_MinutesLeft, ("Minutes", (left + 59) / 60));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
