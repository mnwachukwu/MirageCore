using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Mirage.Editor.Localization;

/// <summary>
/// A nav section's glyph, keyed on the name the SECTION carries rather than on its id.
///
/// <para>🔴 <b>A game picks it.</b> Keyed on the id, every section a game added fell through to one
/// default — so two families in one game were indistinguishable from each other and from one of Core's
/// own sections. The name comes from <c>RecordFamily.Icon</c> now, out of the same vocabulary the game
/// client reads; <see cref="GameIconPaths"/> holds what this editor draws for each one.</para>
/// </summary>
public sealed class SectionIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Geometry.Parse(GameIconPaths.For(value as string));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
