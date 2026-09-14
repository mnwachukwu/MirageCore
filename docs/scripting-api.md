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
| `public function OnAction(Player who, string action, string on, integer map, integer x, integer y)` | the player picked one of this module's own verbs; 'on' names the body it was used on, and is blank for a verb offered on a square or on the HUD |
| `public function OnTick()` | the module's tick came round, however often game.TickEvery asked for |
| `public function OnPlayerTick(Player who)` | the same tick, once for each player in the world, which a script has no other way to walk |
| `public string function OnMayDie(Player who, string cause)` | somebody is about to die; yield a reason to stop it, or blank to let it happen |
| `public integer function OnLinger(Player who)` | their connection dropped; yield how many seconds the body stays in the world |
| `public function OnMessage(Player who, string message, Values values)` | a client sent one of this game's own messages, carrying the fields its model declared |

## The types

### Player

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Message(string line)` | — | Sends a line of text to this player, and to nobody else. |
| `IsHere` | `boolean` | Whether they are still in the world. A handle outlives the body it names. |
| `Map` | `integer` | Which map they are standing on, or zero when they are nowhere. |
| `X` | `integer` | How far across that map they are. |
| `Y` | `integer` | How far down it. |
| `Has(string key)` | `boolean` | Whether they carry that key at all, which is what tells absence from zero. |
| `Number(string key)` | `integer` | What they carry under that key, or zero where they carry nothing. |
| `Text(string key)` | `string` | The same, as text, or empty where they carry nothing. |
| `SetNumber(string key, integer amount)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string key, string value)` | — | The same, with text. |
| `WarpTo(integer map, integer x, integer y)` | `boolean` | Puts them on that map, x and y. False for a square that is not a real tile. |
| `Give(integer item, integer many)` | — | Puts that many of an item in their bag. |
| `Take(integer item, integer many)` | — | Takes that many out of it, worn ones included. |
| `Find(string name)` | `Player?` | The body behind a name, or nothing if no one is using it. OnAction hands you a name; this turns it into a player you can read and write. |

### Builder

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Attribute(string key, string seenBy)` | — | Declares a key this game counts, and who may see it: none, owner, or viewport. |
| `Message(string modelName)` | — | A message a client may send this game, taking its fields from one of this world's own models. It arrives at OnMessage with those fields as values. A stock client cannot compose one, so this is for a client, a tool, or a bot that knows it. |
| `TickEvery(integer ticks)` | — | How often OnTick and OnPlayerTick come round, in ticks. One by default, meaning every tick. A rule about resting or the weather wants far less. |
| `EquipSlot(string key, string caption)` | — | A place on a body something can be worn. A caption left blank becomes the key, as words. |
| `Heading(string caption)` | — | A heading on the sidebar, separating the rows under it. |
| `Field(string key, string caption, integer red, integer green, integer blue)` | — | A sidebar row: a key read live off the player, a caption, and a color as red, green, and blue. All three zero leaves the color to the client. |
| `Badge(string key, string caption, integer red, integer green, integer blue)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string key, string outOf, string caption, integer red, integer green, integer blue)` | — | A sidebar bar, filled by one key against another. |
| `Bar(string key, string outOf, integer red, integer green, integer blue)` | — | A bar over every body's head, in a color given as red, green, and blue, each 0 to 255. |
| `Action(string id, string caption, string heading)` | `Verb` | A verb this game offers, under a heading of its own. Picking it calls OnAction. Offered in a square's menu until the verb says otherwise, and handed back so where it is offered, what key reaches it, and what it needs are each their own line. |
| `Panel(string id, string title, integer width, integer height)` | `Panel` | A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, so its rows and its buttons are written underneath it. |

### Records

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Are(string plural, string singular, integer limit)` | — | What these records are called in the editor — the plural, then the singular — and how many there may be. A caption left blank keeps the model's own name. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Stored(string folder, string prefix)` | — | Where these records live: the folder under the world, and what each file is called before its number. Only needed for records already on disk. A new game leaves it out, and the model's name decides. |
| `Caption(string field, string caption)` | — | What one field is called on the form. Only needed where the field's own name is not the words an author should read. |
| `Range(string field, integer least, integer greatest)` | — | The bounds of a whole-number field. Equal bounds mean unbounded. |
| `Length(string field, integer characters)` | — | How long a text field may be. Zero means no limit. |

### Verb

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `OnTile()` | — | Offer it in the menu of a square. The default, and where most verbs belong. |
| `OnPlayer()` | — | Offer it in the menu of another player, who arrives as 'on' in OnAction. |
| `OnNpc()` | — | Offer it in the menu of a creature. Declaring one is what gives a plain creature a menu at all. |
| `OnHud()` | — | Offer it as a button on the HUD, which is about the player rather than about anything they are pointing at. |
| `Key(string key)` | — | A key that reaches it without the menu: B, E, J, K, N, P, Q, R, T, U, Y, or Z. The key acts on the square the player faces. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Opens(string panel)` | — | The panel it opens, by the id given to game.Panel. One that was never declared is refused by name rather than drawing a button that does nothing. |
| `NeedsAtLeast(string key, integer least)` | — | Offered only to a body carrying at least that much under that key. Below it the entry is grayed rather than missing, so a player can see the verb exists. |
| `NeedsCarrying(string key)` | — | Offered only to a body that carries that key at all. |
| `NeedsNothing(string key)` | — | Offered only to a body that does NOT carry that key. |

### Panel

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Key(string key)` | — | A key that opens it: B, E, J, K, N, P, Q, R, T, U, Y, or Z. |
| `Button(string caption, string verb)` | — | A button along its bottom: a caption, and the id of a verb it calls. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Asks(string modelName, string caption)` | — | Asks the player to fill one of this world's own models in, and send it. Every field of the model becomes a control — a number a spinner, a truth a checkbox, an enumeration a drop-down over its members — and the caption names the button under them. What they send arrives at OnMessage under the model's name. A panel asks for one message. |
| `Heading(string caption)` | — | A heading on this panel, separating the rows under it. |
| `Field(string key, string caption, integer red, integer green, integer blue)` | — | A row on this panel: a key read live off the player, a caption, and a color as red, green, and blue. All three zero leaves the color to the client. |
| `Badge(string key, string caption, integer red, integer green, integer blue)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string key, string outOf, string caption, integer red, integer green, integer blue)` | — | A bar on this panel, filled by one key against another. |

### Values

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Has(string field)` | `boolean` | Whether the message carried that field at all, which is what tells absence from zero. |
| `Number(string field)` | `integer` | What it carried under that name, or zero where it carried nothing. |
| `Text(string field)` | `string` | The same, as text, or empty where it carried nothing. An enumeration field arrives as the member's own name. |
| `Truth(string field)` | `boolean` | The same, as a yes or no. False where it carried nothing. |

## What a module may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when
the module is read, not when it runs. A world folder is something one person hands to another,
and a module that could open a file could read the accounts beside it.

Everything else a script can name is on this page. There is no way to reach a type the engine
did not register.
