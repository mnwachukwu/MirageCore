namespace Mirage.Editor.Models;

/// <summary>
/// Wraps a nullable enum value for use as a ComboBox filter option.
/// The default (Value = null) represents "show all".
/// ToString() returns the display label suitable for direct ComboBox rendering.
/// </summary>
public sealed class EnumFilterOption<T> where T : struct, Enum
{
    public T? Value { get; }
    public EnumFilterOption() { }
    public EnumFilterOption(T value) { Value = value; }
    public override string ToString() => Value is { } v ? v.ToString() : "(All)";
}
