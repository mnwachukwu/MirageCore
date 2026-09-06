using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Mirage.Server.Shell.Localization;

/// <summary>
/// Turns "show this secret" into the character a TextBox masks with: nothing when shown, a bullet when
/// not.
///
/// <para>🔴 Masking is driven through <c>PasswordChar</c> rather than <c>RevealPassword</c> because the
/// TextBox clears <c>RevealPassword</c> itself when it loses focus. The binding is one-way, so the
/// view-model stays true, nothing changes, and no further notification ever arrives — the box re-masks
/// on the first click elsewhere and the Show tick never brings it back. <c>PasswordChar</c> is not
/// touched by focus, so the tick keeps meaning what it says.</para>
/// </summary>
public sealed class MaskConverter : IValueConverter
{
    /// <summary>What a masked box shows per character. Matches the bullet used before this was bound.</summary>
    private const char Bullet = '\u2022';

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? '\0' : Bullet;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
