# Scripting

A game on this engine is meant to be a **script and a folder of records** — edit, restart the server,
done. No toolchain, no rebuild, no client to redeploy.

The language is [Compass](https://github.com/mnwachukwu/Compass): a small, statically typed language
that compiles to CIL and runs on .NET, with a type checker, definite-assignment analysis, optionals
instead of null, and a VS Code extension that gives a `.cm` file diagnostics as you type.

A module — a folder of Compass source, however many files — is checked against the types the engine
registers, refused if it can reach past them, loaded once, and then called into as often as the game
likes. Its mistakes come back as data, its output is captured, and a handler that will not finish is
stopped rather than taking the server with it.

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

`ScriptCompiler` drives Compass's own public front end — parse, check, refuse, lower, convert — and hands
the checked program to the interpreter. There is no fork of the compiler and no copy of its internals,
and the reference list is the same three projects Compass's other embedder uses: the compiler, the
interpreter, and the runtime they share.

```csharp
var (module, problems) = ScriptCompiler.CompileFolder(@"D:\games\isles\scripts", catalog);
if (module is null) { foreach (var p in problems) log.Error("{Problem}", p); return; }

using var rules = LoadedScript.Load(module);
if (rules.Offers("Rules", "OnTick", 0)) rules.Call("Rules", "OnTick");
```

**`Mirage.Scripting` is the only project that names a Compass type.** A seam declares that a member
yields a number in this engine's own vocabulary, and nothing about the language reaches the rest of the
tree — which is what stops the day Compass changes shape from being a day the whole engine is rewritten.

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

The engine registers types, and a module names them:

```csharp
var catalog = ScriptCatalog.Declare(c =>
{
    var player = c.Type("Player");
    var world = c.Shared("World");

    player
        .Value("Name", ScriptType.Text, (who, _) => NameOf(who))
        .Action("Say", [ScriptType.Text], (who, args) => { Tell(who, args.AsText(0)); return null; });

    world.Function("PlayerNamed", player.AsType.OrNothing(), [ScriptType.Text],
                   (_, args) => Find(args.AsText(0)));
});
```

```
shared model Rules
    public function OnPlayerMoved(Player who, integer fromX, integer fromY)
        who.Say("You were at " + fromX + ", " + fromY + ".");
    end function
end model
```

A registered type is **opaque**: a script holds one, asks it questions, and can never declare, extend, or
construct one. That is what lets the engine change what a player is without breaking every world built on
it.

**A catalog is built once and shared.** Building a second from the same declarations gives the same name
two symbol identities, and a `Player` from one will not fit a `Player` from the other — reported as a
type error on code that is plainly correct. `ScriptCatalog.Declare` is the only way in and settles the
names before any signature mentions one, so the mistake cannot be made from outside that project.

### What a module may not reach

A world folder is something one person hands to another, and its scripts run inside the server process as
the server's user. So a module is refused if it uses any part of the language that reaches past the
program — the filesystem, the machine clock — and refused at **compile** time, because .NET offers no way
to take a capability away from code already running. The author finds out at deploy rather than mid-game.

**The rule is derived from the language, never transcribed from it.** Compass classifies each of its own
members by what it touches and adds them up over a checked program; `ScriptSandbox` refuses anything that
adds up to more than nothing. A hand-written list here of the members that reach outside would keep
passing while it stopped meaning anything, the moment the language grew one more — and that failure has
no symptom: the check is green, the module loads, and the thing it was guarding against is allowed.

What the engine itself registers is not counted, because the engine knows what its own bindings do. The
catalog is the other half of the sandbox: a script reaches exactly what was registered and nothing else.

⚠ Where this stops: it defends a trusted server from a careless or opportunistic module. A genuinely
hostile one is an operating-system problem — a separate process with restrictions — and that costs the
in-process embedding and the cheap handles this layer exists to provide.

### Loaded once, called many times

A game module declares no entry point. It is a set of handlers, and the state one call leaves behind is
what the next one reads, so `LoadedScript` initializes the program once and keeps it.

**Calls run on a thread of its own, and the caller waits.** That is a baton rather than concurrency:
exactly one of the two threads runs at a time, so a binding reaching world state touches it under the
same mutual exclusion it always had and nothing in the engine has to become thread-safe. What the thread
buys is its **stack** — a Compass call costs about 4 KB, the language allows 512 levels of them, and an
ordinary thread does not have 2 MB to spare. Running out of real stack is not catchable and ends the
process with every player on it.

**Three ways a handler can fail to hand the thread back**, and each is bounded: it runs forever, it grows
forever, or it calls itself forever. `ScriptLimits` carries a time, an allocation ceiling and a depth,
and the failure arrives as a `ScriptFault` naming the file and line — not as an exception through the
game loop. What none of them bounds is one long statement, which has no back edge to be noticed at.

### Two rules a module author cannot guess

- **A handler has to be `public`.** A function only the host calls is a function nothing in the module
  calls, which is dead code as far as the compiler can see, so an ordinary `OnTick` is reported as
  unused.
- **Nothing a script prints reaches the server's console, and nothing it reads comes from one.** Output
  is captured per call; input is at end-of-file immediately.

## Related

- [modules/README.md](../modules/README.md) — the two routes to a game, and the C# one that works today
- [building.md](building.md) — output naming, and why the engine's own name is compile-time
