using System.Runtime.ExceptionServices;
using System.Text;
using Compass.Interpreter;
using Compass.Runtime;

namespace Mirage.Scripting;

/// <summary>
/// A module the server keeps and calls into, rather than one it runs and throws away.
///
/// <para>A game module declares no entry point. It is a set of handlers the engine calls — when somebody
/// joins, when somebody moves, on every tick — and the state one call leaves behind is what the next one
/// reads. Running the program afresh each time would make that impossible and would pay for the whole
/// front end ten thousand times a minute.</para>
///
/// <para>🔴 <b>The calls run on a thread of this object's own, and the caller waits.</b> That is a baton,
/// not concurrency: exactly one of the two threads runs at a time, and the game thread is stopped for the
/// whole of a call. So a binding reaching world state touches it under the same mutual exclusion it has
/// today, and nothing in the engine has to become thread-safe. What the thread buys is its
/// <b>stack</b>: a Compass call costs about 4 KB of one, the language allows 512 levels of them, and an
/// ordinary thread does not have 2 MB to spare. Running out of real stack is not catchable and ends the
/// process with every player on it.</para>
///
/// <para>🔴 <b>A failure is a value, never an exception out of here.</b> One game's mistake must not end
/// the process every other player is connected to, and a handler that throws on every tick must not
/// become a stack trace per tick in the operator's log.</para>
/// </summary>
public sealed class LoadedScript : IDisposable
{
    /// <summary>
    /// How much stack the script thread gets.
    ///
    /// <para>512 levels at the measured ~4.3 KB a call is about 2.2 MB. This is three to four times that,
    /// and the reasons for the headroom are: the measurement is one build on one platform and its own
    /// author called it a ceiling rather than a constant; the engine's own binding frames sit on top of
    /// the language's and are not in that number; and a deeply nested expression peaks above the depth it
    /// happens at. It costs nothing while unused — a thread's stack is reserved address space, committed
    /// page by page — and the failure it prevents is an uncatchable one.</para>
    /// </summary>
    private const int StackBytes = 8 * 1024 * 1024;

    private readonly SemaphoreSlim _posted = new(0, 1);
    private readonly SemaphoreSlim _finished = new(0, 1);
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private readonly StringBuilder _printed = new();
    private readonly ScriptLimits _limits;

    private Func<object?>? _work;
    private object? _result;
    private ExceptionDispatchInfo? _failure;
    private volatile bool _stopping;
    private LoadedProgram? _program;

    private LoadedScript(CompassScript script, ScriptLimits limits)
    {
        Name = script.Name;
        _limits = limits;

        _thread = new Thread(Serve, StackBytes)
        {
            IsBackground = true,
            Name = $"compass:{script.Name}",
        };
        _thread.Start();

        // Loading runs every shared field's initializer, so it belongs on the thread the calls will come
        // in on — the state it writes is the state they read.
        Hand(() =>
        {
            _program = LoadedProgram.Load(
                script.Lowered,
                script.Model,
                new StringWriter(_printed),
                // Reading input is not a thing a script can do. The reader is empty and stays empty, so a
                // script asking for a line gets end-of-input rather than stopping the server on a console
                // nobody is typing at — the default is Console.In, which is why this is written out.
                new StringReader(string.Empty),
                limits.MaximumDepth);

            return null;
        });
    }

    /// <summary>What to call this module when something in it goes wrong.</summary>
    public string Name { get; }

    /// <summary>The shape of every model this script declares. <inheritdoc cref="CompassScript.Models"/></summary>
    public IReadOnlyList<ScriptModelInfo> Models { get; private set; } = [];

    /// <summary>
    /// Loads a checked module and holds it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The module failed while initializing, which is a module that cannot be run at all rather than one
    /// handler misbehaving.
    /// </exception>
    public static LoadedScript Load(CompassScript script, ScriptLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        return new LoadedScript(script, limits ?? ScriptLimits.Default) { Models = script.Models };
    }

    /// <summary>
    /// Whether the module offers a handler of this name and arity. A module is free not to write one, and
    /// asking costs nothing, so the engine asks rather than calling and catching.
    /// </summary>
    public bool Offers(string model, string function, int arity)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(function);

        return (bool)Hand(() => Program.Offers(model, function, arity))!;
    }

    /// <summary>
    /// Calls a handler, bounded by this module's limits, and says what happened.
    ///
    /// <para>Arguments and the value handed back are in the shapes <see cref="ScriptType"/> names, and an
    /// engine value registered through <see cref="ScriptCatalog"/> passes as itself.</para>
    /// </summary>
    public ScriptOutcome Call(string model, string function, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(arguments);

        lock (_gate)
        {
            _printed.Clear();
        }

        try
        {
            object? value = Hand(() =>
            {
                using CancellationTokenSource? stopwatch =
                    _limits.Timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(_limits.Timeout);

                return Program.Call(
                    model,
                    function,
                    new CallLimits(stopwatch?.Token ?? default, _limits.MaximumBytes),
                    arguments);
            });

            return new ScriptOutcome(value, Printed(), Fault: null);
        }
        catch (Exception raised)
        {
            return new ScriptOutcome(Value: null, Printed(), Describe(raised));
        }
    }

    /// <summary>
    /// Stops the script thread.
    ///
    /// <para>A thread running away inside a call cannot be stopped by asking, so this waits briefly and
    /// then leaves it: the thread is a background one, so it cannot hold the process open. A module
    /// capable of that has already been reported by its own call's limits.</para>
    /// </summary>
    public void Dispose()
    {
        _stopping = true;
        _posted.Release();
        _thread.Join(TimeSpan.FromSeconds(1));

        _posted.Dispose();
        _finished.Dispose();
    }

    private LoadedProgram Program =>
        _program ?? throw new InvalidOperationException($"'{Name}' is not loaded.");

    private string Printed()
    {
        lock (_gate)
        {
            return _printed.ToString();
        }
    }

    private static ScriptFault Describe(Exception raised) => raised switch
    {
        ScriptStoppedException stopped => new ScriptFault(
            ScriptFaultKind.RanTooLong,
            "The handler was still running when its time was up.",
            stopped.File, stopped.Line, stopped.Column),

        ScriptAllocatedTooMuchException greedy => new ScriptFault(
            ScriptFaultKind.AllocatedTooMuch,
            $"The handler allocated more than {greedy.Limit} bytes.",
            greedy.File, greedy.Line, greedy.Column),

        // The language raises its own failures and hands them over wrapped, with the position on the
        // outside and the original inside. Recursion is worth telling apart because the remedy is the
        // script author's and is different from every other kind.
        ScriptFailedException { InnerException: RecursionTooDeepException } deep => new ScriptFault(
            ScriptFaultKind.WentTooDeep,
            "The handler called itself further than the language allows.",
            deep.File, deep.Line, deep.Column),

        ScriptFailedException failed => new ScriptFault(
            ScriptFaultKind.Threw, $"{failed.TypeName}: {failed.Text}", failed.File, failed.Line, failed.Column),

        // The engine's own binding threw. That is a fault on this side of the boundary, and saying so is
        // what stops an operator going looking through somebody's script for a bug in the server.
        ExternalFailureException engine => new ScriptFault(
            ScriptFaultKind.EngineFailed, engine.Message, File: string.Empty, 0, 0),

        MissingFunctionException missing => new ScriptFault(
            ScriptFaultKind.NoSuchHandler, missing.Message, File: string.Empty, 0, 0),

        _ => new ScriptFault(ScriptFaultKind.Threw, raised.Message, File: string.Empty, 0, 0),
    };

    // One slot, one waiting caller. LoadedProgram is not safe to call re-entrantly or from two threads,
    // so the lock is what makes a second caller wait rather than corrupt the program's state in silence.
    private object? Hand(Func<object?> work)
    {
        lock (_gate)
        {
            _work = work;
            _posted.Release();
            _finished.Wait();

            ExceptionDispatchInfo? failure = _failure;
            _failure = null;

            failure?.Throw();

            return _result;
        }
    }

    private void Serve()
    {
        while (true)
        {
            _posted.Wait();
            if (_stopping) return;

            try
            {
                _result = _work!();
                _failure = null;
            }
            catch (Exception raised)
            {
                _result = null;
                _failure = ExceptionDispatchInfo.Capture(raised);
            }

            _finished.Release();
        }
    }
}

/// <summary>
/// What bounds one call into a module.
///
/// <para>Three, because there are three ways a handler can fail to hand the server back its thread: it
/// runs forever, it grows forever, or it calls itself forever. Nothing here bounds one long statement —
/// a single operation over something enormous finishes first — so what these stop is a handler that
/// would never return, not every handler that is slow.</para>
/// </summary>
/// <param name="Timeout">
/// How long one call may run. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for no limit,
/// which is for a tool rather than for a server.
/// </param>
/// <param name="MaximumBytes">
/// How much one call may allocate, or zero for no ceiling. Counted on the script thread from the start of
/// the call, so what the engine's own bindings allocate while the script has them running counts too.
/// </param>
/// <param name="MaximumDepth">How deep a call may go before the language stops it.</param>
public readonly record struct ScriptLimits(TimeSpan Timeout, long MaximumBytes, int MaximumDepth)
{
    /// <summary>
    /// What a server runs somebody else's module under.
    ///
    /// <para>A quarter of a second is not a budget — a handler taking that long every tick is already a
    /// problem the operator will see — it is the point past which the handler is not going to finish at
    /// all. Sixteen megabytes is the same kind of number: far more than a handler wants, and far less
    /// than a loop appending to a set forever.</para>
    /// </summary>
    public static ScriptLimits Default { get; } = new(TimeSpan.FromMilliseconds(250), 16 * 1024 * 1024, 512);

    /// <summary>Nothing bounded, for a test or a tool where the caller is the author.</summary>
    public static ScriptLimits None { get; } = new(System.Threading.Timeout.InfiniteTimeSpan, 0, 512);
}

/// <summary>What one call into a module produced.</summary>
/// <param name="Value">What the handler yielded, or null where it yields nothing or failed.</param>
/// <param name="Output">Everything it printed during this call, in order.</param>
/// <param name="Fault">Why it stopped early, or null where it ran to the end.</param>
public sealed record ScriptOutcome(object? Value, string Output, ScriptFault? Fault)
{
    /// <summary>True when the handler ran to the end.</summary>
    public bool Completed => Fault is null;
}

/// <summary>Which side of the boundary went wrong, and in what way.</summary>
public enum ScriptFaultKind : byte
{
    /// <summary>The script raised something and nothing in it caught the failure.</summary>
    Threw = 0,

    /// <summary>It was still running when its time was up.</summary>
    RanTooLong = 1,

    /// <summary>It allocated past its ceiling.</summary>
    AllocatedTooMuch = 2,

    /// <summary>It called itself further than the language allows.</summary>
    WentTooDeep = 3,

    /// <summary>A member the ENGINE registered threw. This one is the engine's bug, not the script's.</summary>
    EngineFailed = 4,

    /// <summary>The engine asked for a handler the module does not declare.</summary>
    NoSuchHandler = 5,
}

/// <summary>
/// One failed call, as data.
///
/// <para>Carries where it happened because the person reading this is reading a log on a server: they
/// have no editor open on the module and no way to find the line from the message alone.</para>
/// </summary>
public sealed record ScriptFault(ScriptFaultKind Kind, string Message, string File, int Line, int Column)
{
    /// <summary>One line, in the shape every compiler writes: where, then what.</summary>
    public override string ToString() =>
        File.Length == 0 ? $"{Kind}: {Message}" : $"{File}({Line},{Column}): {Kind}: {Message}";
}
