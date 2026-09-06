using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Mirage.Editor.Localization;

/// <summary>
/// A nav section's glyph, keyed on <c>SectionViewModel.Name</c> — the stable id, never the localized
/// label, so an icon cannot change when the language does.
///
/// <para>Vector geometry rather than an icon font: a font would render differently (or not at all) off
/// Windows, and these have to look the same everywhere. Each is drawn on a 16x16 box.</para>
/// </summary>
public sealed class SectionIconConverter : IValueConverter
{
    private const string Grid =
        "M2,2h3v3h-3z M6.5,2h3v3h-3z M11,2h3v3h-3z " +
        "M2,6.5h3v3h-3z M6.5,6.5h3v3h-3z M11,6.5h3v3h-3z " +
        "M2,11h3v3h-3z M6.5,11h3v3h-3z M11,11h3v3h-3z";

    private const string Quads = "M2,2h5.5v5.5h-5.5z M8.5,2h5.5v5.5h-5.5z M2,8.5h5.5v5.5h-5.5z M8.5,8.5h5.5v5.5h-5.5z";

    private const string Bag = "M5,5V4.2A3,3 0 0 1 11,4.2V5h2.2l0.9,9H1.9L2.8,5z M6.6,5h2.8V4.2a1.4,1.4 0 0 0-2.8,0z";

    private const string Person =
        "M8,1.8a2.6,2.6 0 1 1 0,5.2A2.6,2.6 0 0 1 8,1.8z M8,8.3c3.1,0 5.3,1.7 5.3,3.7V14.2H2.7v-2.2c0-2 2.2-3.7 5.3-3.7z";

    private const string Shop =
        "M2,2.4h12l1,3.1a2,2 0 0 1-4,0a2,2 0 0 1-4,0a2,2 0 0 1-4,0z M3,8.2h10V14H9.4v-3.6h-2.8V14H3z";

    private const string Spark = "M8,1.4l1.7,4.9l4.9,1.7l-4.9,1.7L8,14.6l-1.7-4.9L1.4,8l4.9-1.7z";

    private const string Shield = "M8,1.4l5.6,2.2v4.2c0,3.3-2.4,6.2-5.6,6.8c-3.2-0.6-5.6-3.5-5.6-6.8V3.6z";

    private const string List = "M3,2.6h10v1.5H3z M3,5.8h10v1.5H3z M3,9h10v1.5H3z M3,12.2h6v1.5H3z";

    private const string Bubble = "M2,2.6h12v7.8H8.6l-3.4,3.2v-3.2H2z";

    // A key, for accounts. Deliberately not another person glyph: NPCs already own that shape, and at
    // rail size two figures would be one silhouette. What this section is really about is access.
    private const string Key =
        "M9.8,1.6a4.6,4.6 0 1 1-3.1,8L5.4,10.9H3.6v1.8H1.8v1.8H0v-2.6l6-6a4.6,4.6 0 0 1 3.8-4.3z " +
        "M10.6,4a1.3,1.3 0 1 0 0,2.6a1.3,1.3 0 0 0 0-2.6z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string path = (value as string) switch
        {
            "Maps" => Grid,
            "MapGroups" => Quads,
            "Items" => Bag,
            "NPCs" => Person,
            "Shops" => Shop,
            "Spells" => Spark,
            "Classes" => Shield,
            "Quests" => List,
            "Conversations" => Bubble,
            "Accounts" => Key,
            _ => Quads,
        };
        return Geometry.Parse(path);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
