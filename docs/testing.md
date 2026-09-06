# Testing

What the suites cover, how to run one, and why the cross-platform matrix exists.

The six test suites are one **NUnit** project per source portion, each named for the code it exercises; the root `Mirage.slnx` groups them under a `/Tests/src/` solution folder, with the drivers that run them in `/Tests/` above.

Eleven of the solution's twenty-four projects are testing — the six suites plus five drivers — against nine that are the game itself. That ratio is what the rest of this page is about: the suites are split so a shell can be swapped without the core noticing, and each split costs a project.

| Test project | Exercises | Runs on |
|---|---|---|
| `Mirage.Shared.Tests` | `Mirage.Shared` + `Mirage.Ui` + `Mirage.Updates` | all three platforms |
| `Mirage.Server.Tests` | `Mirage.Server.Core` + `Mirage.Server.Host` | all three platforms |
| `Mirage.Client.Core.Tests` | `Mirage.Client.Core` — the shell-agnostic core | all three platforms |
| `Mirage.Editor.Tests` | `Mirage.Editor` | all three platforms |
| `Mirage.Server.Shell.Tests` | `Mirage.Server.Shell` — the management window | all three platforms |
| `Mirage.Client.Shell.Tests` | `Mirage.Client.Shell` — input, panels, HUD | all three platforms |

**Why core and shell are tested apart.** The reference only ever points one way: `Mirage.Client.Shell`
references `Mirage.Client.Core`, and `Mirage.Server.Shell` and `Mirage.Server.Host` each reference
`Mirage.Server.Core`. No core references a shell. That is what makes a shell swappable — MonoGame enters
the client only through `Mirage.Client.Shell`, and Avalonia reaches the server only through
`Mirage.Server.Shell` and the `Mirage.Ui` theme beneath it, so a different renderer, or a server run with
no management window, changes nothing underneath. The split suites are how that stays true: a core suite
is compiled with no shell on its reference path, so core logic that reached for one would fail to build
here rather than quietly bind the core to a single front end. Note the server shell sits *beside* the
host on `Mirage.Server.Core` rather than wrapping it — neither references the other.

**The two sides are named asymmetrically, and it is worth knowing before you go looking.** The client
spells out both halves — `Mirage.Client.Core.Tests` and `Mirage.Client.Shell.Tests`. The server names
only its shell, `Mirage.Server.Shell.Tests`, and puts everything else in a bare `Mirage.Server.Tests`:
`Mirage.Server.Core`, `Mirage.Server.Host` **and** `Mirage.Shared`, three projects in one suite.

So **there is no `Mirage.Server.Core.Tests` and no `Mirage.Server.Host.Tests`.** `Mirage.Server.Tests` is
the counterpart of `Mirage.Client.Core.Tests` by role, not by name.

**`shared/` is the one area whose suite covers three projects.** `Mirage.Shared.Tests` holds the formulas,
records, protocol and security helpers, the `Mirage.Ui` theme dictionaries and the `Mirage.Updates` feed
resolution. It is the only suite that references no server, client or editor, so it runs without any
application built — which is why CI runs it first.

| Scope | Command |
|---|---|
| **Everything, one shot** | `dotnet msbuild tests/Mirage.Test.csproj -t:TestAll` |
| Everything (built-in) | `dotnet test Mirage.slnx` |
| One area | `dotnet msbuild tests/Mirage.Client.Test.csproj -t:TestAll` (or `tests/Mirage.Server.Test.csproj` / `tests/Mirage.Editor.Test.csproj`) |
| One project | `dotnet test tests/src/Mirage.Client.Shell.Tests/Mirage.Client.Shell.Tests.csproj` |

**Where the suites live, and why the drivers exist.** All six sit under `tests/src/`, out of the
`src/` trees they exercise, and each is named for what it covers rather than for where its subject
happens to be. The drivers sit one level up in `tests/`, so the folder reads as five entry points
over a directory of suites. Each area gets a driver — `Mirage.Shared.Test.csproj`, `Mirage.Server.Test.csproj`,
`Mirage.Client.Test.csproj`, `Mirage.Editor.Test.csproj` — mirroring the publish profiles: publishing the
server is `Mirage.Server.Publish.csproj`, so testing it is `Mirage.Server.Test.csproj`, and neither asks
you to remember a path. `shared/` has no publish profile, because nothing ships it on its own.

Two areas hold two suites each, and the driver names both: the server driver runs `Mirage.Server.Tests`
and `Mirage.Server.Shell.Tests`, the client driver runs `Mirage.Client.Core.Tests` and
`Mirage.Client.Shell.Tests`. Each keeps going after a failure and reports both results, so one red suite
never hides its neighbour.

The area drivers deliberately do **not** run on a solution build; only the aggregate
`Mirage.Test.csproj` does. Both would mean every suite running twice — the same double-work that once
turned a 9-second solution build into a 127-second one when the publish projects defaulted to
publishing. `-t:TestAll` is always explicit for an area.

Inside each suite the sources are grouped into folders (`Combat/`, `Ai/`, `Formulas/`, …) so the
project root is the csproj and a handful of directories rather than seventy loose files. The folders
carry no namespace: everything stays in `Mirage.Server.Tests` and friends, because a folder is not a
namespace in C# and renaming them would be churn no reader benefits from.

`Mirage.Test.csproj` runs every suite and the documentation checks, **keeps going after a failure** so one red suite doesn't hide the rest, then reports a per-suite breakdown. The seven exit codes have to concatenate to `0000000`. A bare `dotnet msbuild tests/Mirage.Test.csproj` runs `TestAll` by default, as does a Visual Studio right-click → **Build**; uncheck it in Configuration Manager to keep it out of `Ctrl+Shift+B`.

> Building or testing while the game is running fails to copy the shared DLLs — close the running app first.

## No suite reads authored content

**Unit tests build their own fixtures.** Anything that needs records, a world or a data directory writes
one into a temp folder and deletes it afterwards; nothing under `tests/src/` asserts against
`server/src/Mirage.Server.Host/world`. Real content changes for content reasons, and a test that reads it
fails for reasons that have nothing to do with the code it names.

The one thing that *does* read the shipped seed is a **content guard**, and it says so: those fixtures
carry `[Category("Content")]`, and every unit run filters them out with `--filter "TestCategory!=Content"`.
They run instead in the aggregate's `── Checks ──` block, beside the documentation checks — the other
guards that read committed artifacts rather than exercise code — and as their own CI step.

| | Runs in | Reads |
|---|---|---|
| The six suites | every driver, a solution build, `dotnet test` | their own temp fixtures |
| `[Category("Content")]` | the Checks block, and one CI step | `server/src/Mirage.Server.Host/world` |

**Why the guard is kept rather than converted.** It holds the seed to the engine's own rules — drop tables
naming real items, scrolls teaching real spells, quest chains that can be walked in order, prices that
survive a regeneration. Rebuilt on synthetic data those assertions become tautologies: you would author
the fixture to satisfy the rule you are checking. Its value is that the content is *authored elsewhere*,
by generators outside this repository that no test can reach, and it has already caught a renamed field
that would have zeroed every gold drop and a regeneration that silently wiped all 558 prices.

An empty read there is a **failure**, not a skip. The seed is tracked — 1,172 files in every checkout — so
"no seed found" means the folder was emptied or the loader stopped matching its filenames. A guard that
skips is a guard that has quietly stopped guarding.

**Current coverage.** All six suites are real; the placeholder smoke tests are gone. Roughly 34,000 lines of tests across 200 files, weighted toward the server, where most of the rules live.

What they actually pin, by kind:

- **Formulas** — `StatFormulasTests`, `DeathFormulasTests`, `EconomyFormulasTests`, `GuildWarFormulasTests`, `SeasonFormulasTests`, `TerritoryFormulasTests`, `ItemFormulaTests`. The tuning constants are meant to be retuned, so these pin shape and invariants rather than magic numbers.
- **Parity** — `NpcPlayerFormulaParityTests` holds NPCs and players to the same damage and mitigation math, and `LocalizationParityTests` (present in three suites) holds the four language files to the same key set, so a translation cannot silently go missing.
- **Systems** — one suite per feature area: guilds, quests, mail, market, trade, party, bank, shops, social, weather, time of day, regeneration, conversations, objectives.
- **Seams and geometry** — `DeterminismSeamTests`, `LayerLogicTests`, `NpcChaseRoutingTests`, `NpcPathCacheTests`, `NpcFootprintCombatTests`, `WorldCoordHelperFootprintTests`. The cross-map and two-plane cases are where the engine is easiest to break by accident.
- **Determinism** — `PinnedClockTests` and `PinnedRandomnessTests` keep time and randomness injectable, which is what makes the rest of the suite reproducible.
- **Convention** — `LocalizationConventionTests` reads the editor's own sources to check that anything using localized strings has a refresh hook, and `GamePanelContractTests` / `PanelPolicyTests` hold every panel to one declared behavior table so a new panel cannot silently miss a rule.
- **Performance baselines** — `PerfBaselineTests` and `RenderPerfBaselineTests` catch regressions in the per-frame and per-tick hot paths.
- **Platform** — `UserPathsTests` pins where a per-user file belongs on each OS. It asserts the *current* platform's branch rather than mocking one, because there is nothing to mock: the code switches on `RuntimeInformation.IsOSPlatform`, which no test can lie about.

## Across platforms

CI runs **every suite on Linux, macOS and Windows**.

Cross-building for three platforms from one runner proves they *compile* and nothing more — it never
runs a line of code on the other two. That gap is not theoretical here:

- `UserPaths` branches three ways on the operating system. On any single machine, two of those
  branches are unreachable by any test.
- Linux is **case-sensitive**. Every path the persistence and localization loaders build is somewhere
  that difference could bite, and no Windows runner will ever tell you.

`Mirage.Client.Shell.Tests` is the one that needs a word of explanation, because it pulls in MonoGame
and MonoGame has a reputation for being Windows-shaped. The **runtime** is not: `DesktopGL` is
cross-platform by design, and no test in the suite builds a `GraphicsDevice` or loads a piece of
content. What is Windows-bound is compiling the **content** — see
[Building](building.md#compiled-content-is-committed) — and since that output is committed rather than
built, no ordinary build reaches the pipeline at all. The suite needs no special handling to run
anywhere.

Keeping it in the matrix is the point. The client is a third of what ships, and a row that only ever
goes green on Windows is not evidence about Linux.

Windows appears in the matrix as well as in the full build job, so the three legs are directly
comparable. It costs a minute of duplicated work and buys a row per platform that is either green or
not, instead of one green row and two a reader has to reason about.

Each leg also **builds the client for real** — content pipeline included — after its suites. That is the
only check on the claim that a from-source build needs the .NET SDK and nothing else, and it is a
different question from whether the code runs: the pipeline shells out to `mgcb`, whose font processor
is native code and whose inputs have to exist on the machine doing the building.

## What has actually been played

Automated tests say the logic runs; they say nothing about rendering, audio, input or windowing. Those
have been exercised by hand, and unevenly:

| | Played on |
|---|---|
| Windows | continuously, during development |
| Linux | **yes** — SteamOS on a Steam Deck, handheld mode, which tested the controller scheme at the same time. Controller-arbitration changes made *since* that session have not been back on the device |
| macOS | **never** |

Worth stating plainly because the download page offers all three. The macOS build is compiled, unit
tested and unplayed.

## What no compiler reads is checked too

Four checks in [`.github/checks/`](../.github/checks), each its own CI job, each gating a release
alongside the suites, and all four run by a local `-t:TestAll` as well:

| Check | What it holds |
|---|---|
| `check-doc-links.mjs` | Every Markdown link that points inside the repository still resolves |
| `check-seed-counts.mjs` | The record counts the README quotes match the folders they describe |
| `check-readme-facts.mjs` | The claims the prose makes about the codebase — project count, suite count, level ceiling, framework — match the repository |
| `check-prebuilt-content.mjs` | The committed font and shader binaries were built from the content sources beside them |

The middle two exist because a number in a sentence is invisible to the compiler. Nobody adding a test
project rereads a paragraph in another file, and a count stated in five places is a count that will
disagree with itself. The last one covers the same blindness in a binary: see
[Building](building.md#compiled-content-is-committed).

```sh
node .github/checks/check-doc-links.mjs
```

[`.github/checks/check-doc-links.mjs`](../.github/checks/check-doc-links.mjs) reads every Markdown file in the
repository and follows every link that points inside it — a path, an in-page anchor, or both. It runs
as its own CI job and gates a release alongside the suites.

**Node, in a C# repository, on purpose.** It imports nothing outside Node's standard library, so
there is no `package.json`, no install step and nothing to keep up to date — and every GitHub-hosted
runner ships Node, so the CI job needs no toolchain setup and costs seconds. The first version was a
.NET 10 file-based program, which read better beside the rest of the codebase but required
`setup-dotnet`: .NET 10 is not preinstalled on the runner images, and file-based programs are a .NET
10 feature, so whatever SDK an image carries cannot be assumed. Thirty seconds of setup to check a
few dozen links was the wrong trade for a check meant to be cheap enough that nobody resents it.

It exists because prose rots differently from code. A renamed file or a reorganized document breaks
links that nothing compiles and no test covers, and the person who moved the file is the least likely
to notice. Splitting this documentation out of the README broke four links and left one reference
pointing at a heading that had already stopped existing some time earlier — none of which any other
check here would ever have reported.

Two deliberate choices:

- **External links are not followed.** Reaching the network would make a documentation check depend
  on somebody else's uptime, and a check that fails at random gets ignored.
- **Spelling is compared against real directory entries**, not by asking whether the file exists.
  Windows and macOS answer that question case-insensitively, so `Docs/Building.md` resolves on the
  machine that wrote it and 404s on Linux and on github.com. Same lesson as `UserPaths` above,
  arriving from a different direction.
