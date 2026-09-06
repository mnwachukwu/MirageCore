using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The auto-save schedule, driven by an injected clock rather than a real timer.
///
/// <para>This stands in for a feature otherwise only observable by waiting five minutes, and the shapes
/// worth pinning are the ones that fail SILENTLY: a schedule that never fires because something keeps
/// resetting it, and one that fires on every tick because the stamp never moved. Both look like a
/// working feature from the outside.</para>
/// </summary>
[TestFixture]
public class AutoSaveScheduleTests
{
    private static readonly DateTime Start = new(2026, 8, 21, 9, 0, 0, DateTimeKind.Local);

    private static AutoSaveSetting Every(int minutes) =>
        new() { Enabled = true, IntervalMinutes = minutes, Reach = AutoSaveReach.AllDirty };

    [Test]
    public void AnEditorSeenForTheFirstTime_StartsItsClockRatherThanSaving()
    {
        var schedule = new AutoSaveSchedule();

        Assert.That(schedule.IsDue("Items", Every(5), Start), Is.False,
            "enabling auto-save must not fire on the very next tick");
    }

    [Test]
    public void BeforeTheIntervalElapses_NothingIsDue()
    {
        var schedule = new AutoSaveSchedule();
        schedule.IsDue("Items", Every(5), Start);

        Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(4.9)), Is.False);
    }

    [Test]
    public void OnceTheIntervalElapses_ItIsDue()
    {
        var schedule = new AutoSaveSchedule();
        schedule.IsDue("Items", Every(5), Start);

        Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(5)), Is.True);
    }

    /// <summary>The stamp has to move on a due tick even when nothing was dirty. Otherwise the section
    /// stays permanently due and re-asks the editor on every 30-second tick for the rest of the session.</summary>
    [Test]
    public void AfterFiring_TheClockRestarts()
    {
        var schedule = new AutoSaveSchedule();
        schedule.IsDue("Items", Every(5), Start);
        schedule.IsDue("Items", Every(5), Start.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(6)), Is.False);
            Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(10)), Is.True);
        });
    }

    /// <summary>The whole reason for one app-wide ticker: each section keeps its own clock, so the map
    /// editor can save every 5 minutes while the NPC editor saves every 30 — and neither resets the
    /// other by being the one on screen.</summary>
    [Test]
    public void EachSection_KeepsItsOwnClock()
    {
        var schedule = new AutoSaveSchedule();
        schedule.IsDue("Maps", Every(5), Start);
        schedule.IsDue("NPCs", Every(30), Start);

        Assert.Multiple(() =>
        {
            Assert.That(schedule.IsDue("Maps", Every(5), Start.AddMinutes(6)), Is.True);
            Assert.That(schedule.IsDue("NPCs", Every(30), Start.AddMinutes(6)), Is.False);
        });
    }

    [Test]
    public void ADisabledEditor_IsNeverDue()
    {
        var schedule = new AutoSaveSchedule();
        var off = new AutoSaveSetting { Enabled = false, IntervalMinutes = 5 };
        schedule.IsDue("Items", off, Start);

        Assert.That(schedule.IsDue("Items", off, Start.AddHours(3)), Is.False);
    }

    [Test]
    public void Resetting_MakesEverySectionWaitAFullIntervalAgain()
    {
        var schedule = new AutoSaveSchedule();
        schedule.IsDue("Items", Every(5), Start);

        schedule.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(10)), Is.False,
                "the reset re-seeds the clock rather than leaving it overdue");
            Assert.That(schedule.IsDue("Items", Every(5), Start.AddMinutes(16)), Is.True);
        });
    }

    // ── The status line ───────────────────────────────────────────────────────
    // Built through the same formatter the app uses, so renaming a placeholder breaks these rather than
    // shipping a literal "{Subject}" to the status bar.

    [Test]
    public void TheStatusLine_NamesTheRecordAndTheMoment()
    {
        Assert.That(AutoSaveMessages.For(1, "Rusty Sword", Start),
            Is.EqualTo("Auto-Saved Rusty Sword at 2026-08-21 09:00:00."));
    }

    [Test]
    public void TheStatusLine_CountsWhenSeveralWereSaved()
    {
        Assert.That(AutoSaveMessages.For(7, "Rusty Sword", Start),
            Is.EqualTo("Auto-Saved 7 records at 2026-08-21 09:00:00."));
    }

    /// <summary>An unnamed record leaves nothing to name, so the line falls back to the count rather than
    /// reading "Auto-Saved  at …". A blank slot is exactly where that happens.</summary>
    [Test]
    public void TheStatusLine_FallsBackToTheCountForAnUnnamedRecord()
    {
        Assert.That(AutoSaveMessages.For(1, "  ", Start),
            Is.EqualTo("Auto-Saved 1 records at 2026-08-21 09:00:00."));
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    [Test]
    public void EveryEditor_StartsOffAtFiveMinutesSavingEverythingDirty()
    {
        var s = new AutoSaveSetting();

        Assert.Multiple(() =>
        {
            Assert.That(s.Enabled, Is.False, "an editor that writes to disk unasked is a surprise");
            Assert.That(s.IntervalMinutes, Is.EqualTo(5));
            Assert.That(s.Reach, Is.EqualTo(AutoSaveReach.AllDirty));
        });
    }

    [Test]
    public void TheIntervalsOffered_AreTheOnesAskedFor()
    {
        Assert.That(AutoSaveSetting.Intervals, Is.EqualTo(new[] { 5, 10, 15, 30, 60 }));
    }

    /// <summary>Accounts must never appear: those records belong to the server, every save is a live
    /// write, and there is no dirty tracking to drive a schedule from.</summary>
    [Test]
    public void TheAccountEditor_IsNotSchedulable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MainWindowViewModel.AutoSaveSections, Does.Not.Contain("Accounts"));
            Assert.That(MainWindowViewModel.AutoSaveSections, Has.Length.EqualTo(9));
        });
    }

    // ── What a tick actually does ─────────────────────────────────────────────
    // Only the paths that write NOTHING are exercised here. A real save goes through
    // EditorDataService.SaveOffline*Async, which writes into EditorPaths.Data — the live per-user editor
    // store — so a test that saved for real would drop records into whatever world the developer is
    // authoring. The quiet paths are the ones worth pinning anyway: they are what keeps auto-save from
    // announcing work it never did.

    private static ItemEditorViewModel CleanItemEditor()
    {
        var data = new EditorDataService();
        var items = new ItemRecord[4];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemRecord();
        items[1] = new ItemRecord { Name = "Rusty Sword" };
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineItems))!.SetValue(data, items);
        var vm = new ItemEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    [Test]
    public async Task WithNothingDirty_ATickWritesNothing()
    {
        var vm = CleanItemEditor();

        int saved = await vm.AutoSaveAsync(AutoSaveReach.AllDirty);

        Assert.That(saved, Is.Zero, "zero is what keeps the status line from announcing a save that never happened");
    }

    [Test]
    public async Task WithNoRecordOpen_AnOpenRecordTickWritesNothing()
    {
        var vm = CleanItemEditor();
        vm.SelectedItem = null;

        Assert.That(await vm.AutoSaveAsync(AutoSaveReach.OpenRecord), Is.Zero);
    }

    /// <summary>A clean record that happens to be open is not work to save either.</summary>
    [Test]
    public async Task WithACleanRecordOpen_AnOpenRecordTickWritesNothing()
    {
        var vm = CleanItemEditor();
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        Assert.That(await vm.AutoSaveAsync(AutoSaveReach.OpenRecord), Is.Zero);
    }

    /// <summary>The configuration window offers exactly the sections the ticker walks. A row for a
    /// section the ticker skips would be a setting that silently does nothing.</summary>
    [Test]
    public void TheConfigurationWindow_ListsExactlyTheSchedulableEditors()
    {
        var vm = new AutoSaveDialogViewModel(isOnline: false);

        Assert.That(vm.Rows.Select(r => r.Section), Is.EqualTo(MainWindowViewModel.AutoSaveSections));
    }

    [Test]
    public void TheConfigurationWindow_IsReadOnlyWhileConnected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new AutoSaveDialogViewModel(isOnline: true).IsConfigurable, Is.False,
                "auto-save is offline only, so it must not be settable while connected");
            Assert.That(new AutoSaveDialogViewModel(isOnline: false).IsConfigurable, Is.True);
        });
    }
}
