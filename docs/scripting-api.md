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
| `public function OnDied(Player who, Player killer, string cause)` | somebody died, and the body has not moved yet. What a death COSTS is written here - gear wear, a dropped bag, lost experience - and it runs while they are still lying where they fell, so anything shed lands on the tile they can go back for. 'killer' is nobody when the world itself did it, which Player.IsHere answers. Call Player.RespawnAt from in here to say where they come back |
| `public integer function OnLinger(Player who)` | their connection dropped; yield how many seconds the body stays in the world |
| `public function OnMessage(Player who, string message, Values values)` | a client sent one of this game's own messages, carrying the fields its model declared |
| `public function OnContact(Npc it, Player who)` | a creature reached the player it was chasing. ⚠ Only for a PLAYER quarry — the parameter says Player, and a handler handed the wrong kind of body is worse than one that is not called. A creature that reached another creature raises OnNpcContact |
| `public function OnNpcContact(Npc it, Npc other)` | a creature reached the creature it was chasing. Creatures notice each other on their own — anything not its own kind and not sharing its group, within its range — so a world with two hostile species needs nothing but this handler to make them fight |
| `public string function OnMayUse(Player who, integer item, integer slot)` | somebody is about to use something out of their bag; yield a reason to stop it, or blank to let it happen. 🔴 Asked BEFORE anything happens, which is the whole difference between this and OnItemUsed: a rule told afterwards can only take the gear off again, and the player sees a flicker. A refusal costs them neither the item nor the beat |
| `public function OnItemUsed(Player who, integer item, integer slot)` | they used something out of their bag. ⚠ Raised for EVERY use, the engine's own two included: wearing a piece of gear and opening a door have already happened by the time this is called. Everything else an item might mean is a game's, and this is where it is written - a scroll that teaches, a potion that heals, a horn that is heard across the map |
| `public function OnPlayerWarped(Player who, integer fromMap, integer fromX, integer fromY)` | they arrived somewhere they did not walk to. ⚠ A warp is NOT a step, so it raises nothing at OnPlayerMoved - a rule that watched only steps would miss every door, every teleport, and every respawn |

## The types

### Player

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Message(string line)` | — | Sends a line of text to this player, and to nobody else. |
| `Name` | `string` | Their character's name, trimmed. Blank once the body has left - the same answer a creature's name gives, because it is the same question. |
| `IsHere` | `boolean` | Whether they are still in the world. A handle outlives the body it names. |
| `Guild` | `string` | The name of the guild their account belongs to, or empty for none. |
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
| `Carrying(integer item)` | `integer` | How many of that item they are carrying, worn ones and every stack counted together. The read that goes with Give and Take: a rule charging somebody in a currency of its own asks this first, because taking more than they have and taking what they have look the same afterwards. |
| `Wear(integer item)` | `boolean` | Puts something they are carrying ON, taking off whatever was in its slot. 🔴 A game that hands somebody a sword has no other way to put it in their hand - an opening kit, a quest reward, a curse that arms them against their will. ⚠ None of the refusals a player pressing the button meets apply: a RULE arming somebody mid-fight meant to. False for a body not carrying it, and for a piece naming a slot this world does not declare. |
| `TakeOff(integer item)` | `boolean` | Takes something off. It stays in the bag. The other half of Wear, and what a rule about ruined gear needs: a piece worn down to nothing comes off the body it broke on. False for a body not wearing it. |
| `IsRunning` | `boolean` | Whether they are running rather than walking right now. What running COSTS is yours; the engine moves the body and this is how a rule hears about it. |
| `Engage(integer seconds)` | — | Marks them as in a fight for that many seconds. What being in a fight MEANS is the game's; the engine keeps the clock and the client shows it. |
| `Down(integer seconds)` | — | Marks them as out of the fight for that many seconds. |
| `Mark(integer seconds)` | — | Marks them for that many seconds - a target somebody else's rule put a flag on. |
| `Flag(integer seconds)` | — | Marks them as the one who started it, for that many seconds. What that costs them is the game's to decide. |
| `Wait(integer seconds)` | — | Holds them off acting again for that many seconds. |
| `Float(string line, integer red, integer green, integer blue)` | — | Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. ⚠ The one place a script asks the client to DRAW: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state. |
| `IsEngaged` | `boolean` | Whether they are in a fight right now. What being in one MEANS is yours - holding regenthrough it, refusing a warp out of it - and this is the clock you set, read back. |
| `IsDowned` | `boolean` | Whether they are out of action right now. |
| `IsMarked` | `boolean` | Whether they carry a mark right now. |
| `IsAggressor` | `boolean` | Whether they are flagged as having started it. |
| `IsWaiting` | `boolean` | Whether they are still held off acting. ⚠ Measured FORWARD from when the cooldownstarted, which is the direction the bar drawing it measures. |
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, the way they are facing. True flings sparks with it, which is what makes a swing read as having HIT something rather than passing through air. What the crescent means is yours: a sword, a claw, a thrown net. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at a creature: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at another player: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. ⚠ Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes them out of the world, with a cause OnMayDie can read. False where something refused to let them die, which is what a death policy is for. |
| `RespawnAt(integer map, integer x, integer y)` | — | Says where this body comes back. ⚠ Only from inside OnDied, and only about the body that died - it is read the moment that handler returns, and anywhere else it does nothing. Say nothing and they come back where the world puts them. |
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
| `AskAtCreation(string key, string caption, string records)` | — | Something to ask BEFORE a character exists - a class, a bloodline, a starting town. 🔴 The one question a game cannot ask any other way: everything else it wants to know it asks of a body already in the world, and what a character IS has to be settled before there is one. The player picks from the records you name, listed by their own names, and the number they picked is written onto them under your key before OnPlayerJoined runs - so you read it the ordinary way and need no handler. ⚠ A blank record is not offered, because a list of unnamed slots is a screen nobody can use. |
| `Panel(string id, string title, integer width, integer height)` | `Panel` | A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, so its rows and its buttons are written underneath it. |

### Records

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Are(string plural, string singular, integer limit)` | — | What these records are called in the editor — the plural, then the singular — and how many there may be. A caption left blank keeps the model's own name. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Extend(string records)` | — | Adds this model's fields to records that ALREADY EXIST rather than declaring a kind of its own - the engine's 'Items' or 'NPCs', or another module's. 🔴 How a game hangs its own facts on a record the ENGINE owns: which classes may wield a sword, which spell is written on a scroll. An item's own properties are a closed set because Core cannot act on one it has never heard of; yours are open, and they belong ON the sword rather than in a table beside it. ⚠ What these records are called, where they live and how many there may be are the other family's answers, so Are and Stored say nothing here. |
| `Stored(string folder, string prefix)` | — | Where these records live: the folder under the world, and what each file is called before its number. Only needed for records already on disk. A new game leaves it out, and the model's name decides. |
| `Caption(string field, string caption)` | — | What one field is called on the form. Only needed where the field's own name is not the words an author should read. |
| `Range(string field, integer least, integer greatest)` | — | The bounds of a whole-number field. Equal bounds mean unbounded. |
| `Length(string field, integer characters)` | — | How long a text field may be. Zero means no limit. |
| `Points(string field, string records)` | — | Makes a whole-number field a PICKER over another kind of record, listing them by name. 🔴 The way to point at the ENGINE'S own records - 'Items', 'NPCs', 'Maps', 'Shops', 'Conversations' - which have no model to type a field as. A field typed as one of your own models already does this and needs nothing here. ⚠ The number is still what is stored: this changes what the form draws and nothing about what a rule reads back. |

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
| `Interacts()` | — | Picking it also does what the engine's own reach key would have done - a shop, a conversation, or the body's own line. ⚠ A game that binds E takes that key outright, so this is how it hands interaction back. Only meaningful on a creature. |
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

### Npc

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Name` | `string` | What it is called - the name on its record, trimmed. Blank once the body has left. |
| `IsHere` | `boolean` | Whether the body is still in the world. A handle outlives what it names. |
| `Map` | `integer` | Which map it is standing on, or zero when it is nowhere. |
| `X` | `integer` | How far across that map it is. |
| `Y` | `integer` | How far down it. |
| `Has(string key)` | `boolean` | Whether it carries that key at all, which is what tells absence from zero. |
| `Number(string key)` | `integer` | What it carries under that key, or zero where it carries nothing. |
| `Text(string key)` | `string` | The same, as text, or empty where it carries nothing. |
| `SetNumber(string key, integer amount)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string key, string value)` | — | The same, with text. |
| `Float(string line, integer red, integer green, integer blue)` | — | Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. ⚠ The one place a script asks the client to DRAW: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state. |
| `IsEngaged` | `boolean` | Whether they are in a fight right now. What being in one MEANS is yours - holding regenthrough it, refusing a warp out of it - and this is the clock you set, read back. |
| `IsMarked` | `boolean` | Whether they carry a mark right now. |
| `IsAggressor` | `boolean` | Whether they are flagged as having started it. |
| `IsWaiting` | `boolean` | Whether they are still held off acting. ⚠ Measured FORWARD from when the cooldownstarted, which is the direction the bar drawing it measures. |
| `Engage(integer seconds)` | — | Marks it as in a fight for that many seconds, which is what makes its overhead bars appear. What being in a fight MEANS is the game's; the engine keeps the clock. |
| `Mark(integer seconds)` | — | Marks it for that many seconds - a flag a game puts on a body and reads back later, which is how a kill gets claimed by whoever earned it. |
| `Flag(integer seconds)` | — | Marks it as the one that started it, for that many seconds. |
| `Wait(integer seconds)` | — | Holds it off acting again for that many seconds. |
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, the way they are facing. True flings sparks with it, which is what makes a swing read as having HIT something rather than passing through air. What the crescent means is yours: a sword, a claw, a thrown net. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at a player: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at another creature: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. ⚠ Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes it out of the world, with a cause the death policy can read. False for a body that was not there, or that something refused to let die. |
| `Kind` | `integer` | Which creature it is a copy of - the number of its record in this world's creatures. ⚠ What a rule about a species keys on: a bounty per creature, a drop table, which bodies a quest counts. Name answers with what a player READS, and two records may share a name, so a rule written against one silently follows an editor rename. Zero for a body that has left the world. |
| `Chase(Player who)` | `boolean` | Sends it after a player, whether or not it would ever have noticed them itself - which is how a creature that only fights back gets written: author it to amble, and chase whoever hits it. It commits to the approach rather than walking in, because a body that was SENT is not deciding whether to be interested. ⚠ It overrides the noticing, not the legs: one authored to hold its tile still holds it, and one authored to open the gap runs from them instead. False where either body has left the world. |
| `ChaseNpc(Npc other)` | `boolean` | The same, at another creature - a guard sent at whatever wandered in, a beast set on one it would have ignored. False for a creature sent after itself. |
| `Forget()` | `boolean` | Lets go of whatever it was chasing, leaving it to its record's own behavior again. Harmless on a body that was chasing nothing. |
| `Behavior` | `string` | How it moves on its own: 'stationary', 'wander', 'pursue', 'flee', or 'scavenge'. ⚠ Five ways of WALKING and deliberately nothing about why - a reason is a property of the game, and 'hostile' means nothing in a world with no fighting in it. Empty once the body has left. |
| `Group` | `integer` | Which pack it keeps to, or zero for one in none. Two creatures sharing a group never notice each other, on top of never noticing their own kind - which is what keeps a pack from fighting itself. |
| `Range` | `integer` | How far it notices anything, in tiles, as its record was authored. Zero for a body that notices nobody. |
| `IsChasing` | `boolean` | Whether it is after somebody right now - one it noticed, or one you sent it after. The other half of Chase and Forget, which write and never read: without this a rule cannot tell a creature already in a fight from one standing idle. |

### World

Reached through its own name; there are no values of it.

| Written | Yields | What it does |
|---|---|---|
| `NpcAt(integer map, integer x, integer y)` | `Npc?` | The creature standing on that square, or nothing. A verb declared OnNpc arrives at OnAction with the square it was used on, and this is what turns that into the body. |
| `NpcsNear(integer map, integer x, integer y, integer tiles)` | `Npc[]` | Every creature standing within that many tiles of the square, nearest first - which is what a rule about the bodies AROUND something starts from: guards answering a call, a herd that scatters when one of them is startled. ⚠ On that map only, so a body one tile over a border is close and is not in the answer; ask for each map to reach those. |
| `PlayerAt(integer map, integer x, integer y)` | `Player?` | The player standing on that square, or nothing. Answered before a creature when both somehow occupy one tile. |
| `Tell(string line)` | — | Says a line to everybody in the world. For the handful of things that are genuinely everyone's business - a season turning, somebody finishing what only one person can finish. A game that announces ordinary events this way has an unreadable chat log. |
| `TellOn(integer map, string line)` | — | Says a line to everybody who can SEE that map, which is the nearest thing a seamless world has to a room. Not everybody standing on it: somebody on the next map along is looking at this one. |
| `TellNear(integer map, integer x, integer y, string line)` | — | Says a line to everybody within earshot of a square - the tighter audience, the one that hears speech rather than the one that can see the region. |
| `Stain(integer map, integer x, integer y, integer size, integer amount)` | — | Marks the ground, which dries on its own and is drawn to everyone who can see the tile. Amount is 0 to 100. ⚠ Unlike a burst, this LASTS - it is the one worldspace mark a game makes that is still there when somebody walks back. Its color is the world's own, set once rather than per stain. |
| `TellThese(Player[] them, string line)` | — | Says a line to a set of players, wherever they are. Anybody in it who has left the world is skipped rather than refused: a set gathered a moment ago is a set somebody may have logged out of. |
| `Guildmates(Player who)` | `Player[]` | Everybody IN THE WORLD who shares their guild, including them. Empty for somebody in no guild - which is not the same as a guild with nobody online, and World.Guild is what tells those apart. |
| `Party(Player who)` | `Player[]` | Everybody in their party, including them. Empty for somebody in no party. |
| `TileAt(integer map, integer x, integer y)` | `string` | What kind of ground is there: 'walkable', 'blocked', 'warp', 'item', 'npcavoid', 'door', 'plate', or 'ramp'. Empty for a square that is not on a real map. ⚠ The GROUND layer - a bridge deck is a different answer at the same coordinates. |
| `CanSee(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `boolean` | Whether a straight line between two squares crosses nothing that stops sight. 🔴 The ENGINE'S own trace, which is the same one the client colors its target arrow with - so a rule gating on this agrees with what the player was shown rather than nearly agreeing. A wall stops sight only if it was authored to: a railing is blocked to walk through and clear to see through. |
| `Distance(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `integer` | How far apart two squares are, in tiles, COUNTING ACROSS MAP BORDERS. 🔴 The world scrolls contiguously, so a body one tile over a border is one tile away - and arithmetic on the coordinates says it is on another map and unreachable. Every range rule wants this rather than subtraction. -1 when the two are too far apart to compare. |
| `Weather(integer map)` | `string` | What the sky is doing over that map: 'clear', 'rain', 'snow', 'heatwave', or 'heavywind'. Empty for a map that is not there. |
| `GuildOf(Player who)` | `integer` | Which guild they belong to, as its number, or zero for none. 🔴 The NUMBER, because a guild is a record rather than a body: it has no place, nothing walks it, and it outlives every member. Player.Guild answers with the NAME, which is what a player reads rather than what a rule keys on. |
| `GuildNamed(string name)` | `integer` | The guild with that name, or zero. Case-insensitive, the way the engine's own founding check compares - so a rule acting on a name a player typed asks the same question the engine did when it refused a second guild by that name. |
| `GuildName(integer guild)` | `string` | What that guild is called, or empty for a number naming none. |
| `GuildRank(Player who)` | `string` | What rank they hold: 'leader', 'officer', 'member', or empty for somebody in no guild. The engine keeps the rank and moves it; what a rank may DO is yours. |
| `GuildNumber(integer guild, string key)` | `integer` | One of the values YOUR GAME hangs on a guild - a war, a level, a season score. Zero for a key it does not carry, and for a number naming no guild. |
| `GuildText(integer guild, string key)` | `string` | The same, as text. |
| `SetGuildNumber(integer guild, string key, integer amount)` | — | Writes one of them, and gets the guild onto disk. ⚠ Saved on EVERY write, because a guild is not a body: nothing logs it out, so there is no later moment where its values would be written anyway. |
| `SetGuildText(integer guild, string key, string value)` | — | The same, with text. |
| `GuildMembers(integer guild)` | `Player[]` | Everybody IN THE WORLD who belongs to that guild. Empty for one with nobody online, which is not the same as a guild that is not there. |
| `GuildGold(integer guild)` | `integer` | What is in that guild's vault. |
| `GiveGuildGold(integer guild, integer amount)` | — | Puts gold into a vault. The counterpart to SpendGuildGold: a game holding gold aside - an escrow, a stake, a bond - has to be able to give it back. |
| `WornBy(Player who)` | `integer[]` | What they are wearing, as item numbers, in slot order. Empty for somebody wearing nothing. |
| `WornIn(Player who, string slot)` | `integer` | What they are wearing in ONE slot, by item number. Zero for an empty slot, and for a slot this world does not declare. A rule about one place on the body asks this rather than walking the list - whether a shield is up, whether a hand is free. |
| `DurabilityLeft(Player who, integer item)` | `integer` | How much wear is left in the copy they are WEARING. Zero when they are not wearing one - two copies in a bag are two different amounts of wear, and this means the one that was on them. |
| `DurabilityFull(Player who, integer item)` | `integer` | How much that item holds when new. Zero for one with no durability at all, which is an ordinary thing for an item to be. |
| `WearOut(Player who, integer item, integer points)` | `integer` | Wears out that many points of the copy they are wearing, never past nothing. Yields how many were actually taken, which is fewer than asked for when it was nearly worn out. ⚠ An item worn to nothing is NOT destroyed: it stays in the bag, unusable, until it is repaired. |
| `RepairCost(integer item, integer points)` | `integer` | What repairing that many points of that item costs, by the engine's own repair rate - the same rate a repair shop charges, so a game pricing wear agrees with the shop. |
| `RegionOf(integer map)` | `integer` | Which map group that map belongs to, or zero. A group is the engine's idea of a region: several maps sharing a name and some settings. A game that owns regions asks this to turn where somebody is standing into which region it is. Read what your game hangs on one with World.Record("MapGroups", ...). |
| `MapNumber(integer map, string field)` | `integer` | One of YOUR OWN fields on a map, as a whole number - a field you added with Records.Extend("Maps"). The map's own value, and its region's where the map leaves it unset, which is how every property a map inherits already works. Zero where neither carries it. |
| `MapText(integer map, string field)` | `string` | The same, as text. |
| `MapTruth(integer map, string field)` | `boolean` | The same, as a yes or no. An unticked box and an absent one are the same answer. |
| `SetRecordNumber(string records, integer number, string field, integer amount)` | — | Writes one of YOUR OWN fields on a record, and saves it. Where a game keeps what belongs to no body and no guild: the last day it settled accounts, a season number, who holds a territory. ⚠ Your own fields only - the engine's properties are written through their own paths, which normalize what they are given. |
| `SetRecordText(string records, integer number, string field, string value)` | — | The same, with text. |
| `Now()` | `integer` | The time now, in seconds since 1970, UTC. What anything dated needs: a cooldown that has to survive a restart, a window that stays open for an hour, a daily reset. Counting ticks answers a different question, since ticks stop when the server does. |
| `SpendGuildGold(integer guild, integer amount, Player by)` | `boolean` | Takes gold out of a vault, recording who spent it. 🔴 Through the engine's own LEDGER rather than by writing the number: a vault that went down with nothing in the spending log is money a guild cannot account for, and accounting for it is most of what a vault is for. False when the vault does not hold that much, which makes this the check as well as the payment. |
| `Records(string records)` | `integer` | How many records of that kind this world holds, counting blank slots. Zero for a kind nobody declared. |
| `Record(string records, integer number, string field)` | `string` | One field of one record, as text, or empty where the slot or the field is not there. What a game reads at run time out of the records its own editor authored. |
| `RecordNumber(string records, integer number, string field)` | `integer` | The same, as a whole number. Zero where the slot or the field is not there. |
| `RecordName(string records, integer number)` | `string` | What a record is CALLED - an item's name, a creature's, a map's. The one property of the engine's own a record can be asked for, because a rule that PICKS a record rather than being handed one has to be able to say which. Blank for a slot nobody authored. For your own records it is their name field. |
| `RecordTruth(string records, integer number, string field)` | `boolean` | The same, as a yes or no - which is what a checkbox on the editor's form writes. False where the slot or the field is not there, and an unticked box is the same answer as an absent one. |

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
