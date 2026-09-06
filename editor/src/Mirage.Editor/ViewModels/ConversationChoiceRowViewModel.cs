using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Mirage.Editor.ViewModels;

/// <summary>One authored dialogue choice: a label, the next node to go to (a picker of the conversation's own
/// nodes; "(End)" = end the conversation), and an optional hand-off Action. Mirrors QuestObjectiveRowViewModel,
/// but the NextNode picker is SELF-REFERENTIAL to the conversation (its node ids can have gaps, so it's a linear
/// find, not an id-indexed array). An empty row (blank label) is dropped on save.</summary>
public sealed partial class ConversationChoiceRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _nodeEntriesProvider;

    public int SlotIndex { get; }

    public IEnumerable<ConversationAction> Actions { get; } = Enum.GetValues<ConversationAction>();

    // Localized placeholders bound inside the choice DataTemplate.
    public string LabelPlaceholder => EditorStrings.Get(EditorStrings.ConversationEditor_ChoiceLabelPlaceholder);
    public string NextPlaceholder => EditorStrings.Get(EditorStrings.ConversationEditor_ChoiceNextPlaceholder);

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private int _nextNodeId;
    [ObservableProperty] private ConversationAction _action;

    public bool IsDirty { get; private set; }

    // The conversation's nodes (+ an "(End)" entry at id 0) as the NextNode options.
    public NamedEntry[] NextNodeEntries => _nodeEntriesProvider();

    public NamedEntry? SelectedNextNode
    {
        // id 0 ("(End)") is the NULL sentinel, not a selectable value: return null so DropdownAutoCompleteBox
        // clears its text box. A non-empty box (e.g. "0: (End)") makes the type-ahead filter hide every node,
        // which froze every choice's picker on "(End)" with no node selectable. Mirror of EntryFor's id>0 gate.
        get => NextNodeId <= 0 ? null : NextNodeEntries.FirstOrDefault(e => e.Id == NextNodeId);
        set
        {
            var id = value?.Id ?? 0;
            if (NextNodeId == id) return;
            NextNodeId = id;
        }
    }

    public ConversationChoiceRowViewModel(int slotIndex, ConversationChoice c, Func<NamedEntry[]> nodeEntriesProvider)
    {
        SlotIndex = slotIndex;
        _nodeEntriesProvider = nodeEntriesProvider;
        _label = c.Label;
        _nextNodeId = c.NextNodeId;
        _action = c.Action;
    }

    partial void OnLabelChanged(string value) => IsDirty = true;
    partial void OnNextNodeIdChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedNextNode));
    }
    partial void OnActionChanged(ConversationAction value) => IsDirty = true;

    /// <summary>An unused choice row — blank label. Dropped when the conversation is saved.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Label);

    public void ClearDirty() => IsDirty = false;

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(NextNodeEntries));
        OnPropertyChanged(nameof(SelectedNextNode));
    }

    public ConversationChoice ToRecord() => new() { Label = Label, NextNodeId = NextNodeId, Action = Action };
}
