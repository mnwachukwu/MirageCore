# Scripting

A game on this engine is meant to be a **script and a folder of records** — edit, restart the server,
done. No toolchain, no rebuild, no client to redeploy.

The language is [Compass](https://github.com/mnwachukwu/Compass): a small, statically typed language
that compiles to CIL and runs on .NET, with a type checker, definite-assignment analysis, optionals
instead of null, and a VS Code extension that gives a `.cm` file diagnostics as you type.

**What works today:** a module — a folder of Compass source, however many files — is checked and run
inside the server process, with its mistakes returned as data and its output captured.

**What does not yet:** a script cannot call the engine. Everything below the line "What a script can say"
is the open half.

---

## The checkout beside this one

`Mirage.Scripting` is the only project here that needs another repository present. Compass publishes no
NuGet package, so it is consumed the way this fleet consumes shared code — a `ProjectReference` with a
counted `..` to a **sibling checkout**:

```
D:\Repos\
  Compass\
  MirageSourceRemasteredCore\
```

Clone Compass beside this repository and everything builds. Without it, `Mirage.Scripting` and the whole
solution fail to restore; the satellite `.slnx` files for `server/`, `client/` and `editor/` do not
include it and are unaffected.

## What the host does

`ScriptCompiler` drives Compass's own public front end — parse, check, lower — and hands the checked
program to the interpreter. There is no fork of the compiler and no copy of its internals, and the
reference list is the same three projects Compass's other embedder uses: the compiler, the interpreter,
and the runtime they share.

```csharp
var (script, problems) = ScriptCompiler.CompileFolder(@"D:\games\isles\scripts");
if (script is null) foreach (var p in problems) log.Error("{Problem}", p);
else log.Information("{Output}", script.Run().Output);
```

## What a module is made of

**A module is a folder, and every `.cm` under it belongs to it** — recursively, with no manifest.

That decision is this engine's, not the compiler's. Compass is handed a set of sources and has no
opinion about where they came from; its own command-line tool keeps the question in its driver for the
same reason. A build tool is right to make an author list their sources, because what a release contains
should be readable off one file. A game module is the opposite case: it is authored by somebody
arranging their own folder, it ships as that folder, and asking them to maintain a second list of files
they can already see is the bookkeeping this engine exists to remove. Records already work this way —
drop one in the world folder and it is there.

The sources are checked **together**, so a model declared in one file is reachable from another and
Compass's namespaces, imports and qualified names all work across a module the way they do in any other
Compass program.

### Reading is a delegate

`ScriptModule.Read` takes a lister and a reader, defaulting to the disk. That is load-bearing rather
than tidy: a world folder is the thing somebody zips up and hands to another machine, and wiring this to
`File.ReadAllText` would tie a game's rules to loose files on a disk forever. Behind the delegate, the
same module loads out of an archive, a database, or an editor holding something not yet saved.

```csharp
var sources = ScriptModule.Read("pack://isles", list: pack.Names, read: pack.Text);
var (script, problems) = ScriptCompiler.Compile(sources, "isles");
```

### What about `.cmp`?

Compass has project files, which name sources, references to other projects, an entry point and an
output folder. This engine reads none of that, and the reasons are worth stating because the question
comes up:

- **Most of a `.cmp` is build-tool policy a game has no use for.** Nothing here publishes, so `output`
  is meaningless; the engine decides what a module's entry point is, not the author.
- **What an author actually wanted from it — folders, imports, namespaces — are language features**, and
  they work already. They come from checking the sources together, not from the project file.
- **A `.cmp` reader here would be a second reader of somebody else's format.** The first sign of it
  drifting would be a module that builds in VS Code and not on the server.

Compass's own reader lives in its command-line driver, which is an executable; referencing that to reach
two types would drag the language server and debug adapter along with it.

The case that would change this is an author who keeps a real `.cmp` project — because they work in the
editor and their game references a shared library of their own — and wants to drop that folder into a
world verbatim. The answer then is not to read `.cmp` here, nor to move those types into the compiler
where they do not belong, but to ask Compass for a small workspace library between the compiler and the
driver. Worth doing when there are two consumers for it; today there is one, and it does not need it.

### Four things a server needs that a command-line tool does not

- **Problems come back as values.** The compiler writes to a console by origin; a server has an operator
  reading a log somewhere else and may be compiling on behalf of one connection out of twenty.
- **Nothing runs until everything checks.** A script that fails the front end produces no program at all,
  so a game cannot half-load — and one broken file stops the whole build, not just itself.
- **A script that throws does not take the server.** The failure is a value on the result, so one game's
  mistake cannot end the process every other player is connected to.
- **A script cannot reach the console at either end.** Output is captured; input is at end-of-file
  immediately. The interpreter's default reader is `Console.In`, so a script asking for a line would
  otherwise freeze every player on a console nobody is typing at.

## What a script can say

**This is the open design question, and it is the whole remaining job.**

Compass has no host-binding surface. Its built-ins are a closed catalog — a compile-time enum the type
checker knows, implemented by a switch in the interpreter — with no way for an embedder to register a
function. So "call `world.Say(...)` from a script" is not something the language can do today.

There are two ways forward, and they are genuinely different bets:

**Teach Compass to take host functions.** An embedder registers models and members; the checker sees
them; the interpreter dispatches to a delegate. Scripts then read like ordinary code. The cost is that it
changes Compass — a finished, shipped teaching language with its own CI, samples and editor extension —
to carry a general-purpose FFI it was not built for.

**Keep data travelling, never calls.** A script declares by returning a declaration, and reacts by being
a pure function: the engine calls it with what it needs to know and applies the effects it returns.
Nothing is registered and Compass is untouched. The cost is an effect vocabulary to maintain, and scripts
that describe what should happen rather than making it happen.

The second is what the rest of this engine already does — a module's `Configure` is pure declaration, a
client is sent data and never code, and a panel is a description rather than a program. The first is more
comfortable to write in.

Nothing else is blocked on the answer: the seams a script must reach are the fifteen on `ICoreBuilder`,
and they are built, tested, and — as of the client runs — seen working.

## Related

- [modules/README.md](../modules/README.md) — the two routes to a game, and the C# one that works today
- [building.md](building.md) — output naming, and why the engine's own name is compile-time
