using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>Which transition raised the unsaved-changes prompt. It decides the wording, and separately
/// whether committing writes to disk or sends to the server — a transition that ends offline has to write
/// where the work will still be readable.</summary>
public enum PushChangesReason
{
    /// <summary>Going online. What is dirty is offline work, so committing writes it to disk.</summary>
    Connecting,
    /// <summary>The connection dropped and came back.</summary>
    Reconnecting,
    /// <summary>Leaving the server deliberately.</summary>
    Disconnecting,
    /// <summary>Closing the window.</summary>
    Closing,
    /// <summary>Opening, creating or closing a world — every record is replaced, and what is dirty belongs
    /// to the world being left, so committing writes it to that world's folder.</summary>
    SwitchingWorld,
}

/// <summary>
/// The unsaved-changes prompt shown before any transition that would discard edits — connecting,
/// reconnecting, disconnecting, switching world, or closing the window. Lists every dirty row across all
/// editors and offers to commit them first.
/// <para>Which situation applies is carried by <see cref="PushChangesReason"/> and changes the wording
/// (<see cref="MessageText"/>, <see cref="SaveButtonText"/>, <see cref="ProceedButtonText"/>) plus where a
/// commit lands.</para>
/// <para>The caller drives the outcome through <see cref="ProceedConfirmed"/> and
/// <see cref="Canceled"/>; this view-model never closes the dialog itself.</para>
/// </summary>
public sealed partial class PushChangesDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _dirtyItems;
    private readonly EditorConnection _conn;
    private readonly EditorDataService _data;
    private readonly PushChangesReason _reason;
    /// <summary>Whether a commit writes to the offline folder rather than sending editor-save packets.
    /// Kept apart from <see cref="_reason"/> because closing answers it from the connection state, and
    /// because two transitions that read the same to an author can land in different places.</summary>
    private readonly bool _commitsToDisk;
    /// <summary>Whether the prompt was raised by a dropped connection rather than a user action.</summary>
    public bool IsReconnecting => _reason == PushChangesReason.Reconnecting;

    [ObservableProperty] private string _statusMessage = "";
    /// <summary>True while a commit is in flight, so the buttons can disable.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Human-readable "type index: name" line for each dirty row, for the dialog's list.</summary>
    public ObservableCollection<string> DirtyNames { get; } = [];

    /// <summary>Prompt body, worded for the situation that raised the dialog.</summary>
    public string MessageText => EditorStrings.Get(_reason switch
    {
        PushChangesReason.Closing => EditorStrings.PushChangesDialog_UnsavedOnClose,
        PushChangesReason.Reconnecting => EditorStrings.PushChangesDialog_UnsavedPush,
        PushChangesReason.Connecting => EditorStrings.PushChangesDialog_UnsavedConnect,
        PushChangesReason.SwitchingWorld => EditorStrings.PushChangesDialog_UnsavedSwitchWorld,
        _ => EditorStrings.PushChangesDialog_UnsavedOnline,
    });

    /// <summary>Caption for the commit-then-continue button.</summary>
    public string SaveButtonText => EditorStrings.Get(_reason switch
    {
        PushChangesReason.Closing => EditorStrings.PushChangesDialog_SaveAndClose,
        PushChangesReason.Reconnecting => EditorStrings.PushChangesDialog_PushAndContinue,
        PushChangesReason.Connecting => EditorStrings.PushChangesDialog_SaveAndConnect,
        PushChangesReason.SwitchingWorld => EditorStrings.PushChangesDialog_SaveAndContinue,
        _ => EditorStrings.PushChangesDialog_PushAndDisconnect,
    });

    /// <summary>Caption for the discard-then-continue button.</summary>
    public string ProceedButtonText => EditorStrings.Get(_reason switch
    {
        PushChangesReason.Closing => EditorStrings.PushChangesDialog_DiscardAndClose,
        PushChangesReason.Reconnecting or PushChangesReason.SwitchingWorld
            => EditorStrings.PushChangesDialog_DiscardAndContinue,
        PushChangesReason.Connecting => EditorStrings.PushChangesDialog_DiscardAndConnect,
        _ => EditorStrings.PushChangesDialog_DiscardAndDisconnect,
    });

    /// <summary>Raised when the author chose to go ahead, whether they committed or discarded.</summary>
    public event Action? ProceedConfirmed;
    /// <summary>Raised when the author backed out; the caller should abandon the transition.</summary>
    public event Action? Canceled;

    /// <summary>Alias of <see cref="ProceedConfirmed"/> for the disconnect call sites.</summary>
    public event Action? DisconnectConfirmed
    {
        add => ProceedConfirmed += value;
        remove => ProceedConfirmed -= value;
    }

    public PushChangesDialogViewModel(
        IReadOnlyList<object> dirtyItems,
        EditorConnection conn,
        EditorDataService data,
        PushChangesReason reason = PushChangesReason.Disconnecting)
    {
        _dirtyItems = dirtyItems;
        _conn = conn;
        _data = data;
        _reason = reason;
        _commitsToDisk = reason switch
        {
            PushChangesReason.Connecting or PushChangesReason.SwitchingWorld => true,
            // Closing while offline has no server to push to.
            PushChangesReason.Closing => !data.IsOnline,
            _ => false,
        };

        // The dirty set arrives as a flat object list (it spans every editor), so each row type is
        // matched back to its own caption format here.
        foreach (var item in dirtyItems)
        {
            DirtyNames.Add(item switch
            {
                ItemRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyItem, ("Index", vm.Index), ("Name", vm.Name)),
                NpcRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyNpc, ("Index", vm.Index), ("Name", vm.Name)),
                ShopRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyShop, ("Index", vm.Index), ("Name", vm.Name)),
                QuestRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyQuest, ("Index", vm.Index), ("Name", vm.Name)),
                ConversationRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyConversation, ("Index", vm.Index), ("Name", vm.Name)),
                SpellRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtySpell, ("Index", vm.Index), ("Name", vm.Name)),
                MapRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyMap, ("Index", vm.Index), ("Name", vm.Record.Name)),
                MapGroupRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyMapGroup, ("Index", vm.Index), ("Name", vm.Name)),
                ClassRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyClass, ("Index", vm.Index), ("Name", vm.Name)),
                _ => item.ToString() ?? EditorStrings.Get(EditorStrings.PushChangesDialog_DirtyUnknown),
            });
        }
    }

    /// <summary>Commit every dirty row, then raise <see cref="ProceedConfirmed"/>. Offline rows go
    /// straight to disk; online rows are sent as editor-save packets. A failure leaves the status
    /// line set and does NOT raise the event, so the transition is abandoned.</summary>
    // ONE case per row type, each carrying both modes, so a type cannot be handled offline and forgotten
    // online — a missing arm skips the row silently AND leaves it dirty (ClearDirty is never reached).
    // Neither arm restates a field mapping: offline goes through the row's ToRecord, online through its
    // BuildSavePacket — the very projections the per-type editors save with, so this path can't drift
    // from them and drop fields the normal save writes.
    [RelayCommand]
    private async Task SaveAndProceedAsync()
    {
        IsBusy = true;
        StatusMessage = _commitsToDisk ? EditorStrings.Get(EditorStrings.PushChangesDialog_Saving) : EditorStrings.Get(EditorStrings.PushChangesDialog_Pushing);
        try
        {
            foreach (var item in _dirtyItems)
            {
                switch (item)
                {
                    case ItemRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineItemAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case NpcRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineNpcAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ShopRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineShopAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case QuestRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineQuestAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ConversationRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineConversationAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case SpellRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineSpellAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case MapRowViewModel vm:
                        // Bump before either save — the server ignores the packet's Revision and does its
                        // own bump; the local bump is a UI mirror (see MapRowViewModel.BumpRevision).
                        vm.BumpRevision();
                        if (_commitsToDisk)
                        {
                            await _data.SaveOfflineMapAsync(vm.Index, vm.Record);
                        }
                        else
                        {
                            await _conn.SendSaveAsync(
                            EditorDataService.BuildSaveMapPacket(vm.Index, vm.Record));
                        }

                        vm.ClearDirty();
                        break;
                    case MapGroupRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineMapGroupAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ClassRowViewModel vm:
                        if (_commitsToDisk) await _data.SaveOfflineClassAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                }
            }
            ProceedConfirmed?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.PushChangesDialog_Error, ("Error", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DiscardAndProceed() => ProceedConfirmed?.Invoke();

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
