# Mirage Source Remastered — C# Rewrite

[![Build and test](https://github.com/mnwachukwu/MirageSourceRemasteredCore/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/mnwachukwu/MirageSourceRemasteredCore/actions/workflows/ci.yml)

![.NET 10](https://img.shields.io/badge/.NET-10-9aa8f5)
![Windows](https://img.shields.io/badge/Windows-x64-9aa8f5?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-x64-9aa8f5?logo=linux&logoColor=white)
![macOS](https://img.shields.io/badge/macOS-x64-9aa8f5?logo=apple&logoColor=white)

**[Overview](#overview)** · **[Project structure](#project-structure)** · **[Getting started](#getting-started)** · **[Documentation](#documentation)**

## Overview

This is a C# reimplementation (a remastering, if you will) of [Mirage Online v3.0.3](https://github.com/mnwachukwu/mirage-source-v3.0.3) — whose [original site](https://miragesource.net/) is still standing — a 2D tile-based MMORPG engine originally written in Visual Basic 6. The original's world model — tile maps, a seamless grid of them, records authored in an editor — is the foundation; the rules that made it one particular game are a module's, which is what lets a different game be built on the same engine. It's a handwritten .NET 10 codebase built on [MonoGame](https://monogame.net/), [Avalonia](https://avaloniaui.net/), and [Serilog](https://serilog.net/) — no VB6 runtime, no transpilation, no auto-conversion tools. The client's game logic carries no MonoGame dependency, so another shell such as [Godot](https://godotengine.org/) could consume `Mirage.Client.Core` unchanged; MonoGame is the shell shipped here.

I don't know why I did this.

---

## Building a game on it

The engine has no genre. Everything that would make it one particular game — what a character is, what
there is to find, what a value means, what the player reads on the sidebar — is a **module's**, declared
through one interface and loaded by name.

There are two ways to write one, and they meet at the same place.

**A script, with no compiler.** A game is a [Compass](https://github.com/mnwachukwu/Compass) script and a
folder of records: edit it, restart the server, and that is the whole loop. No toolchain, no rebuild, no
client to redeploy. This is the intended way and the reason the seams are shaped as they are: a world
carries its rules in a `scripts/` folder, the server reads them when it starts, and a module that could
reach past what the engine offers is refused before it runs — see [docs/scripting.md](docs/scripting.md).

**A C# module, by building from source.** Fork the repository, write an assembly against
`Mirage.Shared.Extensibility`, and list it in `GameModules.Load()`. The compiler checks every declaration
and the debugger steps through your rules. This works today, and
[`modules/survey/`](modules/README.md) is a complete worked example — a small game about cataloguing
plants, shipped loaded, using every seam the engine offers.

Both produce the same thing: a registry the engine reads once at startup. Neither edits a file in Core.

**A stock client renders a game it has never heard of.** Everything a module declares — the attribute
numbering, equipment slots, overhead bars, sidebar fields — reaches the client as data when a player
joins. Nothing is deployed alongside it.

Deleting the one line in `GameModules.Load()` leaves the engine by itself: a server that runs, accepts
players, and moves them around a world with no game rules in it at all. See
[modules/README.md](modules/README.md).

---

## Project Structure

VB6 kept its source in `client/` and `server/`, with the editor forms — `frmItemEditor`, `frmNpcEditor`,
`frmShopEditor`, `frmSpellEditor` — sitting among the client's. The rewrite has four source folders,
because the two it adds are the parts VB6 had nowhere to put: code both sides must agree on, and the
editor.

| VB6 | C# | |
|---|---|---|
| `server/` | `server/src/` | `Mirage.Server.Core` — game logic, no transport dependency |
| | | `Mirage.Server.Host` — TCP, DI, entry point; runs headless |
| | | `Mirage.Server.Shell` — optional Avalonia front end for the same server |
| `client/` | `client/src/` | `Mirage.Client.Core` — game state and logic, no MonoGame dependency |
| | | `Mirage.Client.Shell` — MonoGame rendering, input, and audio |
| `client/` (editor forms) | `editor/src/` | `Mirage.Editor` — Avalonia editor, offline against a world folder or live against a running server |
| — | `shared/src/` | `Mirage.Shared` — protocol types, records, and the formulas both sides evaluate |
| | | `Mirage.Ui` — the theme and shared controls the two Avalonia apps use |
| | | `Mirage.Updates` — the GitHub update check every app runs |

`Mirage.Shared` is referenced by all three solutions, replacing VB6's duplicated `modTypes.bas` definitions and the server/client divergence they caused. Every formula that both the client and the server must agree on — damage, requirements, prices, vitals — lives there and is evaluated from the same code on both sides.

`server/`, `client/`, and `editor/` each carry a satellite `.slnx` for working on one area alone. The rest
of the tree is not source: `tests/` holds the suites in `src/` and their drivers above, `publish/` holds
the packaging drivers, `modules/` holds the games built on the engine, and `assets/`, `docs/`, `tools/`,
and `.github/checks/` hold what is neither.

The root `Mirage.slnx` ties all 27 projects together, and the split is lopsided on purpose: **eleven of the twenty-seven are the engine and the game on it. The other sixteen exist to test and publish those eleven.**

| | Count | What |
|---|---|---|
| **The game** | 3 | shared libraries — `Mirage.Shared`, `Mirage.Ui`, `Mirage.Updates` |
| | 3 | server — `Mirage.Server.Core`, `.Host`, `.Shell` |
| | 2 | client — `Mirage.Client.Core`, `.Shell` |
| | 1 | editor — `Mirage.Editor` |
| | 1 | the loaded game — `Mirage.Modules.Survey`, in `modules/` |
| | 1 | the scripting host — `Mirage.Scripting`, in `scripting/` |
| **Scaffolding** | 7 | test suites, one per source portion, in `tests/src/` |
| | 5 | test drivers in `tests/` — one per area, plus a root that runs all seven suites |
| | 4 | publish drivers in `publish/` — one per deliverable, plus a root that runs all three |

Only the first eleven compile into anything a player or a developer runs; a fork that never publishes and never runs the seven test suites needs none of the other sixteen.

The last two are the odd ones out and are meant to be. `modules/` holds games built ON the engine rather than part of it. A module references `Mirage.Shared` and nothing else, and the server loads it in one line — see [modules/README.md](modules/README.md). `scripting/` holds the Compass host, which is the only project here that needs a checkout beside this one — see [docs/scripting.md](docs/scripting.md).

The suites split the way the code does: a **core** and a **shell** get separate suites wherever the shell can be swapped. `Mirage.Client.Core` carries no MonoGame and `Mirage.Server.Core` no Avalonia, and neither points back at a shell — the renderer and the management window are both replaceable, and separate suites are what keeps that so. A core suite builds without a shell on its reference path, so logic that reached for one would fail to compile rather than quietly tie the core to one front end.

**The two sides are not named symmetrically.** The client spells out both halves — `Mirage.Client.Core.Tests` and `Mirage.Client.Shell.Tests`. The server names only the shell, `Mirage.Server.Shell.Tests`, and leaves a bare `Mirage.Server.Tests` covering `Mirage.Server.Core` and `Mirage.Server.Host` together. There is no `Mirage.Server.Core.Tests` to find. Everything under `shared/` has its own suite, `Mirage.Shared.Tests`. See [Testing](docs/testing.md).

Some things people expect to find here live **outside** this repository, because they write into it
rather than build with it: the content generators that produced the seed, the scripts that draw the
app icons and control-scheme images, and the converter that imports an old VB6 world. Those are
published separately — see [Authoring content](#authoring-content) below.

The standalone balance simulators are not published. They answer "what would this feel like" against
the shipped formulas, nothing here builds or ships them, and their output is a judgment call that
already lives in the numbers.

Deliberately described rather than enumerated: a previous version of this section named three simulators
by hand and was wrong about all of it within a few months.

---

## Getting Started

**Prerequisite:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.x or later)

```sh
git clone https://github.com/mnwachukwu/MirageSourceRemasteredCore.git
cd MirageSourceRemasteredCore
dotnet tool restore
dotnet tool restore --tool-manifest client/.config/dotnet-tools.json
```

There are two tool manifests, and the second command is not redundant: the root one declares `vpk`
(Velopack, used by the publish targets) and `ilspycmd`, while `client/.config/dotnet-tools.json`
declares `mgcb` (the MonoGame content builder). The client manifest sets `"isRoot": true`, which stops
the upward search, so a plain `dotnet tool restore` at the root never reaches it.

Run the server, then the client (and optionally the editor) in separate terminals:
```sh
dotnet run --project server/src/Mirage.Server.Host
dotnet run --project client/src/Mirage.Client.Shell
dotnet run --project editor/src/Mirage.Editor
```

The editor opens on nothing and says so. A world is a folder, and it edits one wherever it lives — so
either **World → Open World…** and point it at a world of your own or at
`server/src/Mirage.Server.Host/world/`, or connect to a running server and edit that world live. Running
from source there is no bundled copy, so the first Open is yours to aim.

> **Installed, both the server and the editor ship the world as `seed-world/` beside their executable**, and it
> is the same set of files and folders in each — whichever you installed, you already have it, and there is
> no reason to install one to get the other's copy. A much smaller `seed-data/` rides along with the
> defaults an installation starts with rather than a world, which today is the MOTD.
>
> **Neither is shipped as the folder it becomes**, so nothing an installer or an update writes can land on
> top of a world you already have. A first run lays each down only where there is nothing: no `world/` gets
> the shipped world, no `data/` gets the shipped defaults. An **empty** one of either is left exactly as
> found — that is somebody's blank canvas, and refilling it on the next launch is the one thing seeding must
> never do.
>
> So a fresh install starts on the shipped world without being asked, and clearing `world/` and restarting
> gets it back. To start from nothing instead, leave an empty `world/` in place. The editor needs no copy
> at all: it opens a world wherever it lives, and starts its picker at `seed-world/`.

> **Importing VB6 world data:** [MirageSourceRemasteredConverter](https://github.com/mnwachukwu/MirageSourceRemastered.Tools.Public) turns an original VB6 server directory into this JSON format in one pass — all binary `.dat` maps and INI data files, with account passwords hashed on the way through and the source files never modified, so a run costs nothing if the result is not what you wanted. See [Authoring content](#authoring-content).

> **`world.json`** at a world folder's root is what the folder says about itself: its **name**, the **name of the game built on it**, the **size new maps are created at**, and its record ceilings. Set them in the editor under **World → World Settings**. The file is optional — a folder without one runs on the stock answers.
>
> **The world name and the game name are different things, and only one of them is public.** The *game* name is what a player sees — the window title, the login screen, the chat greeting. The *world* name identifies one set of records, and exists so an operator can tell a live world from a test copy of it in the editor's title bar, the server window, and the logs. **It never reaches a player**, so there is no reason to make it presentable and no harm in calling a folder "friday-rollback-test".
>
> **Naming your game takes no build.** A game is a world folder plus the modules that give it rules, so the name lives with them: fill in **Game name** under World Settings and every server that opens the folder announces it. Three people can have a say, and each blank one defers to the next — an **operator** may override it for their own installation, else the **world** names it, else it carries the **engine's** name. None of them affects a file or folder name.
>
> **Map size.** A map is 16×12 tiles unless it says otherwise; `world.json` sets what a *new* map starts at, and any map can be resized in its properties. Maps joined by an edge must all be the same size — world coordinates run continuously across a seam, so a mismatch would make a step across one land somewhere other than where it looks — and the editor refuses to resize a linked map rather than letting that happen. **Resizing cannot be undone**: shrinking discards the tiles outside the new bounds and nothing writes them anywhere first, so the editor itemizes exactly what would go and tells you to copy the folder first.
>
> Past 128 tiles on an axis the editor warns, but nothing breaks. Drawing the world costs the same at every size — the client only ever draws what fits on screen — so what grows with a map is the two things that read it whole: crossing a seam loads three maps, and an NPC that loses its path searches the whole nine-map neighborhood before giving up. At 128×128 each takes about 40 ms, a few frames and under a tenth of an AI tick; at 256×256 both are about 180 ms, which is a visible stall. Resident memory is 96 bytes a tile, so 1.5 MB for a 128×128 map against 18 KB for the default. The actual ceiling is 65,535 on either axis, which is how wide a warp's destination coordinate is: past that, a map could hold tiles no door could point at.

> **A server runs on two folders, and the split is one question: does it change while the server runs?**
>
> `world/` is what an author wrote — maps, items, NPCs, spells, shops, quests, conversations, and `world.json`. Nothing in it changes unless somebody edits it, which is what lets a world be zipped up and handed to another machine. It is the folder the **editor** opens.
>
> `data/` is what one installation accumulated — accounts, guilds, market listings, trade journals, seasons, dropped items, the name registry, the ban lists, the clock, and the MOTD. It belongs to that server on that machine and means nothing beside a different world. Keeping the two apart is what stops a copied world carrying somebody's password hashes with it.
>
> Both are set independently, `WorldDir` and `DataDir`, and both default to a per-user folder — `%LocalAppData%\Mirage Source Remastered Server\` on Windows, `~/.local/share/mirage-source-remastered-server/` on Linux, `~/Library/Application Support/` on macOS. Not beside the executable: an installed server runs out of a folder the updater replaces wholesale, so a world and a set of accounts kept there would last exactly one update.
>
> **Seed data:** `server/src/Mirage.Server.Host/world/` holds a demo world — 4 maps, 8 items, 2 NPCs, 1 conversation, 1 shop, and the 5 species the loaded game reads. Any collection you leave out is created empty and written on first save, so a partial world folder boots fine.
>
> Those counts are checked against the folder by `.github/checks/check-seed-counts.mjs`, which CI runs — they have gone stale twice.
>
> **It is a demonstration, not a game.** One record of every family, so each format has a worked example you can open in the editor and read on disk. The four maps are linked in a square because the seamless neighbourhood is the thing a single map cannot show; map 1 carries a door, the pressure plate that opens it, and an item spawn. You can start a server, make a character, and walk it across a seam.
>
> **It references no artwork**, because none ships here. The maps are laid out with attributes rather than tile graphics, so the demo runs against whatever tilesets you supply rather than requiring a particular set.
>
> **It is deliberately small.** A genre-agnostic engine that shipped a fantasy world's towns, bestiary and quest prose would be handing every game a pile of content to delete first. What a world looks like is the game's business; what the records look like is the engine's, and that is all this demonstrates.

---

## Authoring content

The demo world in `world/` is hand-authored, and small enough to read. For anything larger the editor is
the tool: it opens a world folder directly, and a server it connects to tells it what record families that
world has, so it can author families this build was never compiled against.

**Generators are a game's business, not the engine's.** A world of a few hundred records is not something
to type, and the practical answer is a generator that computes against `Mirage.Shared` — item prices from
`EconomyFormulas`, and whatever a game's own module adds. A generator that takes a project reference on
the engine cannot drift from it, because it has no second copy of the rule to drift from.

One worked example exists: **[MirageSourceRemastered.Tools.Public](https://github.com/mnwachukwu/MirageSourceRemastered.Tools.Public)**,
which wrote Mirage Source Remastered's own world and imports an original VB6 Mirage Online server
directory into this JSON format. It targets **that** game's schema rather than this engine's, so treat it
as a worked example of the shape rather than something to run against a Core world.

Two things worth knowing before writing one:

- **A generator owns its collection outright.** Clearing the collection before writing is what makes a
  rerun reproducible, and it is also what loses hand edits. Author in the editor, or author in the
  generator — not both.
- **Maps are the exception.** A generated map that is then edited by hand cannot be regenerated without
  losing the edits, so generate maps into a scratch folder and diff, rather than over `world/`.

---

## Known limitation: the client has no name until a server gives it one

The client ships branded **Mirage Source Remastered** — the engine's name. It has no game identity of its
own, because one client is meant to reach every server. On connect, before you log in, the server tells it
the game's name, and the window title, the menu, and the HUD show that from then on.

So launching "Mirage Source Remastered" and arriving in "Brightwater" is expected. It is a handshake, not
a rebrand and not a bait and switch: the engine cannot know what to call itself until a server says.

Two things deliberately do **not** follow the server's name:

- **Your settings folder.** It stays under the engine name, so joining a differently-named game never
  moves your configuration or loses your options.
- **The executables.** Server and client filenames are fixed, which is what lets the management window
  find the server it ships beside.

A game names itself in its world folder — **World → World Settings → Game name** in the editor. An
operator who wants one installation called something else overrides it in the server window under
**Configuration → This server → Game name**, or as `gameName` in `serverconfig.json`; leaving that empty
takes the world's name, and a world that names none carries the engine's.

If you want a client that carries your own name and icon from the moment it launches, that is a rebuild
rather than a setting — see [Icons and shipping your own client](docs/branding.md).

---

## Documentation

This file covers what the project is and how to get it running. Everything else lives in
[`docs/`](docs/), one file per subject:

| Document | What it answers |
|---|---|
| [Building a game on Core](docs/building-on-core.md) | What the engine already does, the fifteen seams a game declares through, and the half-built features that fail silently |
| [Building, publishing, and releasing](docs/building.md) | How a working tree becomes installers, what the version number is bound to, how a tag cuts a release, and which platforms the output runs on |
| [Icons and shipping your own client](docs/branding.md) | Rebranding a fork: the four icon locations, the MonoGame window-icon trap, and repackaging a client without a compiler |
| [Scripting](docs/scripting.md) | Writing a game in Compass: the sibling checkout it needs, what the host does, what a script may declare, and what it may not reach |
| [Scripting API reference](docs/scripting-api.md) | Every type, member, and handler a script can use, generated from the engine itself |
| [Testing](docs/testing.md) | What the seven suites cover, how to run one on its own, and why the cross-platform matrix exists |
| [Technical decisions](docs/architecture.md) | Choices that are not obvious from the code, recorded with the reasoning that produced them |
| [Game data conventions](docs/game-data.md) | Rules the authored content is expected to follow, including music loop points |

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/mnwachukwu/tip)