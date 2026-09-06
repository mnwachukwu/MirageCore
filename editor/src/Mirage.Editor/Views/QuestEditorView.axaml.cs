using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits player quests — objectives, rewards, and prerequisites.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class QuestEditorView : LocalizedUserControl
{
    public QuestEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _refsHeader.Text = EditorStrings.Get(EditorStrings.References_Header);
        _noRefs.Text = EditorStrings.Get(EditorStrings.References_None);
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.QuestEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.QuestEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _descriptionLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_DescriptionLabel);
        _giverLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_GiverLabel);
        _turnInLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_TurnInLabel);
        _repeatableLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_RepeatableLabel);
        _cadenceLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_CadenceLabel);
        _requirementsHeader.Text = EditorStrings.Get(EditorStrings.QuestEditor_RequirementsHeader);
        _reqLevelLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_ReqLevelLabel);
        _reqStrLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_ReqStrLabel);
        _reqDefLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_ReqDefLabel);
        _reqSpdLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_ReqSpdLabel);
        _reqIntLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_ReqIntLabel);
        _allowedClassesLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_AllowedClasses);
        _prereqLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_PrereqLabel);
        _objectivesHeader.Text = EditorStrings.Get(EditorStrings.QuestEditor_ObjectivesHeader);
        _objColKind.Text = EditorStrings.Get(EditorStrings.QuestEditor_ObjColKind);
        _objColTarget.Text = EditorStrings.Get(EditorStrings.QuestEditor_ObjColTarget);
        _objColCount.Text = EditorStrings.Get(EditorStrings.QuestEditor_ObjColCount);
        _rewardsHeader.Text = EditorStrings.Get(EditorStrings.QuestEditor_RewardsHeader);
        _rewardExpLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_RewardExpLabel);
        _repeatRewardsHeader.Text = EditorStrings.Get(EditorStrings.QuestEditor_RepeatRewardsHeader);
        _repeatRewardExpLabel.Text = EditorStrings.Get(EditorStrings.QuestEditor_RepeatRewardExpLabel);
        _rewardColItem.Text = EditorStrings.Get(EditorStrings.QuestEditor_RewardColItem);
        _rewardColQty.Text = EditorStrings.Get(EditorStrings.QuestEditor_RewardColQty);
        _noObjectivesHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addObjectiveBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _noRewardsHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addRewardBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _noRepeatRewardsHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addRepeatRewardBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.QuestEditor_SaveQuestButton);
        _saveAllBtn.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
    }

    /// <summary>Persist the panel layout as the view leaves the tree, so switching sections
    /// keeps the splitter position.</summary>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        SavePanelState();
        AppSettings.Current.Save();
    }

    /// <summary>Restore the saved splitter width once the visual tree exists.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.QuestEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.QuestEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.QuestEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.QuestEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
