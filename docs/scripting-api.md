# The scripting API

What a world's own rules may name, and what each thing does.

> Generated from the catalog the server registers, by `ScriptApiReferenceTests`. Editing this
> file by hand is undone by the next test run — the sentences live beside the bindings in
> `ScriptedWorldModule.Catalog`.

A module is a folder of `.cm` files under `<world>/scripts/`, holding a shared model called
`Rules`. Every handler on it is optional and every one has to be `public`, because a function
only the engine calls is one nothing in the module calls.

## The handlers

| Written | Called |
|---|---|
| `public function Configure(Builder game)` | once, before the world exists, so a module can declare what it adds |
| `public function OnPlayerJoined(Player who)` | a player is in the world and has been sent everything they need |
| `public function OnPlayerLeft(Player who)` | they have left, while their record is still readable |
| `public function OnPlayerMoved(Player who, integer fromX, integer fromY)` | every accepted step, seam crossings included |
| `public function OnAction(Player who, string action, integer map, integer x, integer y)` | the player picked one of this module's own verbs |
| `public function OnTick()` | every tick |

## The types

### Player

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Message(string)` | — | Sends a line of text to this player, and to nobody else. |
| `IsHere` | `boolean` | Whether they are still in the world. A handle outlives the body it names. |
| `Map` | `integer` | Which map they are standing on, or zero when they are nowhere. |
| `X` | `integer` | How far across that map they are. |
| `Y` | `integer` | How far down it. |
| `Has(string)` | `boolean` | Whether they carry that key at all, which is what tells absence from zero. |
| `Number(string)` | `integer` | What they carry under that key, or zero where they carry nothing. |
| `Text(string)` | `string` | The same, as text, or empty where they carry nothing. |
| `SetNumber(string, integer)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string, string)` | — | The same, with text. |
| `WarpTo(integer, integer, integer)` | `boolean` | Puts them on that map, x and y. False for a square that is not a real tile. |
| `Give(integer, integer)` | — | Puts that many of an item in their bag. |
| `Take(integer, integer)` | — | Takes that many out of it, worn ones included. |

### Builder

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Attribute(string, string)` | — | Declares a key this game counts, and who may see it: none, owner, or viewport. |
| `Heading(string)` | — | A heading on the sidebar, separating the rows under it. |
| `Field(string, string)` | — | A sidebar row: a caption, and a key read live off the player. |
| `Badge(string, string)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string, string, string)` | — | A sidebar bar, filled by one key against another. |
| `Bar(string, string, integer, integer, integer)` | — | A bar over every body's head, in a color given as red, green, and blue, each 0 to 255. |
| `Action(string, string, string)` | — | A verb in the square menu, under a heading of its own. Picking it calls OnAction. |

## What a module may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when
the module is read, not when it runs. A world folder is something one person hands to another,
and a module that could open a file could read the accounts beside it.

Everything else a script can name is on this page. There is no way to reach a type the engine
did not register.
