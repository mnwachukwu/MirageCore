using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits playable classes and the level-1 preview of a character rolled from each.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class ClassEditorView : LocalizedUserControl
{
    public ClassEditorView()
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
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.ClassEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.ClassEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _descLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_DescLabel);
        _descBox.PlaceholderText = EditorStrings.Get(EditorStrings.ClassEditor_DescHint);
        _spriteSheetMaleLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpriteSheetMaleLabel);
        _spriteMaleLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpriteMaleLabel);
        _spriteSheetFemaleLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpriteSheetFemaleLabel);
        _spriteFemaleLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpriteFemaleLabel);
        _strLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_StrLabel);
        _defLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_DefLabel);
        _spdLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpdLabel);
        _intLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_IntLabel);
        _maxHpLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_MaxHpLabel);
        _maxMpLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_MaxMpLabel);
        _maxSpLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_MaxSpLabel);
        _startingStatsNote.Text = EditorStrings.Get(EditorStrings.ClassEditor_StartingStatsNote);
        _regenHeader.Text = EditorStrings.Get(EditorStrings.ClassEditor_RegenHeader);
        _hpRegenLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_HpRegenLabel);
        _mpRegenLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_MpRegenLabel);
        _spRegenLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_SpRegenLabel);
        _combatHeader.Text = EditorStrings.Get(EditorStrings.ClassEditor_CombatHeader);
        _pdmgLabel.Text = EditorStrings.Get(EditorStrings.Common_PhysDmgAbbrev);
        _mdmgLabel.Text = EditorStrings.Get(EditorStrings.Common_MagDmgAbbrev);
        _mitLabel.Text = EditorStrings.Get(EditorStrings.Common_MitAbbrev);
        _notesExpander.Header = EditorStrings.Get(EditorStrings.Common_Notes);
        _fmtVitalsHeader.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_VitalsHeader);
        _fmtVitalsMaxHp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_VitalsMaxHp);
        _fmtVitalsMaxMp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_VitalsMaxMp);
        _fmtVitalsMaxSp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_VitalsMaxSp);
        _fmtVitalsNote.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_VitalsNote);
        _fmtRegenHeader.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_RegenHeader);
        _fmtRegenHp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_RegenHp);
        _fmtRegenMp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_RegenMp);
        _fmtRegenSp.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_RegenSp);
        _fmtRegenNote.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_RegenNote);
        _fmtCombatHeader.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_CombatHeader);
        _fmtCombatPDmg.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_CombatPDmg);
        _fmtCombatMDmg.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_CombatMDmg);
        _fmtCombatMit.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_CombatMit);
        _fmtCombatNote.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_CombatNote);
        _fmtPreviewNote.Text = EditorStrings.Get(EditorStrings.ClassEditor_Formula_PreviewNote);

        _startItemsLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_StartItemsLabel);
        _startSpellsLabel.Text = EditorStrings.Get(EditorStrings.ClassEditor_StartSpellsLabel);
        _addStartItemButton.Content = EditorStrings.Get(EditorStrings.ClassEditor_AddStartItem);
        _addStartSpellButton.Content = EditorStrings.Get(EditorStrings.ClassEditor_AddStartSpell);
        _startSkippedWarning.Text = EditorStrings.Get(EditorStrings.ClassEditor_StartSkippedWarning);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardButton.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllButton.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveClassButton.Content = EditorStrings.Get(EditorStrings.ClassEditor_SaveClassButton);
        _saveAllButton.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.ClassEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.ClassEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.ClassEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.ClassEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
