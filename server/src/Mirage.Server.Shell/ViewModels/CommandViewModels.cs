using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mirage.Server.Shell.ViewModels;

public enum ParameterKind { Text, Choice, Number, Paragraph }

/// <summary>One argument of a command. Its kind picks the control, which is also what stops an operator
/// entering something the server would only reject.</summary>
public sealed partial class CommandParameter : ObservableObject
{
    private CommandParameter(string name, ParameterKind kind) { Name = name; Kind = kind; }

    public static CommandParameter Text(string name) => new(name, ParameterKind.Text);

    /// <summary>Multi-line, for values long enough to want the room.</summary>
    public static CommandParameter Paragraph(string name) => new(name, ParameterKind.Paragraph);

    public static CommandParameter Choice(string name, IReadOnlyList<string> options) =>
        new(name, ParameterKind.Choice) { Options = options, Value = options.FirstOrDefault() ?? "" };

    /// <summary>A bounded whole number — map ids and the like.</summary>
    public static CommandParameter Number(string name, decimal min, decimal max, decimal? initial = null) =>
        new(name, ParameterKind.Number) { Minimum = min, Maximum = max, Amount = initial ?? min };

    /// <summary>Shown as the placeholder; matches the name in the command's own usage line.</summary>
    public string Name { get; }
    public ParameterKind Kind { get; }

    public IReadOnlyList<string>? Options { get; private init; }
    public decimal Minimum { get; private init; }
    public decimal Maximum { get; private init; }

    /// <summary>Marks an argument the command cannot run without — a name, an account. Run stays
    /// disabled while it is blank, because posting the line without it asks the server a question it
    /// can only refuse, and the refusal arrives as console text nobody is watching for.</summary>
    public CommandParameter Required() { IsRequired = true; return this; }

    public bool IsRequired { get; private set; }

    /// <summary>Show this argument only while <paramref name="source"/> holds one of
    /// <paramref name="values"/>.
    ///
    /// <para>A command whose arguments depend on a choice — /management, where only "port" takes a
    /// value — otherwise shows a box that does nothing for three of its four actions, and an empty box
    /// beside a control reads as something you forgot to fill in.</para></summary>
    public CommandParameter ShownWhen(CommandParameter source, params string[] values)
    {
        _shownSource = source;
        _shownValues = values;
        // The source drives this one’s visibility, so its changes are this one’s changes too.
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Value)) return;
            OnPropertyChanged(nameof(IsShown));
            NotifyControlVisibility();
        };
        return this;
    }

    private CommandParameter? _shownSource;
    private string[] _shownValues = [];

    /// <summary>Whether this argument applies at all right now.</summary>
    public bool IsShown => _shownSource is null || _shownValues.Contains(_shownSource.Value);

    /// <summary>Blank, and the command needs it. Only counts while the argument is shown — an
    /// argument that does not apply cannot be missing.</summary>
    public bool IsMissing => IsShown && IsRequired && AsArgument().Length == 0;

    private void NotifyControlVisibility()
    {
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(ShowChoice));
        OnPropertyChanged(nameof(ShowNumber));
        OnPropertyChanged(nameof(ShowParagraph));
    }

    public bool IsText => Kind == ParameterKind.Text;
    public bool IsChoice => Kind == ParameterKind.Choice;
    public bool IsNumber => Kind == ParameterKind.Number;
    public bool IsParagraph => Kind == ParameterKind.Paragraph;

    // What the view binds: the kind picks the control, IsShown decides whether it is there at all.
    public bool ShowText => IsText && IsShown;
    public bool ShowChoice => IsChoice && IsShown;
    public bool ShowNumber => IsNumber && IsShown;
    public bool ShowParagraph => IsParagraph && IsShown;

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    /// <summary>The numeric face of <see cref="Value"/>, for the spinner to bind to.</summary>
    public decimal? Amount
    {
        get => decimal.TryParse(Value, out var d) ? d : null;
        set { Value = value?.ToString() ?? ""; OnPropertyChanged(); }
    }

    /// <summary>The value as it goes on the wire. A paragraph is flattened to one line: the console reads
    /// commands with ReadLine, so an embedded newline would arrive as a SECOND command — and the MOTD is
    /// broadcast to players, whose sprite font cannot draw one anyway.</summary>
    public string AsArgument() =>
        IsParagraph ? string.Join(' ', Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    : Value.Trim();
}

/// <summary>A command and its arguments. Composes the same text an operator would type and hands it to
/// the console, so there is one execution path and the CLI stays the source of truth.</summary>
public sealed partial class ShellCommand : ObservableObject
{
    private readonly Action<string> _send;

    public ShellCommand(string verb, string description, Action<string> send, params CommandParameter[] parameters)
    {
        Verb = verb;
        Description = description;
        _send = send;
        Parameters = new ObservableCollection<CommandParameter>(parameters);

        // Run is disabled while a required argument is blank, so it has to re-ask as the operator
        // types. Disabling beats refusing on click: a button that does nothing when pressed is
        // indistinguishable from one that failed.
        foreach (var p in Parameters)
            p.PropertyChanged += (_, _) => RunCommand.NotifyCanExecuteChanged();
    }

    public string Verb { get; }
    public string Description { get; }
    public ObservableCollection<CommandParameter> Parameters { get; }
    public bool HasParameters => Parameters.Count > 0;

    /// <summary>True for commands worth a second look before they fire.</summary>
    public bool NeedsConfirmation { get; init; }

    [ObservableProperty]
    public partial bool IsConfirming { get; private set; }

    /// <summary>Nothing required is blank. What the operator sees is a greyed Run rather than a line
    /// posted for the server to reject into a console nobody is reading.</summary>
    public bool CanRun => !Parameters.Any(p => p.IsMissing);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        if (NeedsConfirmation && !IsConfirming) { IsConfirming = true; return; }
        IsConfirming = false;

        // No quoting: the console splits on the first space only, so quoting would make the GUI and a
        // hand-typed command parse differently.
        var text = Verb;
        foreach (var p in Parameters)
        {
            // An argument that does not apply is not sent, whatever is still typed in it: switching
            // /management from "port" to "off" must not carry the port number along.
            if (!p.IsShown) continue;
            string arg = p.AsArgument();
            if (arg.Length > 0) text += " " + arg;
        }
        _send(text);
    }

    [RelayCommand]
    private void CancelConfirm() => IsConfirming = false;
}

/// <summary>Commands grouped by what they act on, which is how an operator looks for one — "the thing
/// that changes the weather", not "the Developer-tier commands".</summary>
public sealed class CommandGroup(string title, IReadOnlyList<ShellCommand> commands)
{
    public string Title { get; } = title;
    public IReadOnlyList<ShellCommand> Commands { get; } = commands;
}
