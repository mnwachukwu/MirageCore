# Scripting

A game on this engine is meant to be a **script and a folder of records** — edit, restart the server,
done. No toolchain, no rebuild, no client to redeploy.

The language is [Compass](https://github.com/mnwachukwu/Compass): a small, statically typed language
that compiles to CIL and runs on .NET, with a type checker, definite-assignment analysis, optionals
instead of null, and a VS Code extension that gives a `.cm` file diagnostics as you type.

**A world carries its own rules in a `scripts/` folder**, and the server reads them when it starts. They
are checked against the types the engine registers, refused if they can reach past them, loaded once, and
then called into as the game runs. A world whose rules are broken is reported and runs unscripted; a
handler that will not finish is stopped rather than taking the server with it.

---

## The checkout beside this one

`Mirage.Scripting` is the only project here that needs another repository present. Compass publishes no
NuGet package, so it is consumed the way this fleet consumes shared code — a `ProjectReference` with a
counted `..` to a **sibling checkout**:

```
D:\Repos\
  Compass\
  MirageCore\
```

Clone Compass beside this repository and everything builds. Without it, `Mirage.Scripting` and anything
that reaches it fail to restore — and that now includes the **server**, because a world carries its own
rules and the thing that loads a world is the thing that reads them. The client and the editor are
unaffected.

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
var (script, problems) = ScriptCompiler.CompileModule(sources, "isles");
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

## Where a world's rules live

`<world>/scripts/`, every `.cm` under it, checked together. `ScriptedWorldModule` reads it, and every
server loads that module — a world with no scripts folder loads it and it does nothing.

What a module writes is a shared model called `Rules` with public handlers on it. Each is optional: the
engine asks whether one is offered before it calls it, so a world that only cares about movement writes
one function and nothing else.

| Handler | When |
|---|---|
| `Configure(Builder game)` | once, before the world exists, so a module can declare |
| `OnPlayerJoined(Player who)` | a player is in the world and has everything they need |
| `OnPlayerLeft(Player who)` | they have left, while their record is still readable |
| `OnPlayerMoved(Player who, integer fromX, integer fromY)` | every accepted step, seam crossings included |
| `OnAction(Player who, string action, integer map, integer x, integer y)` | the player picked one of this module's own verbs |
| `OnTick()` | every tick |

**[docs/scripting-api.md](scripting-api.md) is the reference**, and it is generated from the catalog the
server registers rather than written: a member cannot exist without appearing in it.

```
shared model Rules
    public function OnPlayerMoved(Player who, integer fromX, integer fromY)
        integer left = who.Number("stamina");

        if left <= 0
            yield;
        end if

        who.SetNumber("stamina", left - 1);

        if left == 1
            who.Message("You are too tired to go much further today.");
        end if
    end function
end model
```

**A world whose rules do not compile still runs.** The problems are logged against it and the game runs
unscripted, because a server that refused to start over a typo in somebody's rules is a server an
operator cannot recover without an editor.

The worked example is [`server/src/Mirage.Server.Host/world/scripts/`](../server/src/Mirage.Server.Host/world/scripts),
which ships loaded — a seam only a test has ever run is a seam nobody has run.

## What a script can say

The engine registers types, and a module names them. `ScriptedWorldModule.Catalog` is what the shipped
server registers:

```csharp
var catalog = ScriptCatalog.Declare(c =>
{
    var player = c.Type("Player");

    player
        .Action("Message", [ScriptType.Text], (who, a) => { world.Tell(Who(who), a.AsText(0)); return null; })
        .Value("X", ScriptType.Integer, (who, _) => (long)world.PlaceOf(Who(who)).X)
        .Function("Number", ScriptType.Integer, [ScriptType.Text], (who, a) => ...);
});
```

A `Player` carries `Say`, `IsHere`, `Map`, `X`, `Y`, `Has`, `Number`, `Text`, `SetNumber`, `SetText`,
`WarpTo`, `Give` and `Take` — everything `IWorld` already offered, named the way a script writes it. What
a game COUNTS lives in the attribute bag, which is why `Number` and `SetNumber` take a key: a C# module
declares what stamina is and the world's own rules decide what spends it.

## What a script can add

`Configure` is handed a `Builder`, which is the same two-phase shape a C# module has and for the same
reason: what a module declares shapes the engine that is then built, so it has to be said before anything
exists to act on.

```
shared model Rules
    public function Configure(Builder game)
        game.Attribute("harvest.baskets", "owner");

        game.Heading("Harvest");
        game.Field("harvest.baskets", "Baskets");

        game.Action("harvest.gather", "Gather here", "Harvest");
    end function

    public function OnAction(Player who, string action, string on, integer map, integer x, integer y,
                             string picked)
        who.SetNumber("harvest.baskets", who.Number("harvest.baskets") + 1);
        who.Message("Gathered. That is " + who.Number("harvest.baskets") + " baskets.");
    end function
end model
```

A stock client then draws a Harvest heading and a Baskets row on the sidebar, and offers "Gather here"
under a Harvest heading in the square menu — having never heard of any of it. A world's own rows and
verbs sit AFTER whatever the compiled game declared, because an ordinal is global to its surface and two
modules both numbering from zero would interleave one's heading into the other's rows.

**An enum cannot cross the boundary**, since a script may name a registered type and nothing else. So a
visibility is a word — `none`, `owner`, `viewport` — and a word that is not one of them is refused with
the list rather than falling back to a default nobody chose.

**A declaration that collides with the compiled game is refused on its own**, named in the log, and the
rest are still made. Two modules claiming one attribute key is an error the engine raises at startup,
which is right when both are assemblies somebody built and wrong when one of them is a folder a stranger
handed over: the operator would be left with a server that will not start and a world they did not
author. What it is not is silent.

**Declaring ends when `Configure` returns.** A script keeping the builder and declaring from a handler is
declaring into an engine that has already been built around it, and is told so.

**`Message` is the one chat path in the engine that carries text rather than a key.** Everything else the
server says is looked up per recipient so it arrives in each player's language; a game's words are not in
that table and cannot be added to it, so they travel as written.

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
