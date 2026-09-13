# Building a game on Core

Core is an engine with no game in it. Run it with nothing loaded and you get a server that accepts
players, keeps their accounts and characters, loads a world of maps, and walks bodies around it.
Nobody has hit points, nothing is equippable, no menu offers anything, and nothing is drawn over
anyone's head. That is not a stripped-down mode — it is the whole engine, and it is where a new game
starts.

This is the playbook for the part that comes next: what the engine already does, where your game
attaches, and the mistakes that are easy to make because the engine will not complain about them.

Two routes exist and they meet at the same place. [`modules/README.md`](../modules/README.md) compares
them; [Scripting](scripting.md) covers the script route in full. Everything below is true of both,
because both end in a `CoreRegistry`.

---

## Start from what is already there

Before declaring anything, know what you would be re-implementing. The engine owns all of this, and a
game gets it without asking:

| | |
|---|---|
| **Bodies and places** | accounts, characters, maps, two walkable planes per tile, seam crossings between maps, warps |
| **Movement** | stepping, blocking, ramps, doors, the tile attributes behind them |
| **Things** | an inventory, items on the ground, a bank, shops, a market, trading between players, mail |
| **People** | chat with channels and tabs, parties, guilds, social lists, moderation |
| **Bodies that are not players** | NPC spawning, wandering, chasing, pathing, cross-map pursuit |
| **The world itself** | weather, time of day, decals, the tick |
| **Tools** | the map editor, a client that renders a game it has never heard of |

None of it assumes a genre. An NPC chases because a game told it to; the engine supplies the chase,
not the reason.

⚠ **What the engine does NOT have is a rule.** It has no combat, no leveling, no classes, no spells,
no currency worth anything, and no opinion about what dying costs. Those are the parts a game brings,
and their absence is the product rather than a gap in it.

---

## The fifteen seams

A game is an [`ICoreModule`](../shared/src/Mirage.Shared/Extensibility/ICoreModule.cs). It is asked to
describe itself once, and everything it can say is one of fifteen calls on the builder it is handed.

**What the game is made of**

| Seam | Answers |
|---|---|
| `AddFamily` | a kind of record this game authors — plants, quests, recipes — editable in the editor |
| `AddChoiceSet` | the fixed list a field on one of those records picks from |
| `Attributes` | the named values a body carries, and who is allowed to see each one |

**What the player sees**

| Seam | Answers |
|---|---|
| `AddDisplayField` | a value to show, on a named surface, read straight off a body's attributes |
| `AddOverheadBar` | a row over a body's head, reading two of that body's attributes |
| `AddEquipSlot` | a place on a character where something can be worn |
| `AddPanel` | a screen this game paints: a title, a surface, and the verbs under it |

**What the player does**

| Seam | Answers |
|---|---|
| `AddAction` | a verb, its caption, and the surface that offers it |
| `AddActionHandler` | what happens when one is picked |
| `Packets` | a wire command this game can read |
| `AddPacketRoute` | where a command that was read goes |

**What the game does on its own**

| Seam | Answers |
|---|---|
| `AddTickWork` | work the game loop drives |
| `AddObserver` | what to be told when something happens in the world |
| `AddDeathPolicy` | whether dying happens at all, what it costs, where the body comes back |
| `AddLingerPolicy` | how long a dropped connection leaves a body standing |

Declare none of them and you have the engine by itself. Every seam's "declare nothing" case is a
coherent game, not a broken one.

---

## Four rules that hold everywhere

### Data travels, never code

Everything a game declares reaches the client as data when a player joins: the attribute numbering,
the equipment slots, the bars, the rows, the verbs and where they are offered. A stock client renders
a game it has never heard of, and nothing is deployed beside it.

The rule a verb triggers runs on the server. Nothing about your game's behavior crosses the wire, which
is why a client needs no knowledge of your game and why one client binary serves every game built here.

### Declaring and acting are two phases

`Configure` runs before the world exists. Nothing may be called from it, because what it declares is
what decides what gets built. `Start` is the other end: the engine is built, the world is loaded,
nothing is live yet, and the module is handed the one [`IWorld`](../shared/src/Mirage.Shared/Extensibility/IWorld.cs)
it will ever get.

Keep that reference. Everything the game does afterwards — from an observer, from tick work, from a
policy, from an action handler — goes through it.

⚠ **The builder is not valid after `Configure` returns.** A module that stashes it and declares later
is writing into a registry somebody is already reading, and is refused.

### An ordinal is global to its surface

Headings, rows, and menu items are ordered by a number that belongs to the surface, not to the module
that declared it. Two modules both numbering from zero interleave: one's heading lands in the middle of
the other's rows. Leave room. The world's own scripted rules number from 1000 for exactly this reason,
so they sit after whatever the compiled game declared.

### A collision stops the server

Two modules claiming the same family id, choice-set id, attribute key, or packet command is an error at
startup that names the second module. It is never last-one-wins. The one exception is a *script* whose
declaration collides with the compiled game: that single declaration is refused and logged, the rest
are made, and the server starts — because the operator running a world someone else authored should not
be left with a server that will not boot.

---

## Two halves, and either one missing is silent

This is the failure mode to watch for. Several seams are only half of a working feature, and declaring
one without the other produces no error anywhere — the game simply does nothing, correctly.

| Declare this | Without this | And you get |
|---|---|---|
| `AddAction` | `AddActionHandler` | a menu item that does nothing when picked |
| `Packets` | `AddPacketRoute` | a command that deserializes and is delivered to nobody |
| `AddDisplayField` | the attribute it reads | a row that renders empty forever |
| `AddOverheadBar` | both attributes it reads | a bar that is always full, or always empty |
| `AddFamily` | records authored into it | an editor tab with nothing in it |
| `AddEquipSlot` | items that fit it | a slot nothing can ever go in |

🔴 **Write the test that asserts both halves.** The compiler checks neither, and a game missing one
half looks exactly like a game that has not implemented that feature yet. The registry is a plain
object — build it in a test and assert it field by field, the way
[`SurveyModuleTests`](../tests/src/Mirage.Server.Tests/Modules/SurveyModuleTests.cs) does.

---

## Where a rule actually runs

Four places, and the choice is usually obvious once stated:

- **An action handler** — the player did something and the answer depends on what they did. Harvesting
  a tile, opening a chest, reading a sign.
- **An observer** — something happened in the world and the game wants to react. A player joined,
  moved, left. The engine does the noticing; the game decides whether it matters.
- **Tick work** — something has to happen on its own schedule, whether or not anybody did anything.
  Regeneration, decay, a timer.
- **A policy** — the engine is about to do something and is asking the game what the rule is. Dying,
  lingering after a disconnect.

A rule that does not fit any of the four is usually a rule that wants a seam the engine does not have.
Say so rather than working around it: a module that grows a second project reference has found a gap.

---

## Keeping the game out of the engine

A module references `Mirage.Shared` and nothing else. Not the server it runs inside, not the client
that draws it, not the editor that authors its records. Core names no module and no file in Core is
edited to make room for one — `GameModules.Load` is the single line where a game plugs in.

The engine's own name is subject to the same rule, and a test enforces it. What a game is called is
the operator's choice, else the world's, else the engine's, resolved in exactly one place; a line
reaching past that to the constant hard-codes the bottom layer and is refused by
[`GameNameReachesPlayersTests`](../tests/src/Mirage.Server.Tests/Configuration/GameNameReachesPlayersTests.cs).

---

## Related

| | |
|---|---|
| [Scripting](scripting.md) | Writing a game in Compass instead of C# — the same seams, no toolchain |
| [Scripting API reference](scripting-api.md) | Every type, member, and handler a script can use, generated from the engine |
| [`modules/README.md`](../modules/README.md) | The two routes compared, the layout a module takes, and how one is distributed |
| [Testing](testing.md) | What the seven suites cover and how to run one on its own |
| [Game data conventions](game-data.md) | Rules the authored content is expected to follow |
