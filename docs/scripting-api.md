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
| `public function OnAction(Player who, string action, string on, integer map, integer x, integer y)` | the player picked one of this module's own verbs; 'on' names the body it was used on, or is blank for a verb offered on a square or on the HUD |
| `public function OnTick()` | the module's tick came round, however often game.TickEvery asked for |
| `public function OnPlayerTick(Player who)` | the same tick, once for each player in the world — which is the list a script has no other way to walk |
| `public string function OnMayDie(Player who, string cause)` | somebody is about to die; yield a reason to stop it, or blank to let it happen |
| `public integer function OnLinger(Player who)` | their connection dropped; yield how many seconds the body stays in the world |

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
| `Find(string)` | `Player?` | The body behind a name, or nothing where nobody is answering to it. What OnAction's 'on' is for: a name is what arrives, and this is what reads and writes through it. |

### Builder

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Records(string, string, string, integer)` | `Records` | A kind of record this game authors, taken from one of this world's own models: the model's name, a plural caption, a singular one, and how many there may be. Every field of the model becomes a row on the form — an enumeration as a drop-down over its members, another model as a picker over that model's records. Hands the records back, so the rest can be said about them. |
| `Attribute(string, string)` | — | Declares a key this game counts, and who may see it: none, owner, or viewport. |
| `TickEvery(integer)` | — | How often OnTick and OnPlayerTick come round, in ticks. One by default, which is every tick — a rule about resting or the weather wants far less than that. |
| `EquipSlot(string, string)` | — | A place on a body something can be worn. A caption left blank becomes the key, as words. |
| `Heading(string)` | — | A heading on the sidebar, separating the rows under it. |
| `Field(string, string, integer, integer, integer)` | — | A sidebar row: a key read live off the player, a caption, and a color as red, green, and blue. All three nought leaves the color to the client. |
| `Badge(string, string, integer, integer, integer)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string, string, string, integer, integer, integer)` | — | A sidebar bar, filled by one key against another. |
| `Bar(string, string, integer, integer, integer)` | — | A bar over every body's head, in a color given as red, green, and blue, each 0 to 255. |
| `Action(string, string, string)` | `Verb` | A verb this game offers, under a heading of its own. Picking it calls OnAction. Offered in a square's menu until the verb says otherwise, and handed back so what it is offered on, what key reaches it, and what it needs are each a line of their own. |
| `Panel(string, string, integer, integer)` | `Panel` | A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, so its rows and its buttons are written underneath it. |

### Records

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Stored(string, string)` | — | Where these records live: the folder under the world, and what each file is called before its number. Only wanted for records that already exist on disk — a new game says nothing and lets the model's name decide. |
| `Caption(string, string)` | — | What one field is called on the form. Only wanted where the field's own name is not the words an author should read. |
| `Range(string, integer, integer)` | — | The bounds of a whole-number field. Equal bounds mean unbounded. |
| `Length(string, integer)` | — | How long a text field may be. Zero means no limit. |

### Verb

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `OnTile()` | — | Offer it in the menu of a square. The default, and where most verbs belong. |
| `OnPlayer()` | — | Offer it in the menu of another player, who arrives as 'on' in OnAction. |
| `OnNpc()` | — | Offer it in the menu of a creature. Declaring one is what gives a plain creature a menu at all. |
| `OnHud()` | — | Offer it as a button on the HUD, which is about the player rather than about anything they are pointing at. |
| `Key(string)` | — | A key that reaches it without the menu: B, E, J, K, N, P, Q, R, T, U, Y, or Z. The key acts on the square the player faces. |
| `Opens(string)` | — | The panel it opens, by the id given to game.Panel. One that was never declared is refused by name rather than drawing a button that does nothing. |
| `NeedsAtLeast(string, integer)` | — | Offered only to a body carrying at least that much under that key. Below it the entry is greyed rather than missing, so a player can tell the verb exists. |
| `NeedsCarrying(string)` | — | Offered only to a body that carries that key at all. |
| `NeedsNothing(string)` | — | Offered only to a body that does NOT carry that key. |

### Panel

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Key(string)` | — | A key that opens it: B, E, J, K, N, P, Q, R, T, U, Y, or Z. |
| `Button(string, string)` | — | A button along its bottom: a caption, and the id of a verb it calls. |
| `Heading(string)` | — | A heading on this panel, separating the rows under it. |
| `Field(string, string, integer, integer, integer)` | — | A row on this panel: a key read live off the player, a caption, and a color as red, green, and blue. All three nought leaves the color to the client. |
| `Badge(string, string, integer, integer, integer)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string, string, string, integer, integer, integer)` | — | A bar on this panel, filled by one key against another. |

## What a module may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when
the module is read, not when it runs. A world folder is something one person hands to another,
and a module that could open a file could read the accounts beside it.

Everything else a script can name is on this page. There is no way to reach a type the engine
did not register.
