using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
namespace Mirage.Editor.Controls;

/// <summary>
/// An <see cref="AutoCompleteBox"/> that behaves like a combo box: the list drops open on click or
/// Tab focus rather than only after typing, and stale text is cleared when the selection is reset.
/// <para>Keeps the base control's style key so it picks up the stock AutoCompleteBox theme.</para>
/// </summary>
public class DropdownAutoCompleteBox : AutoCompleteBox
{
    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    // Set when the user explicitly selects the id=0 "(none)" entry, so the binding-driven
    // null that immediately follows still clears the text box even while the control has focus.
    private bool _clearTextOnNextNull;

    // The configured filter, parked while the list is being browsed rather than searched.
    private AutoCompleteFilterPredicate<object?>? _parkedFilter;

    public DropdownAutoCompleteBox()
    {
        // Only open on Tab navigation. Pointer-driven GotFocus events (including focus
        // returning from the popup after an item is selected) must not reopen the dropdown.
        GotFocus += (_, e) =>
        {
            if (e.NavigationMethod == NavigationMethod.Tab)
                Dispatcher.UIThread.Post(() => { ShowEveryItem(); IsDropDownOpen = true; });
        };

        // Capture IsDropDownOpen synchronously at press time, before AutoCompleteBox
        // runs its selection logic. If the dropdown was already open the press landed on
        // a list item — AutoCompleteBox will close it; do not reopen in the deferred post.
        AddHandler(PointerPressedEvent,
            (_, _) =>
            {
                bool wasOpen = IsDropDownOpen;
                Dispatcher.UIThread.Post(() =>
                {
                    if (wasOpen) return;
                    ShowEveryItem();
                    IsDropDownOpen = true;
                });
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Typing turns the list back into a search. Anything else — arrows, Enter, a click on an item —
        // leaves the full list up.
        AddHandler(TextInputEvent, (_, _) => FilterAsTyped(), RoutingStrategies.Bubble, handledEventsToo: true);
        DropDownClosed += (_, _) => FilterAsTyped();
    }

    /// <summary>
    /// Drops the filter so opening the list offers every entry.
    ///
    /// <para><see cref="AutoCompleteBox"/> filters against its own <c>Text</c>, and once something is
    /// selected that text is the selected entry's caption. Filtering against it offers only the entry
    /// already chosen — a list of one that reads as "there is nothing else here".</para>
    /// </summary>
    private void ShowEveryItem()
    {
        if (FilterMode == AutoCompleteFilterMode.None) return;
        _parkedFilter = ItemFilter;
        FilterMode = AutoCompleteFilterMode.None;
    }

    // Restores the configured filter. Assigning ItemFilter is what puts FilterMode back to Custom.
    private void FilterAsTyped()
    {
        if (_parkedFilter is null) return;
        ItemFilter = _parkedFilter;
        _parkedFilter = null;
    }

    /// <summary>Makes the text box agree with an empty selection, discarding anything typed into it. The map
    /// properties panel calls this on every record load; a filled selection captions itself and is left alone.</summary>
    public void ResyncTextToSelection()
    {
        if (SelectedItem is null) Text = string.Empty;
    }

    /// <summary>Watches <c>SelectedItem</c> so the text box doesn't keep a stale caption after the
    /// selection is cleared — either by a binding reset or by the author picking "(none)".</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SelectedItemProperty) return;

        if (change.NewValue is null)
        {
            // Clear stale text when:
            //   (a) focus is elsewhere — binding-driven reset (e.g. switching selected map), or
            //   (b) the user just explicitly selected the id=0 "(none)" entry.
            if (!IsKeyboardFocusWithin || _clearTextOnNextNull)
                Text = string.Empty;
            _clearTextOnNextNull = false;
        }
        else if (change.NewValue is NamedEntry { Id: 0 })
        {
            // "(none)" was picked — the binding will immediately set SelectedItem back to
            // null (EntryFor returns null for id=0). Flag so that null-change clears the text.
            _clearTextOnNextNull = true;
        }
    }
}
