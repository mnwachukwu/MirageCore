# Survey

**The worked example of extending the engine by building from source.** A game written in C# against
`Mirage.Shared.Extensibility`, compiled, and listed in `GameModules.Load()`. The other route — a script,
with no compiler — is what [modules/README.md](../README.md) describes; this is the one that works today,
and it is the stronger one for anyone forking the repository to build their own game.

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

## Installing its records

The five species in `world/species/` are authored content, not build output. Copy them into a server's
world directory:

```bash
Copy-Item -Recurse -Force "D:\Repos\MirageSourceRemasteredCore\modules\survey\world\species" "D:\Repos\MirageSourceRemasteredCore\server\src\Mirage.Server.Host\world\"
```

A world with none of them loads perfectly well — a surveyor then spends stamina and finds nothing, which
is what a world with nothing in it should do.

## Turning it off

One line. Empty `GameModules.Load()` and the server is Core alone: it runs, accepts players, and moves
them around a world with no game in it. The project reference may stay — an assembly nothing constructs
costs nothing at runtime.
