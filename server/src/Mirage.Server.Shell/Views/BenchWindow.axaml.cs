using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mirage.Server.Shell.ViewModels;

namespace Mirage.Server.Shell.Views;

/// <summary>
/// The load benchmark's window.
///
/// <para>A plain <see cref="Window"/>, not the app window the shell itself uses: this is a dialog, and a
/// dialog wants the system's own frame and close behaviour.</para>
/// </summary>
public sealed partial class BenchWindow : Window
{
    public BenchWindow()
    {
        InitializeComponent();
        // Closing while a ramp is live would orphan a server process and a temp folder. Cancelling here
        // lets the run unwind through its own cleanup.
        Closing += (_, _) =>
        {
            if (DataContext is BenchViewModel vm && vm.StopCommand.CanExecute(null)) vm.StopCommand.Execute(null);
        };
    }

    private void CloseDialog(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
