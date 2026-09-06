using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Mirage.Editor.Localization;

/// <summary>
/// Renders a vocabulary enum through <see cref="EditorVocabulary"/> instead of <c>ToString()</c>.
///
/// <para>A <c>ComboBox</c> bound straight to <c>Enum.GetValues<T>()</c> displays the member
/// identifier, which is how the attribute and layer pickers came to read "NpcAvoid" while the rest
/// of the editor called the same thing "NPC Avoid". Applying this as the <c>ItemTemplate</c> fixes
/// both the drop-down list and the closed selection box, which share that template.</para>
/// </summary>
public sealed class VocabularyConverter : IValueConverter
{
    /// <summary>Shared instance for XAML resource declarations.</summary>
    public static readonly VocabularyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => EditorVocabulary.NameOfValue(value);

    /// <summary>One-way only: the ComboBox binds SelectedItem to the enum value itself, so nothing
    /// ever needs to turn a display name back into a member.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("EditorVocabulary names are display-only.");
}
