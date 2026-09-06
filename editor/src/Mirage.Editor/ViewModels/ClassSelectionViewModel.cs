using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One class checkbox in a <see cref="ClassSelectionViewModel"/>.</summary>
public sealed partial class ClassToggleViewModel : ObservableObject
{
    public short Id { get; }
    public string Label { get; }

    /// <summary>Raised on a real toggle so the owner can rewrite the record's list. Set by the owner;
    /// suppressed while it is refilling the toggles from a record.</summary>
    internal Action? Toggled;

    [ObservableProperty] private bool _isSelected;

    public ClassToggleViewModel(short id, string label, bool isSelected)
    {
        Id = id;
        Label = label;
        _isSelected = isSelected;   // field, not property — the ctor must not count as an author edit
    }

    partial void OnIsSelectedChanged(bool value) => Toggled?.Invoke();
}

/// <summary>
/// The class multi-select shared by the item, spell and quest editors: one checkbox per class, with
/// "none ticked" meaning every class — the same convention <see cref="ClassGate"/> enforces at runtime,
/// so what the form shows and what the server allows cannot disagree.
///
/// <para>Lives on the EDITOR view-model rather than the row, so there is one set of toggles for the
/// whole list instead of fifty per slot across hundreds of rows. <see cref="Rebuild"/> re-points it at
/// whichever row is selected.</para>
///
/// <para>Writes flow out through <see cref="SelectionChanged"/> rather than the VM reaching into a row:
/// the three editors hold different row types with nothing in common but this field.</para>
/// </summary>
public sealed partial class ClassSelectionViewModel : ObservableObject
{
    /// <summary>A checkbox per real class. Never includes the id-0 "(none)" sentinel the type-ahead
    /// pickers carry — "no restriction" here is every box unticked, not a magic entry.</summary>
    public ObservableCollection<ClassToggleViewModel> Classes { get; } = new();

    /// <summary>Fires when the author ticks or unticks a box, carrying the new stored value: a sorted
    /// list of the ticked ids, or null when nothing is ticked (unrestricted).</summary>
    public event Action<List<short>?>? SelectionChanged;

    // Set while Rebuild is filling the toggles, so seeding them from a record doesn't read as an edit
    // and doesn't write straight back into the row that was just loaded.
    private bool _refilling;

    /// <summary>Whether a row is selected at all; the view hides the control otherwise.</summary>
    [ObservableProperty] private bool _isActive;

    public ClassSelectionViewModel()
    {
        // Summary resolves "Any class" through EditorStrings, so it has to re-read on a live language
        // switch. The checkbox labels come from the class table rather than the string table, and refresh
        // whenever the owner calls Rebuild (row selection, or the entries being invalidated).
        EditorStrings.LanguageChanged += () => OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Repopulate for a newly selected row (or a changed class table). Pass null for
    /// <paramref name="selected"/> — or an empty list — to show everything unticked.</summary>
    public void Rebuild(NamedEntry[] classEntries, IReadOnlyList<short>? selected)
    {
        _refilling = true;
        try
        {
            Classes.Clear();
            // Skip index 0: that slot is the pickers' "(none)" sentinel, not a class.
            for (int id = 1; id < classEntries.Length; id++)
            {
                var entry = classEntries[id];
                if (entry is null) continue;
                // An unnamed slot is an unused class; showing fifty blank checkboxes would bury the real
                // ones. A slot already ticked is shown regardless, so a stale id stays visible and
                // removable rather than silently un-editable.
                bool ticked = selected is not null && selected.Contains((short)id);
                if (!ticked && string.IsNullOrWhiteSpace(entry.Name)) continue;

                string label = string.IsNullOrWhiteSpace(entry.Name)
                    ? $"{id}: {EditorStrings.Get(EditorStrings.Common_EmptyName)}"
                    : entry.ToString();
                Classes.Add(new ClassToggleViewModel((short)id, label, ticked) { Toggled = OnToggled });
            }
        }
        finally
        {
            _refilling = false;
        }
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Clear the control when no row is selected.</summary>
    public void Clear()
    {
        _refilling = true;
        try { Classes.Clear(); }
        finally { _refilling = false; }
        OnPropertyChanged(nameof(Summary));
    }

    private void OnToggled()
    {
        if (_refilling) return;
        SelectionChanged?.Invoke(Current());
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>The ticked ids in ascending order, or null when none are — matching the record's
    /// canonical stored form so a save is a straight assignment.</summary>
    public List<short>? Current()
    {
        List<short>? ids = null;
        foreach (var toggle in Classes)
        {
            if (!toggle.IsSelected) continue;
            ids ??= new List<short>();
            ids.Add(toggle.Id);
        }
        ids?.Sort();
        return ids;
    }

    /// <summary>One-line readout above the boxes, so the common "anyone can use this" case is stated
    /// rather than inferred from fifty empty checkboxes.</summary>
    public string Summary
    {
        get
        {
            var ids = Current();
            if (ids is null) return EditorStrings.Get(EditorStrings.ClassSelector_AnyClass);
            var names = new List<string>(ids.Count);
            foreach (var toggle in Classes)
                if (toggle.IsSelected) names.Add(toggle.Label);
            return string.Join(", ", names);
        }
    }
}
