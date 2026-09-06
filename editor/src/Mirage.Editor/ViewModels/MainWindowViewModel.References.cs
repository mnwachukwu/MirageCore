using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// "What refers to this record?" for every editor.
///
/// <para>References in this data model only ever run one way — a map names its group, an NPC names the item it
/// drops, a shop names its keeper. No record carries a list of its dependents, so the answer is always a scan
/// of the OTHER collections, which is why it lives here: this class is the only one holding every editor.</para>
///
/// <para>Nothing is cached. The scan reads records already resident (the connect-time eager load pulls every
/// collection), it runs once per selection rather than per frame, and a cache would have to be invalidated on
/// every edit in every editor — which is exactly the bookkeeping this avoids having.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    private void WireReferenceScans()
    {
        MapGroupEditor.ResolveGroupMaps = id => MapEditor.Maps
            .Where(m => m.Record.MapGroup == id)
            .Select(m => Link(m.DisplayName, () => OpenMap(m.Index)))
            .ToList();

        ItemEditor.ResolveInboundRefs = RefsToItem;
        SpellEditor.ResolveInboundRefs = RefsToSpell;
        NpcEditor.ResolveInboundRefs = RefsToNpc;
        QuestEditor.ResolveInboundRefs = RefsToQuest;
        ClassEditor.ResolveInboundRefs = RefsToClass;
        // Shops and conversations are pointed FROM, never TO: a shop names its keeper, a conversation names
        // its speaker. Nothing in the world names a shop or a conversation, so those two editors get no panel.
        // The map editor gets none either, though plenty points at a map: its own Up/Down/Left/Right fields
        // and the group panel above already say who a map's neighbors are, and a mapper is placing tiles.
    }

    /// <summary>Re-read the reference panel on whichever editor is showing. The referring records live in
    /// other editors, so the one on screen cannot see them change — the eager load lands long after the lists
    /// are built, and an edit in another section adds or removes references behind this one's back.</summary>
    private void RefreshReferences()
    {
        MapGroupEditor.NotifyGroupMapsChanged();
        ItemEditor.NotifyInboundRefsChanged();
        SpellEditor.NotifyInboundRefsChanged();
        NpcEditor.NotifyInboundRefsChanged();
        QuestEditor.NotifyInboundRefsChanged();
        ClassEditor.NotifyInboundRefsChanged();
    }

    // ── Link construction ────────────────────────────────────────────────────

    private static ReferenceLinkViewModel Link(string label, Action open) => new(label, open);

    /// <summary>A group, or nothing when no record matched. Empty groups are dropped rather than shown with a
    /// heading and no rows — a heading is a claim that there is something under it.</summary>
    private static void AddGroup(List<ReferenceGroupViewModel> into, string headerKey,
                                 IEnumerable<ReferenceLinkViewModel> links)
    {
        var list = links.ToList();
        if (list.Count > 0) into.Add(new ReferenceGroupViewModel(EditorStrings.Get(headerKey), list));
    }

    private IEnumerable<ReferenceLinkViewModel> ItemLinks(Func<ItemRecord, bool> names) =>
        ItemEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("Items", ItemEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> NpcLinks(Func<NpcRecord, bool> names) =>
        NpcEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("NPCs", NpcEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> ShopLinks(Func<ShopRecord, bool> names) =>
        ShopEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("Shops", ShopEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> SpellLinks(Func<SpellRecord, bool> names) =>
        SpellEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("Spells", SpellEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> QuestLinks(Func<QuestRecord, bool> names) =>
        QuestEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("Quests", QuestEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> ClassLinks(Func<ClassRecord, bool> names) =>
        ClassEditor.Items.Where(r => names(r.ToRecord())).Select(r => Link(r.DisplayName, () => Open("Classes", ClassEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> ConversationLinks(Func<ConversationRecord, bool> names) =>
        ConversationEditor.Items.Where(r => names(r.ToRecord()))
            .Select(r => Link(r.DisplayName, () => Open("Conversations", ConversationEditor, r.Index)));

    private IEnumerable<ReferenceLinkViewModel> MapLinks(Func<MapRecord, bool> names) =>
        MapEditor.Maps.Where(m => names(m.Record)).Select(m => Link(m.DisplayName, () => OpenMap(m.Index)));

    // ── The reference graph, one method per target ───────────────────────────

    private IReadOnlyList<ReferenceGroupViewModel> RefsToItem(int num)
    {
        var groups = new List<ReferenceGroupViewModel>();
        AddGroup(groups, EditorStrings.References_DroppedBy,
            NpcLinks(n => n.Drops?.Any(d => d.ItemNum == num) ?? false));
        AddGroup(groups, EditorStrings.References_SoldBy,
            ShopLinks(s => s.SalesItem.Contains(num) || s.BarterItem.Any(b => b.GiveItem == num || b.GetItem == num)));
        AddGroup(groups, EditorStrings.References_RewardedBy,
            QuestLinks(q => q.RewardItems.Any(r => r.ItemNum == num)
                         || q.RepeatRewardItems.Any(r => r.ItemNum == num)
                         || q.Objectives.Any(o => o.Kind is ObjectiveKind.Gather or ObjectiveKind.Fetch && o.Target == num)));
        AddGroup(groups, EditorStrings.References_ReagentFor, SpellLinks(s => s.ItemNum == num));
        AddGroup(groups, EditorStrings.References_StartingGearFor,
            ClassLinks(c => c.StartingItems?.Any(i => i.ItemNum == num) == true));
        return groups;
    }

    private IReadOnlyList<ReferenceGroupViewModel> RefsToSpell(int num)
    {
        var groups = new List<ReferenceGroupViewModel>();
        AddGroup(groups, EditorStrings.References_TaughtBy,
            ItemLinks(i => i.Type == ItemType.Spell && i.SpellNum == num));
        AddGroup(groups, EditorStrings.References_StartingSpellFor,
            ClassLinks(c => c.StartingSpells?.Contains(num) == true));
        return groups;
    }

    private IReadOnlyList<ReferenceGroupViewModel> RefsToNpc(int num)
    {
        var groups = new List<ReferenceGroupViewModel>();
        AddGroup(groups, EditorStrings.References_GivesQuest, QuestLinks(q => q.GiverNpc == num));
        AddGroup(groups, EditorStrings.References_TakesQuest,
            QuestLinks(q => q.EffectiveTurnInNpc == num && q.GiverNpc != num));
        AddGroup(groups, EditorStrings.References_KilledFor,
            QuestLinks(q => q.Objectives.Any(o => o.Kind == ObjectiveKind.Kill && o.Target == num)));
        AddGroup(groups, EditorStrings.References_KeepsShop, ShopLinks(s => s.Keeper == num));
        AddGroup(groups, EditorStrings.References_Speaks, ConversationLinks(c => c.SpeakerNpc == num));
        AddGroup(groups, EditorStrings.References_SpawnsOn, MapLinks(m => m.Npcs.Any(e => e.Npc == num)));
        AddGroup(groups, EditorStrings.References_GroupedWith, AlliedNpcLinks(num));
        return groups;
    }

    /// <summary>The other NPCs sharing this one's alliance group — a sibling relation rather than an inbound
    /// reference, but it answers the same "who else is involved here?" question the panel exists for. Group 0
    /// means ungrouped, which is every NPC's default and so names no allies.</summary>
    private IEnumerable<ReferenceLinkViewModel> AlliedNpcLinks(int num)
    {
        var group = NpcEditor.Items.FirstOrDefault(r => r.Index == num)?.Group ?? 0;
        if (group == 0) return [];
        return NpcEditor.Items
            .Where(r => r.Index != num && r.Group == group)
            .Select(r => Link(r.DisplayName, () => Open("NPCs", NpcEditor, r.Index)));
    }

    private IReadOnlyList<ReferenceGroupViewModel> RefsToQuest(int num)
    {
        var groups = new List<ReferenceGroupViewModel>();
        AddGroup(groups, EditorStrings.References_PrerequisiteFor, QuestLinks(q => q.PrereqQuest == num));
        return groups;
    }

    private IReadOnlyList<ReferenceGroupViewModel> RefsToClass(int num)
    {
        var groups = new List<ReferenceGroupViewModel>();
        AddGroup(groups, EditorStrings.References_RestrictedItems,
            ItemLinks(i => i.AllowedClasses is { Count: > 0 } a && a.Contains((short)num)));
        AddGroup(groups, EditorStrings.References_RestrictedSpells,
            SpellLinks(s => s.AllowedClasses is { Count: > 0 } a && a.Contains((short)num)));
        return groups;
    }

    // ── Following a link ─────────────────────────────────────────────────────

    /// <summary>Show <paramref name="section"/> with <paramref name="index"/> selected. A number naming no row
    /// changes nothing at all, rather than switching the user to a section showing the wrong record.</summary>
    private void Open<TRow>(string section, EditorViewModelBase<TRow> editor, int index)
        where TRow : class, System.ComponentModel.INotifyPropertyChanged
    {
        if (!editor.TrySelect(index)) return;
        SelectedSection = _sectionMap[section];
    }
}
