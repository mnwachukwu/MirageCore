using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
namespace Mirage.Editor.ViewModels;

/// <summary>One authored dialogue node: a stable Id, an optional speaker override, the spoken Text, and its own
/// dynamic list of choices — the second nesting level over the quest pattern (a node owns Add/Remove-choice
/// commands, like the quest row owns its objective/reward tables). An empty node (no text AND no real choices) is
/// dropped on save; the Id is preserved so choices keep referencing it.</summary>
public sealed partial class ConversationNodeRowViewModel : ObservableObject
{
    private const int MaxChoices = Constants.MaxConversationChoices;
    private readonly Func<NamedEntry[]> _nodeEntriesProvider;

    /// <summary>Stable node id (assigned on add by the parent conversation, never reused), referenced by choices.</summary>
    public int NodeId { get; }

    [ObservableProperty] private string _speaker = "";
    [ObservableProperty] private string _text = "";

    public ObservableCollection<ConversationChoiceRowViewModel> Choices { get; } = [];
    public bool HasNoChoices => Choices.Count == 0;

    // Localized labels/placeholders bound inside the node DataTemplate (per-instance, so not code-behind-set).
    public string SpeakerPlaceholder => EditorStrings.Get(EditorStrings.ConversationEditor_NodeSpeakerPlaceholder);
    public string TextPlaceholder => EditorStrings.Get(EditorStrings.ConversationEditor_NodeTextPlaceholder);
    public string ChoicesLabel => EditorStrings.Get(EditorStrings.ConversationEditor_ChoicesLabel);
    public string NoChoicesHint => EditorStrings.Get(EditorStrings.Common_NoRowsHint);
    public string AddChoiceLabel => EditorStrings.Get(EditorStrings.Common_AddRow);

    private bool _dirty;
    private bool _loading;

    public bool IsDirty => _dirty || Choices.Any(c => c.IsDirty);

    /// <summary>Row heading + the label shown for this node in a choice's NextNode picker: "#id: text preview".</summary>
    public string Header
    {
        get
        {
            var t = Text.TrimEnd();
            if (t.Length > 30) t = t[..30] + "...";
            return t.Length == 0 ? $"#{NodeId}" : $"#{NodeId}: {t}";
        }
    }

    public ConversationNodeRowViewModel(int nodeId, ConversationNode n, Func<NamedEntry[]> nodeEntriesProvider)
    {
        NodeId = nodeId;
        _nodeEntriesProvider = nodeEntriesProvider;
        _speaker = n.Speaker;
        _text = n.Text;

        Choices.CollectionChanged += OnChoicesChanged;
        _loading = true;
        for (int i = 0; i < n.Choices.Count; i++)
            Choices.Add(new ConversationChoiceRowViewModel(i + 1, n.Choices[i], _nodeEntriesProvider));
        _loading = false;
    }

    private void OnChoicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ObservableObject r in e.OldItems) r.PropertyChanged -= OnChoicePropertyChanged;
        if (e.NewItems is not null)
            foreach (ObservableObject r in e.NewItems) r.PropertyChanged += OnChoicePropertyChanged;
        OnPropertyChanged(nameof(HasNoChoices));
        AddChoiceCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanAddChoice))]
    private void AddChoice() =>
        Choices.Add(new ConversationChoiceRowViewModel(Choices.Count + 1, new ConversationChoice(), _nodeEntriesProvider));
    private bool CanAddChoice() => Choices.Count < MaxChoices;
    [RelayCommand]
    private void RemoveChoice(ConversationChoiceRowViewModel row) => Choices.Remove(row);

    private void OnChoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnSpeakerChanged(string value) => MarkDirty();
    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(Header));
        MarkDirty();
    }

    /// <summary>An unused node — no text AND no real choices. Dropped when the conversation is saved.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Choices.All(c => c.IsEmpty);

    public void ClearDirty()
    {
        _dirty = false;
        foreach (var c in Choices) c.ClearDirty();
        OnPropertyChanged(nameof(IsDirty));
    }

    public void NotifyEntriesChanged()
    {
        foreach (var c in Choices) c.NotifyEntriesChanged();
    }

    public ConversationNode ToRecord()
    {
        var n = new ConversationNode { Id = NodeId, Speaker = Speaker, Text = Text };
        foreach (var c in Choices) if (!c.IsEmpty) n.Choices.Add(c.ToRecord());
        return n;
    }
}
