# Survey

**The worked example of extending the engine by building from source.** A game written in C# against
`Mirage.Shared.Extensibility`, compiled, and ready to be listed in `GameModules.Load()`.

⚠ **It is built and tested, and the server does not load it.** The same game ships as the thing the
server actually runs, written in Compass, in
[`server/src/Mirage.Server.Host/world/scripts/`](../../server/src/Mirage.Server.Host/world/scripts/).
Loading both would be one game colliding with itself over every attribute key it owns, so this one is
opt-in: add the project reference back and put `new SurveyModule()` in `GameModules.Load()`, with the
scripts folder emptied or the world pointed elsewhere.

Nothing here has rotted for sitting unloaded. `SurveyModuleTests` builds a registry from it and holds
that registry to the same assertions `ScriptedSurveyTests` holds the scripted one to — the two routes
are checked against one specification, which is the only way "either route, same game" stays true.

**Which route to read.** [The C# playbook](../../docs/building-on-core.md) is this one, and it is the
stronger choice for a large game: the compiler checks every seam, a debugger steps through it, and a
typed packet of your own is possible. [The Compass playbook](../../docs/scripting-a-game.md) is the
other, and it is the stronger choice for a game that should ship without a toolchain.
[Choosing between them](../../docs/implementing-a-game.md) is the honest comparison.

You walk a world writing down what grows in it. Walking is tiring; resting gives it back. Every twelfth
step turns up something worth cataloguing, and enough of those earn you a rank.

Nothing fights, nothing levels, nothing dies.

## Why a game about plants

The engine claims to be genre-free. A demo that was a small RPG would not test that claim — it would
re-derive the shapes Core was carved out of and prove nothing. A botanical survey has no combat, no
vitals, no classes, no death, and no progression in the sense an RPG means it, so every seam it uses had
to be neutral enough to describe a game that wants none of those things.

It is also the reason each seam below says which *question* it answers rather than which feature it
implements. `ILingerPolicy` exists in Core so an RPG can stop a player escaping a fight by pulling the
plug; this game uses the same seam so a dropped connection is not a lost afternoon. Same mechanism,
opposite reason, no change to Core.

## What it declares

| Seam | Used for |
|---|---|
| `Attributes.Declare` | `stamina`, `staminaMax`, `rank` (seen by onlookers), `specimens` (yours alone) |
| `AddFamily` | `species` — what there is to find, authored in the editor |
| `AddChoiceSet` | `habitat` — shore, woodland, meadow |
| `AddEquipSlot` | `satchel` |
| `AddOverheadBar` | stamina, over every surveyor's head |
| `AddDisplayField` | a heading, rank, specimen count, and a stamina meter on the sidebar |
| `AddTickWork` | stamina recovery |
| `AddObserver` | joining enrols a surveyor; stepping spends stamina and sometimes finds something |
| `AddDeathPolicy` | refuses every death |
| `AddLingerPolicy` | the body stays half a minute after a dropped connection |
| `Packets` + `AddPacketRoute` | `survey.note` — the typed message a client compiled against this game sends |
| `AddAction` + `AddActionHandler` | "Note this down" in a square's menu, and "Open field book" beside it |
| `AddPanel` | the Field Book — a window of this game's own, painted by a client that never heard of it |
| `Start(IWorld)` | reads the authored species once, and keeps the world everything acts through |

That is every seam `ICoreBuilder` offers.

Two ways in, one outcome. **Right-click a square and "Note this down" is there**, under a Survey heading,
in a client that was never compiled against this module — it was told the caption and the id on join and
sends the id back. A client that DID compile this module can send `SurveyNotePacket` instead and name the
species it thinks it found, which a stock client cannot know.

## The Field Book

The second action in that menu opens a window rather than sending a verb, and the window is two things
this module already declared. Its body is the display fields it put on the `survey.book` surface — the
same four values the sidebar shows, at more length — and its one button carries `survey.note`, the same
id the menu item carries. So a screen costs a declaration, not a client.

What that buys is a game with a face of its own on a stock client. What it does not buy is layout: a
panel is a title, a column of declared rows, and a row of buttons, in that order. A game wanting columns,
a grid, or a list of its own wants a UI toolkit on the wire, which is a much larger thing than this.

## Its records

The five species live in the shipped world, at
[`server/src/Mirage.Server.Host/world/species/`](../../server/src/Mirage.Server.Host/world/species/),
because records are authored content rather than part of either module. Both routes declare the same
family and read the same files; neither owns them.

A world with none of them loads perfectly well — a surveyor then spends stamina and finds nothing, which
is what a world with nothing in it should do.

## Turning it on

Three steps, and the third is the one people forget:

1. Add the project reference to `server/src/Mirage.Server.Host/Mirage.Server.Host.csproj`.
2. Put `new SurveyModule()` first in `GameModules.Load()`, before the `ScriptedWorldModule`.
3. **Empty the world's `scripts/` folder**, or point the server at a world without one. Otherwise the
   scripted Survey declares `stamina` a moment after this one does, and the server stops at startup
   naming the collision — which is the engine working, not a bug.

## Turning everything off

Empty `GameModules.Load()` and empty the world's `scripts/` folder, and the server is Core alone: it
runs, accepts players, and moves them around a world with no game in it at all. That is a supported
configuration and the one to start a new game from.
