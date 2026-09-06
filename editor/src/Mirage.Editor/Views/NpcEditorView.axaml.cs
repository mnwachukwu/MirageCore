using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits NPC templates — stats, behavior, drops, size, and the live stat readout.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class NpcEditorView : LocalizedUserControl
{
    public NpcEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _refsHeader.Text = EditorStrings.Get(EditorStrings.References_Header);
        _noRefs.Text = EditorStrings.Get(EditorStrings.References_None);
        _behaviorFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.NpcEditor_AllBehaviorsFilter);
        _nameFilterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_FilterByName);

        _selectPromptText.Text = EditorStrings.Get(EditorStrings.NpcEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.NpcEditor_SectionTitle);

        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _attackSayLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_AttackSayLabel);
        _spriteSheetLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpriteSheetLabel);
        _spriteLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpriteLabel);
        _sizeLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SizeLabel);
        _spawnSecsLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpawnSecsLabel);
        _behaviorLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_BehaviorLabel);
        _isBossCheck.Content = EditorStrings.Get(EditorStrings.NpcEditor_IsBossLabel);
        _lightingHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightingHeader);
        _emitsLightCheck.Content = EditorStrings.Get(EditorStrings.NpcEditor_EmitsLightLabel);
        _lightColorLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightColorLabel);
        _lightRadiusLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightRadiusLabel);
        _lightIntensityLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightIntensityLabel);
        _lightFlickerLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightFlickerLabel);
        _groupLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_GroupLabel);
        _rangeLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_RangeLabel);
        // One label for the whole table now, plus the add-row button. The per-field labels the single
        // drop had (chance / item / quantity) are column positions in the table instead.
        _dropTableLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropTableLabel);
        _dropItemHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropQuantityHeader);
        _dropChanceHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropChanceHeader);
        _addDropButton.Content = EditorStrings.Get(EditorStrings.NpcEditor_AddDrop);

        _strLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_StrLabel);
        _defLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_DefLabel);
        _spdLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpdLabel);
        _intLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_IntLabel);
        _extraHpLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_ExtraHpLabel);
        _extraHpNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_ExtraHpNote);

        _totalStatsLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_TotalStatsLabel);
        _equivLevelLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_EquivLevelLabel);
        _levelNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_LevelNote);

        _vitalsHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_VitalsHeader);
        _maxHpLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_MaxHpLabel);
        _maxMpLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_MaxMpLabel);
        _maxSpLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_MaxSpLabel);

        _regenHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_RegenHeader);
        _hpRegenLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_HpRegenLabel);
        _mpRegenLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_MpRegenLabel);
        _spRegenLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpRegenLabel);

        _effHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_EffectivenessHeader);
        _pdmgLabel.Text = EditorStrings.Get(EditorStrings.Common_PhysDmgAbbrev);
        _mdmgLabel.Text = EditorStrings.Get(EditorStrings.Common_MagDmgAbbrev);
        _mitLabel.Text = EditorStrings.Get(EditorStrings.Common_MitAbbrev);

        _chanceHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_ChanceHeader);
        _critLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_PCritLabel);
        _spellCritLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_MCritLabel);
        _blockLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_BlockLabel);
        _dodgeLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_DodgeLabel);

        _rewardsHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_RewardsHeader);
        _expLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_ExpLabel);
        _previewLevelLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_PreviewLevelLabel);
        _notesExpander.Header = EditorStrings.Get(EditorStrings.Common_Notes);

        // The drop-item picker is per-ROW now, so its placeholder is bound through
        // NpcDropRowViewModel.ItemPlaceholder rather than set once on a single control here.

        _fmtVitalsHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsHeader);
        _fmtVitalsBaseHp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsBaseHp);
        _fmtVitalsFavorPct.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsFavorPct);
        _fmtVitalsMaxHp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsMaxHp);
        _fmtVitalsMaxMp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsMaxMp);
        _fmtVitalsMaxSp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsMaxSp);
        _fmtVitalsNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_VitalsNote);

        _fmtRegenHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_RegenHeader);
        _fmtRegenHp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_RegenHp);
        _fmtRegenMp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_RegenMp);
        _fmtRegenSp.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_RegenSp);
        _fmtRegenNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_RegenNote);

        _fmtCombatHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatHeader);
        _fmtCombatPDmg.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatPDmg);
        _fmtCombatMDmg.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatMDmg);
        _fmtCombatMit.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatMit);
        _fmtCombatFloor.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatFloor);
        _fmtCombatNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_CombatNote);

        _fmtExpHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ExpHeader);
        _fmtExpLine1.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ExpLine1);
        _fmtExpLine2.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ExpLine2);
        _fmtExpNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ExpNote);

        _fmtDropChanceHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceHeader);
        _fmtDropChance1.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceLine1);
        _fmtDropChance2.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceLine2);
        _fmtDropChance3.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceLine3);
        _fmtDropChance4.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceLine4);
        _fmtDropChance5.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceLine5);
        _fmtDropChanceNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_DropChanceNote);

        _fmtChancesHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesHeader);
        _fmtChancesCrit.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesCrit);
        _fmtChancesSpellCrit.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesSpellCrit);
        _fmtChancesBlock.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesBlock);
        _fmtChancesDodge.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesDodge);
        _fmtChancesNote.Text = EditorStrings.Get(EditorStrings.NpcEditor_Formula_ChancesNote);

        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveNpcBtn.Content = EditorStrings.Get(EditorStrings.NpcEditor_SaveNpcButton);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.NpcEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.NpcEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.NpcEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.NpcEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
