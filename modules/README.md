# Modules

A module is a game. Core is the engine it runs on, and it knows nothing about any of them.

Everything a game is — its records, its values, its rules, what the player sees — is declared through
`Mirage.Shared.Extensibility`. Nothing in Core names a module, and no file in Core is edited to make room
for one.

[Building a game on Core](../docs/building-on-core.md) is the playbook: what the engine already does,
the thirty-four seams, and the features that fail silently when only half of one is declared. This file
covers how a module is laid out and shipped.

## Two ways to extend the engine

|  | Scripting | A C# module |
|---|---|---|
| **What you write** | a Compass script and a folder of records | an assembly against `Mirage.Shared.Extensibility` |
| **To change the game** | edit the script, restart the server | edit the source, rebuild, redeploy |
| **Toolchain needed** | none | the .NET SDK |
| **Touches Core's source** | no | no, but you build from it |
| **Status** | works today; the demo world's `scripts/` folder is the worked example | works today, and `survey/` is the worked example |

**Scripting is the point.** An engine whose every game needs a compiler is an engine only its own authors
can extend: a designer cannot try an idea, an operator cannot run a variant, and nothing ships without a
build. A script and a folder of records need none of that — the same file can be read, edited, diffed and
dropped on a server that is already running.

**A C# module is the source-manipulating route**, and it is a legitimate one. Somebody forking this
repository to build their own game gets the strongest possible version of it: the compiler checks every
declaration, the debugger steps through their rules, and the seam is the same one a script will use. That
is what `survey/` demonstrates, and why it is kept rather than treated as scaffolding.

The two are not in competition, because they meet at the same place: both produce a `CoreRegistry`, and
[`SurveyModuleTests`](../tests/src/Mirage.Server.Tests/Modules/SurveyModuleTests.cs) asserts that registry
field by field. "The script says the same thing the C# said" is therefore a test rather than a hope.

## What is here

| Module | What it is | Seams it uses |
|---|---|---|
| [`foraging/`](foraging/) | Picking things. About fifty lines, written twice. | six seams |
| [`survey/`](survey/) | Cataloging plants. Deliberately not an RPG. | all thirty-four seams |
| [`msr/`](msr/) | Mirage Source Remastered, ported. An RPG, in Compass, in progress. | the combat surface |

## Turning a game off

One line. `GameModules.Load` in `server/src/Mirage.Server.Host/` lists what loads:

```csharp
public static IReadOnlyList<ICoreModule> Load(string worldDir) => [];
```

That is the engine by itself: a server that runs, accepts players, and moves them around a world with no
game rules in it at all. A supported configuration, and the one to start a new game from. The project
reference may stay — an assembly nothing constructs costs nothing at runtime.

## Layout

```
modules/
  <name>/
    README.md                       what it is, and what it demonstrates
    src/Mirage.Modules.<Name>/      the assembly — references Mirage.Shared and nothing else
    world/                          records it ships, laid out as a world folder
```

**A module references `Mirage.Shared` alone.** Not the server it runs inside, not the client that draws
it, not the editor that authors its records. A module that grows a second reference has found a gap in the
seam — report that rather than working around it.

The shape is chosen so a script costs nothing to move to: `src/` becomes the script, `world/` stays
exactly as it is, and the README keeps saying what the module is for.

## Distribution

Three questions, and they have different answers.

**The client needs nothing.** This is the part worth saying twice, and it is true on both routes.
Everything a module declares reaches the client as *data* on join — the attribute numbering, the equipment
slots, the overhead bars, the display fields. A stock client renders a game it has never heard of, and
nothing is deployed alongside it.

That includes what the player DOES, not only what they read. A game declares an action — an id, a caption,
and where it is offered — and a stock client puts it in the menu and sends the id back when it is picked.
The rule behind the verb runs on the server, so nothing about the behavior crosses the wire and nothing
is deployed beside the client.

**What a module still cannot do is bring its own screen.** A panel of its own layout, a control Core has
no name for: those are code, and a seam that shipped code to every player would be a different and much
worse bargain. A game that needs one builds its own client.

What a module CAN bring is a screen assembled from what Core already draws — a title, a display
surface, and the verbs under it — through `AddPanel`. That crosses the wire as data like everything
else, so a stock client paints it. A script cannot declare one yet, and that is the next thing the
scripting host answers.

**The server** loads C# modules at compile time, so deploying one means deploying a server built with it.
There is no runtime assembly loading and none is planned: it would make a missing module look like a
feature that silently did nothing, and failing to load a game should stop a server rather than start a
quiet one. A script has no such problem, because a script is not code the runtime has to trust — it is
content the host reads.

**Records** ship in the module's own `world/` folder, laid out exactly as a world folder is. Installing
them is a copy into the server's world directory. Deliberately a copy rather than a build step: the
running world is authored content that an operator and the editor both write to, and a build that
overwrote it would throw away their work every rebuild.
