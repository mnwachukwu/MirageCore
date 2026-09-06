using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Copy duplicates the open record into the first unused slot, dirty, ready to edit.
///
/// <para>Most of what these pin is what copy REFUSES to carry. Three record types point at an NPC through
/// a side-mapping the game resolves by SCANNING — a conversation's speaker, a quest's giver, a shop's
/// keeper — so a verbatim copy leaves two records claiming one NPC and the loser silently never fires.
/// A map's neighbor links are the same shape: the map on the other side still points at the original, so
/// a copy that kept them would assert an adjacency only one side agrees with. None of that is visible in
/// the editor; it shows up as a door that goes nowhere or an NPC with nothing to say.</para>
///
/// <para>The rest is the copy being a real copy: a deep one, so editing the duplicate cannot reach back
/// into the original, and a dirty one, so nothing touches disk until a save says so.</para>
/// </summary>
[TestFixture]
public class RecordCopyTests
{
    // ── Item: the plain case every other record editor shares ─────────────────

    private static ItemEditorViewModel ItemsWith(params ItemRecord[] items)
    {
        var data = new EditorDataService();
        // Slot 0 is the unused sentinel every collection carries; the editor lists from 1.
        var all = new ItemRecord[items.Length + 3];
        for (int i = 0; i < all.Length; i++) all[i] = new ItemRecord();
        for (int i = 0; i < items.Length; i++) all[i + 1] = items[i];
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineItems))!.SetValue(data, all);
        var vm = new ItemEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    private static ItemRecord Sword() => new()
    {
        Name = "Rusty Sword",
        Type = ItemType.Weapon,
        Power = 12,
        Durability = 40,
        Price = 250,
        AllowedClasses = [1, 3],
    };

    [Test]
    public void Copy_LandsInTheFirstEmptySlot_AndAppendsTheMarker()
    {
        var vm = ItemsWith(Sword(), new ItemRecord { Name = "Shield" });
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        vm.CopyCommand.Execute(null);

        var copy = vm.Items.First(i => i.Index == 3);   // 1 and 2 are named, so 3 is first free
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("Rusty Sword (Copy)"));
            Assert.That(vm.SelectedItem, Is.SameAs(copy), "the copy is selected, ready to edit");
        });
    }

    [Test]
    public void Copy_ArrivesDirty_SoNothingReachesDiskUntilSaved()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        vm.CopyCommand.Execute(null);

        var copy = vm.Items.First(i => i.Index == 2);
        Assert.Multiple(() =>
        {
            Assert.That(copy.IsDirty, Is.True, "an unsaved copy is what makes it discardable");
            Assert.That(vm.HasAnyDirty, Is.True);
        });
    }

    /// <summary>Online a row is a name-only placeholder until it is fetched, and selecting an unloaded row
    /// starts that fetch. A copy that left the target unloaded would be overwritten moments later by the
    /// empty record the server still holds for that slot.</summary>
    [Test]
    public void Copy_MarksTheTargetLoaded()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        vm.CopyCommand.Execute(null);

        Assert.That(vm.Items.First(i => i.Index == 2).IsLoaded, Is.True);
    }

    [Test]
    public void Copy_CarriesEveryField()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        vm.CopyCommand.Execute(null);

        var copy = vm.Items.First(i => i.Index == 2);
        Assert.Multiple(() =>
        {
            Assert.That(copy.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(copy.Power, Is.EqualTo(12));
            Assert.That(copy.Durability, Is.EqualTo(40));
            Assert.That(copy.Price, Is.EqualTo(250));
            Assert.That(copy.AllowedClasses, Is.EqualTo(new List<short> { 1, 3 }));
        });
    }

    /// <summary>A shallow copy would share the class list, so restricting the duplicate would silently
    /// restrict the original too — and the original is not even dirty, so the change would never be saved
    /// and would vanish on reload.</summary>
    [Test]
    public void Copy_IsDeep_SoEditingItCannotReachTheOriginal()
    {
        var vm = ItemsWith(Sword());
        var source = vm.Items.First(i => i.Index == 1);
        vm.SelectedItem = source;
        vm.CopyCommand.Execute(null);

        var copy = vm.Items.First(i => i.Index == 2);
        copy.AllowedClasses = [7];
        copy.Power = 99;

        Assert.Multiple(() =>
        {
            Assert.That(source.AllowedClasses, Is.EqualTo(new List<short> { 1, 3 }));
            Assert.That(source.Power, Is.EqualTo(12));
            Assert.That(source.IsDirty, Is.False, "copying reads the original, it does not edit it");
        });
    }

    // ── When Copy is offered at all ───────────────────────────────────────────

    [Test]
    public void CopyIsUnavailable_WithNothingSelected()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = null;

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanCopy, Is.False);
            Assert.That(vm.CopyTooltip, Does.Contain("Select").IgnoreCase);
        });
    }

    /// <summary>An empty slot holds nothing to duplicate; copying one would spend another slot on a
    /// second nothing, named " (Copy)".</summary>
    [Test]
    public void CopyIsUnavailable_WhenTheOpenSlotIsEmpty()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = vm.Items.First(i => i.Index == 2);   // unnamed slot

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanCopy, Is.False);
            Assert.That(vm.CopyTooltip, Does.Contain("empty").IgnoreCase,
                "the disabled button has to say why");
        });
    }

    [Test]
    public void CopyIsAvailable_ForARealRecord()
    {
        var vm = ItemsWith(Sword());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        Assert.That(vm.CanCopy, Is.True);
    }

    [Test]
    public void Copy_WithNoEmptySlotLeft_RefusesAndSaysSo()
    {
        // Every slot named, so there is nowhere for a copy to land.
        var vm = ItemsWith(Sword(), new ItemRecord { Name = "Shield" }, new ItemRecord { Name = "Helm" });
        foreach (var row in vm.Items) row.Name = row.Name.Length > 0 ? row.Name : "Taken";
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);
        int before = vm.Items.Count(i => i.Name.EndsWith(RecordCopy.Suffix, StringComparison.Ordinal));

        vm.CopyCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Count(i => i.Name.EndsWith(RecordCopy.Suffix, StringComparison.Ordinal)),
                Is.EqualTo(before), "nothing was overwritten to make room");
            Assert.That(vm.CanCopy, Is.False, "and the button says so before you press it");
        });
    }

    // ── The references a copy must not duplicate ──────────────────────────────

    [Test]
    public void CopiedShop_ArrivesWithNoKeeper()
    {
        var data = new EditorDataService();
        var shops = new ShopRecord[4];
        for (int i = 0; i < shops.Length; i++) shops[i] = new ShopRecord();
        shops[1] = new ShopRecord { Name = "Fenn's Forge", Keeper = 131 };
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineShops))!.SetValue(data, shops);
        var vm = new ShopEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedShop = vm.Shops.First(s => s.Index == 1);

        vm.CopyCommand.Execute(null);

        var copy = vm.Shops.First(s => s.Index == 2);
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("Fenn's Forge (Copy)"));
            Assert.That(copy.Keeper, Is.Zero,
                "two shops naming one keeper means the server opens only whichever it scans first");
        });
    }

    [Test]
    public void CopiedQuest_ArrivesWithNoGiverOrTurnIn()
    {
        var data = new EditorDataService();
        var quests = new QuestRecord[4];
        for (int i = 0; i < quests.Length; i++) quests[i] = new QuestRecord();
        quests[1] = new QuestRecord { Name = "Sellswords", GiverNpc = 140, TurnInNpc = 141 };
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineQuests))!.SetValue(data, quests);
        var vm = new QuestEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedQuest = vm.Quests.First(q => q.Index == 1);

        vm.CopyCommand.Execute(null);

        var copy = vm.Quests.First(q => q.Index == 2);
        Assert.Multiple(() =>
        {
            Assert.That(copy.GiverNpc, Is.Zero);
            Assert.That(copy.TurnInNpc, Is.Zero);
        });
    }

    [Test]
    public void CopiedConversation_ArrivesWithNoSpeaker()
    {
        var data = new EditorDataService();
        var convs = new ConversationRecord[4];
        for (int i = 0; i < convs.Length; i++) convs[i] = new ConversationRecord();
        convs[1] = new ConversationRecord { Name = "Corrin", SpeakerNpc = 134 };
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineConversations))!.SetValue(data, convs);
        var vm = new ConversationEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedConversation = vm.Conversations.First(c => c.Index == 1);

        vm.CopyCommand.Execute(null);

        Assert.That(vm.Conversations.First(c => c.Index == 2).SpeakerNpc, Is.Zero,
            "the resolver takes the FIRST conversation matching an NPC, so a duplicate claim buries one");
    }
}
