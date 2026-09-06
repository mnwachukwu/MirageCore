using Avalonia.Controls;
using Avalonia.Threading;

namespace Mirage.Editor.Views;

/// <summary>Closing a dialog on a later dispatcher beat instead of inside the input handler that asked.</summary>
internal static class WindowCloseExtensions
{
    /// <summary>Queues the close, so window disposal runs after the current input dispatch and render commit
    /// rather than on top of them.
    ///
    /// <para>Closing straight from a Click or KeyDown handler puts <c>WindowImpl.Dispose</c> on the stack while
    /// the compositor is mid-commit. On the Win32 WinUI composition backend the disposing thread then blocks on
    /// the composition lock the compositor holds while it waits for that commit, and the UI thread stops
    /// answering for good — the surface keeps presenting its last frame, so the window still looks alive.</para></summary>
    public static void CloseDeferred(this Window window) =>
        Dispatcher.UIThread.Post(() => window.Close(), DispatcherPriority.Background);

    /// <inheritdoc cref="CloseDeferred(Window)"/>
    public static void CloseDeferred(this Window window, object? result) =>
        Dispatcher.UIThread.Post(() => window.Close(result), DispatcherPriority.Background);

    /// <summary>Closes the window whenever any of the view-model events subscribed by
    /// <paramref name="subscriptions"/> is raised.
    ///
    /// <para>Each takes the handler and attaches it: <c>dlg.CloseWhen(h => vm.Confirmed += h, h => vm.Canceled += h)</c>.
    /// A view-model event is raised from inside a command invocation, which is the same input dispatch a
    /// Click handler runs on, so every one of these closes has to be the deferred one.</para></summary>
    public static void CloseWhen(this Window window, params Action<Action>[] subscriptions)
    {
        foreach (var subscribe in subscriptions) subscribe(window.CloseDeferred);
    }

    /// <summary>The same, for an event that carries a payload. The payload is the caller's to handle in its
    /// own subscription; this one only closes.</summary>
    public static void CloseWhen<T>(this Window window, Action<Action<T>> subscribe) =>
        subscribe(_ => window.CloseDeferred());
}
