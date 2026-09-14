# Implementing a game on Core

**Two routes, one engine, and neither is the poor cousin.** This page is for choosing between them.

- **[Scripting a game](scripting-a-game.md)** — a folder of Compass files inside a world. No compiler,
  no SDK, no rebuild.
- **[Building on Core](building-on-core.md)** — a C# assembly against `Mirage.Shared.Extensibility`,
  compiled and listed in `GameModules.Load()`.

Both declare through the same seams and produce the same registry. Survey — the game this repository
ships — exists twice, once each way, and one set of assertions is held against both. That is not a
demonstration; it is how "either route, same game" stays true as the engine changes.

---

## The short answer

| If … | Then |
|---|---|
| you want to hand somebody a folder and have them play it | **script** |
| you are building something the size of a real MMORPG | **C#** |
| you want to change a rule on a live server tonight | **script** |
| you need a message of your own on the wire | **C#** |
| you have never installed the .NET SDK | **script** |
| you want one language for the rules, the types, and the records | **C#** |
| you are not sure | **script** — you can move later, and moving is easier in that direction |

## Side by side

|  | Scripting | C# module |
|---|---|---|
| **What you write** | `.cm` files under `world/scripts/` | an assembly against `Mirage.Shared.Extensibility` |
| **To change a rule** | edit, restart the server | edit, rebuild, redeploy |
| **Toolchain** | none | the .NET SDK |
| **Shipping the game** | copy the world folder | ship a build |
| **Errors found** | as you type, by the language server | as you type, by the language service |
| **A mistake the checker cannot see** | refused at server start, named, and the rest still declares | there is no server start to reach |
| **Debugger** | breakpoints, stepping, call stack, variables | breakpoints, stepping, call stack, variables |
| **The engine's own types in the editor** | **not yet** — see below | yes |
| **Records of your own** | yes | yes |
| **Panels, verbs, conditions, keys** | yes | yes |
| **Attributes, bars, sidebar rows** | yes | yes |
| **Death and linger policies** | yes | yes |
| **Equip slots** | yes | yes |
| **A message of its own** | yes — `game.Message` | yes |
| **Arbitrary .NET in a handler** | no — the sandbox refuses the filesystem and the clock | yes |
| **Performance ceiling** | an interpreter, called across a boundary | native, in-process |

## What each is actually good at

### Scripting

**Its real advantage is the loop.** Edit a file, restart, play. No build, no redeploy, no artifact to
move. For the first weeks of a game — when most of what you write is wrong and you find out by playing
it — that loop is worth more than everything on the other column.

**And the game travels as content.** The world folder *is* the game. Somebody who has never seen this
repository can be handed one and run it, and the operator running a server can read exactly what the
rules do, because the rules are a text file in the world they are serving.

**The sandbox is a feature, not a limitation.** A world folder is something one person hands to another.
A module that could open a file could read the accounts beside it, so it cannot.

⚠ **The one thing genuinely missing today is not a language problem.** The engine's registered types
— `Builder`, `Player`, `Records`, `Verb`, `Panel` — exist only inside the server, handed to the compiler
by `ScriptCatalog` while the world loads. A standalone `cm check` has never heard of them, so opening a
Mirage world script in VS Code reports *"There is no type named 'Builder'"* on every declaring line.
Everything else resolves correctly, cross-file references included. Exporting the catalog so the tooling
can read it is work on this repository's side, and until it is done, **the editor is right about your
own code and wrong about the engine's**.

### A C# module

**Its real advantage is that everything is one language.** A rule, the type it reads, and the record it
writes are all C#, checked together, refactored together, and stepped through in one debugger session.
On the scripted route the boundary is real: the engine is on one side and your rules on the other, and
the things that go wrong go wrong at the seam.

🔴 **The seam's own failure has no compiler on either side.** A handler is matched by NAME AND ARITY, so
a `Rules` function one parameter off is not an error — it is a function nobody calls. It compiles, the
language server is happy, the server loads it, and the rule quietly never runs. In C# the same mistake
is an interface not implemented. This is the difference worth knowing about, and it is why
`ScriptedWorldModule.Offered` is exposed and why a test holds what Survey wrote against what the engine
took.

**Both routes can own a message on the wire.** A script declares one from a model —
`game.Message("Note")` — and the engine registers a parse that reads the line into the shapes that
model named. No type is compiled, because `PacketRegistry.Register` takes a parse delegate and
`Register<T>` is only the convenience overload for a shape somebody did compile.

⚠ **What neither route gives you is a STOCK CLIENT that can send one.** The only thing a stock client
originates for a game is a verb — an action id and the square or body it was used on, carrying no
values. A message is for a client, a tool, or a bot that knows it, and that is equally true of a
compiled module's packet. The difference between the routes here is narrower than it looks: C# gets a
TYPED packet, checked by the compiler on both ends when the client is built from the same source.

**It scales further.** For a game with hundreds of rules, thousands of records, and work on every tick,
native code in-process is the one that does not have to be thought about.

## Mixing them

**You can do both, and the engine is built for it.** `GameModules.Load()` takes a list, and the
`ScriptedWorldModule` is last in it — so a world's own rules are told about things after the compiled
game has had its say.

The useful shape is a compiled module that declares the **machinery** — the records, the attributes, the
packets, the tick work — and a script that decides the **numbers and the words**: what a step costs,
what a rank is called, what happens when somebody uses a verb. Then the parts that need a compiler have
one, and the parts an operator wants to change tonight are a text file.

⚠ **Two modules cannot declare the same thing.** Two claims on one attribute key, one family id, or one
packet command stop the server at startup with the second one named — which is the engine working. That
is why Survey ships as a script *or* as a module and never both at once: it is one game, and loading it
twice is it colliding with itself. See
[`modules/survey/README.md`](../modules/survey/README.md) for the three steps to swap which one runs.

## Moving from one to the other

**Script to C#** is the easy direction and does not need rewriting. Everything a script declares has a
one-to-one C# counterpart — a model's `Describe` is `builder.AddFamily(...)`, `game.Action(...)` is
`builder.AddAction(...)` — and the records on disk do not move. Read Survey both ways side by side; the
shapes line up line for line.

**C# to script** is harder only where you used arbitrary .NET in a handler. A typed packet becomes a
model and a `game.Message` line; everything else transfers.

**The records never move either way.** They are authored content, written by the editor, owned by the
world rather than by either module.

## Where to start

1. Read [the Compass playbook](scripting-a-game.md) even if you intend to write C#. It is shorter, and
   the seams it describes are the same seams.
2. Run the server. It ships Survey, scripted, in
   [`server/src/Mirage.Server.Host/world/scripts/`](../server/src/Mirage.Server.Host/world/scripts/).
   Change a number in it, restart, and watch the game change.
3. Read [`modules/survey/`](../modules/survey/) for the same game in C#.
4. Then read [Building on Core](building-on-core.md), which covers the seams in depth and the ones that
   fail silently when only half of one is declared.
