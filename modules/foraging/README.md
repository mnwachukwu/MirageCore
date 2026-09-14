# Foraging

**The smallest complete game on this engine, written twice.** Once as a script and once as a C#
module, with one test holding both to the same assertions.

You walk around picking things. Each pick adds a basket, the count sits on your sidebar, and <kbd>Q</kbd>
picks without opening a menu. The world also gets a kind of record it did not have — berries, with a
name, a ripeness, and a note about where they grow — which an author fills in through the map editor.

About fifty lines either way.

## Why it exists

It is the worked example behind [Your first game](https://mirage.pluperfect.dev/first-game), and the
site generates that page's snippets from these files. A tutorial can only show code that compiles and
declares what the prose says, because the code is here and a test reads it.

**[Survey](../survey/) is the bigger one.** Several files across several folders, a panel, two
policies, a message on the wire, records an author fills in. Read Foraging first and Survey second.

## What is here

| | |
|---|---|
| [`world/scripts/rules.cm`](world/scripts/rules.cm) | the whole game, in Compass, in one file |
| [`src/Mirage.Modules.Foraging/`](src/Mirage.Modules.Foraging/) | the same game, in C# |
| `tests/src/Mirage.Server.Tests/Modules/ForagingTests.cs` | both, against one set of assertions |

## Running it

⚠ **Neither version is loaded by the shipped server**, and they cannot both be loaded at once: they
declare the same attribute key and the same records, so the second one stops the server at startup.
That is the engine working.

**As a script.** Copy the scripts folder into a world and restart:

```bash
Copy-Item -Recurse "D:\Repos\MirageSourceRemasteredCore\modules\foraging\world\scripts" "C:\Users\<you>\AppData\Local\Mirage Core Server\world\"
```

The shipped world already carries Survey's scripts, so empty that folder first or point the server at
a different world.

**As a module.** Add the project reference to `server/src/Mirage.Server.Host`, put
`new ForageModule()` in `GameModules.Load()`, and empty the world's `scripts` folder.

## What it does not use

Panels, equip slots, death and linger policies, overhead bars, tick work, and messages on the wire.
All of those are in [Survey](../survey/). This one covers an attribute, a record family with a choice
set, a sidebar row, a verb with a key, and two handlers — which is enough for a game, and little
enough to read in one sitting.
