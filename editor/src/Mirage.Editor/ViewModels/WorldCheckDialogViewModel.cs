using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>One finding, worded and ready to click. <see cref="Where"/> names the record it is on,
/// <see cref="What"/> says what is wrong there.</summary>
public sealed class WorldIssueRowViewModel(WorldIssue issue, string ownerName, Action<WorldIssue> go)
{
    public string Where => issue.HasTile
        ? EditorStrings.Format(EditorStrings.WorldCheck_WhereTile,
            ("Kind", KindLabel), ("Num", issue.OwnerNum), ("Name", ownerName), ("X", issue.X), ("Y", issue.Y))
        : EditorStrings.Format(EditorStrings.WorldCheck_WhereRecord,
            ("Kind", KindLabel), ("Num", issue.OwnerNum), ("Name", ownerName));

    public string What => EditorStrings.Format(KeyFor(issue.Kind), ("Detail", issue.Detail));

    /// <summary>The caption on this row's button. On the row rather than on the window because a template
    /// binding that reaches for an ancestor resolves by reflection, and nothing checks it until it runs.</summary>
    public string GoLabel => EditorStrings.Get(EditorStrings.WorldCheck_Go);

    public IRelayCommand GoCommand { get; } = new RelayCommand(() => go(issue));

    private string KindLabel => EditorStrings.Get(issue.OwnerKind switch
    {
        WorldRecordKind.Map => EditorStrings.WorldCheck_KindMap,
        WorldRecordKind.Item => EditorStrings.WorldCheck_KindItem,
        WorldRecordKind.Npc => EditorStrings.WorldCheck_KindNpc,
        WorldRecordKind.Shop => EditorStrings.WorldCheck_KindShop,
        WorldRecordKind.Spell => EditorStrings.WorldCheck_KindSpell,
        WorldRecordKind.Quest => EditorStrings.WorldCheck_KindQuest,
        WorldRecordKind.Conversation => EditorStrings.WorldCheck_KindConversation,
        _ => EditorStrings.WorldCheck_KindClass,
    });

    private static string KeyFor(WorldIssueKind kind) => kind switch
    {
        WorldIssueKind.LinkSizeMismatch => EditorStrings.WorldCheck_LinkSizeMismatch,
        WorldIssueKind.LinkNotReciprocal => EditorStrings.WorldCheck_LinkNotReciprocal,
        WorldIssueKind.LinkOutOfRange => EditorStrings.WorldCheck_LinkOutOfRange,
        WorldIssueKind.WarpMapMissing => EditorStrings.WorldCheck_WarpMapMissing,
        WorldIssueKind.WarpTileOutside => EditorStrings.WorldCheck_WarpTileOutside,
        WorldIssueKind.BootMapMissing => EditorStrings.WorldCheck_BootMapMissing,
        WorldIssueKind.BootTileOutside => EditorStrings.WorldCheck_BootTileOutside,
        WorldIssueKind.MapGroupMissing => EditorStrings.WorldCheck_MapGroupMissing,
        WorldIssueKind.SpawnPinOutside => EditorStrings.WorldCheck_SpawnPinOutside,
        WorldIssueKind.LightOutside => EditorStrings.WorldCheck_LightOutside,
        WorldIssueKind.NpcMissing => EditorStrings.WorldCheck_NpcMissing,
        WorldIssueKind.ItemMissing => EditorStrings.WorldCheck_ItemMissing,
        WorldIssueKind.SpellMissing => EditorStrings.WorldCheck_SpellMissing,
        WorldIssueKind.QuestMissing => EditorStrings.WorldCheck_QuestMissing,
        WorldIssueKind.ClassMissing => EditorStrings.WorldCheck_ClassMissing,
        WorldIssueKind.ConversationNodeMissing => EditorStrings.WorldCheck_ConversationNodeMissing,
        WorldIssueKind.ShopHasNoKeeper => EditorStrings.WorldCheck_ShopHasNoKeeper,
        WorldIssueKind.ConversationOpensNoShop => EditorStrings.WorldCheck_ConversationOpensNoShop,
        WorldIssueKind.ConversationOpensNoQuests => EditorStrings.WorldCheck_ConversationOpensNoQuests,
        _ => EditorStrings.WorldCheck_QuestPrereqCycle,
    };
}

/// <summary>
/// The world check's results.
///
/// <para>Every row clicks through to the record it is about, which is the whole point: a list of faults
/// nobody can navigate to is a list nobody acts on. Following one closes the window, since the record behind
/// it is what the author needs to see.</para>
///
/// <para>The sweep itself is <see cref="WorldCheck"/> and knows nothing about the editor. This wraps it in
/// wording and in a way to get there.</para>
/// </summary>
public sealed partial class WorldCheckDialogViewModel : ObservableObject
{
    /// <summary>Raised when a row is followed, carrying the record to open.</summary>
    public event Action<WorldRecordKind, int>? Navigate;

    /// <summary>Raised when the window should close, whether by a row or by the button.</summary>
    public event Action? Closed;

    public IReadOnlyList<WorldIssueRowViewModel> Rows { get; }

    /// <summary>Whether the sweep found nothing, which is worth saying rather than showing an empty list.</summary>
    public bool IsClean => Rows.Count == 0;

    public string Summary => IsClean
        ? EditorStrings.Get(EditorStrings.WorldCheck_Clean)
        : EditorStrings.Format(EditorStrings.WorldCheck_Summary, ("Count", Rows.Count));

    public WorldCheckDialogViewModel(IReadOnlyList<WorldIssue> issues, Func<WorldRecordKind, int, string> nameOf)
    {
        // Grouped by record so every finding on one map or one shop reads together, and in tile order within
        // a record so a long list is walked rather than searched.
        Rows =
        [
            .. issues
                .OrderBy(i => i.OwnerKind)
                .ThenBy(i => i.OwnerNum)
                .ThenBy(i => i.Y)
                .ThenBy(i => i.X)
                .Select(i => new WorldIssueRowViewModel(i, nameOf(i.OwnerKind, i.OwnerNum), Follow)),
        ];
    }

    private void Follow(WorldIssue issue)
    {
        Navigate?.Invoke(issue.OwnerKind, issue.OwnerNum);
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke();
}
