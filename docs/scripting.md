# Scripting

A game on this engine is meant to be a **script and a folder of records** — edit, restart the server,
done. No toolchain, no rebuild, no client to redeploy.

The language is [Compass](https://github.com/mnwachukwu/Compass): a small, statically typed language
that compiles to CIL and runs on .NET, with a type checker, definite-assignment analysis, optionals
instead of null, and a VS Code extension that gives a `.cm` file diagnostics as you type.

**What works today:** a Compass program — one file, a folder of them, or a `.cmp` project — is checked
and run inside the server process, with its mistakes returned as data and its output captured.

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

`ScriptCompiler` drives Compass's own public front end in the order its command-line tool drives it —
parse, check, lower — and hands the checked program to the interpreter. There is no fork of the compiler
and no copy of its internals.

```csharp
var (script, problems) = ScriptCompiler.CompileAt(@"D:\games\isles\Game.cmp");
if (script is null) foreach (var p in problems) log.Error("{Problem}", p);
else log.Information("{Output}", script.Run().Output);
```

`CompileAt` takes one `.cm` file, a folder of them, or a `.cmp` project, and defers to Compass's own
`SourceDiscovery` to decide what a build is made of. That matters: a module worth writing uses folders,
imports and namespaces, and a second reader of somebody else's project format is one that drifts — the
first sign of which would be a module that builds in the editor and not on the server.

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
