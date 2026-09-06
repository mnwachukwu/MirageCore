using Mirage.Editor.Localization;
using Mirage.Shared;

namespace Mirage.Editor.Models;

/// <summary>A selectable Moral for the inherit/override ComboBoxes. <see cref="Value"/> is null for
/// the "(Inherit)" sentinel (the field declines to provide a value) and a concrete <see cref="MapMoral"/>
/// otherwise. Record value-equality lets a ComboBox match the bound selection back to a list element.
/// Enum names render raw (matching the pre-tri-state Moral combo); only the inherit label is localized.</summary>
public sealed record MoralChoice(MapMoral? Value, string Label)
{
    public override string ToString() => Label;
}

public static class MoralChoices
{
    /// <summary>The inherit sentinel followed by every <see cref="MapMoral"/>. Rebuilt on demand so a
    /// language switch re-localizes the "(Inherit)" label.</summary>
    public static IReadOnlyList<MoralChoice> Build()
    {
        var list = new List<MoralChoice> { new(null, EditorStrings.Get(EditorStrings.Common_Inherit)) };
        foreach (var m in Enum.GetValues<MapMoral>())
            list.Add(new MoralChoice(m, m.ToString()));
        return list;
    }
}
