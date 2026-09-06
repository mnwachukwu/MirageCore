using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One record that points at the record being edited, rendered as a clickable link.
///
/// <para>References in this data model only ever run one way — a map names its group, an NPC names the item it
/// drops, a shop names its keeper — so "what refers to me" is answered by scanning the other collections rather
/// than read off the record itself. This is the row that scan produces.</para>
///
/// <para>The link carries a plain <see cref="Action"/> rather than a target address: what a click has to do is
/// switch section and select a record, and only <see cref="MainWindowViewModel"/> can do both. Keeping the
/// destination as a closure it supplies means this type never needs to know the section names.</para>
/// </summary>
public sealed partial class ReferenceLinkViewModel : ObservableObject
{
    /// <summary>What the link reads as — the target's list label, so it matches how the record is named
    /// everywhere else in the editor.</summary>
    public string DisplayName { get; }

    private readonly Action _open;

    public ReferenceLinkViewModel(string displayName, Action open)
    {
        DisplayName = displayName;
        _open = open;
    }

    [RelayCommand]
    private void Open() => _open();
}
