using Avalonia;
using Avalonia.Controls;

namespace Mirage.Ui.Controls;

/// <summary>
/// A two-column label/value form row. The label column sizes to its content, so a caption can never be clipped
/// by the column that holds it — the width a form asks for is a FLOOR, not a cap.
///
/// <para><see cref="LabelWidth"/> sets that floor. Rows sharing a floor also share a sizing group, so a caption
/// that outgrows it widens every row in the group together instead of stepping out of line on its own. A row
/// with no <see cref="LabelWidth"/> is simply as wide as it needs to be.</para>
/// </summary>
public class FormRow : Grid
{
    /// <summary>The narrowest the label column may be, in pixels. Rows with equal values line up with each
    /// other. Zero (the default) means the column is sized purely by its content.</summary>
    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<FormRow, double>(nameof(LabelWidth));

    public double LabelWidth
    {
        get => GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    private readonly ColumnDefinition _labelColumn = new(GridLength.Auto);

    public FormRow()
    {
        ColumnDefinitions = [_labelColumn, new ColumnDefinition(GridLength.Star)];
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LabelWidthProperty) return;

        double width = change.GetNewValue<double>();
        _labelColumn.MinWidth = width;
        // Keyed by the floor rather than one group per form: rows that asked for the same width are the ones
        // that used to line up, so grouping by it reproduces the alignment the fixed columns gave.
        _labelColumn.SharedSizeGroup = width > 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MirageFormLabel{width:0}")
            : null;
    }
}
