using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Naming a world before it exists.
///
/// <para>A world is created rather than stumbled into: this asks what to call it, and the folder it goes
/// in is asked for next. What makes the folder a world is the <c>world.json</c> written at the end — so
/// creating one is a deliberate act with a name attached, not the side effect of opening somewhere empty
/// and saving.</para>
///
/// <para>The name may be left blank, and the box says what the world will be called if it is.</para>
/// </summary>
public sealed partial class NewWorldDialogViewModel : ObservableObject
{
    /// <summary>Raised with the chosen name — possibly empty, which is a real answer.</summary>
    public event Action<string>? Confirmed;

    public event Action? Canceled;

    [ObservableProperty] private string _worldName = "";

    /// <summary>What the world will be called if the name is left empty. Shown in the box rather than
    /// filled into it: a placeholder that becomes a value the moment somebody types beside it is a name
    /// nobody chose.</summary>
    public string UntitledPlaceholder => EditorStrings.Get(EditorStrings.World_Untitled);

    public string Header => EditorStrings.Get(EditorStrings.NewWorld_Header);
    public string Explanation => EditorStrings.Get(EditorStrings.NewWorld_Explanation);
    public string NameLabel => EditorStrings.Get(EditorStrings.NewWorld_NameLabel);
    public string ConfirmLabel => EditorStrings.Get(EditorStrings.NewWorld_ChooseFolder);
    public string CancelLabel => EditorStrings.Get(EditorStrings.Common_Cancel);

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke(WorldName.Trim());

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
