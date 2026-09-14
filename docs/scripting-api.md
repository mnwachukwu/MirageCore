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
| `public function OnLoot(Spoils drop)` | a creature is about to drop one line of its table, and this is the game's say over what that line is worth. Called once PER LINE, before the roll, so a table of three things raises it three times. Write on what you are handed: drop.Rolls changes how often it lands, drop.Yields how much of it there is, drop.ClaimedBy who may pick it up and for how long. ⚠ The body is still on its tile while this runs and stops being there immediately afterwards, so read it now rather than keeping it |
| `public integer function OnLinger(Player who)` | their connection dropped; yield how many seconds the body stays in the world |
| `public function OnMessage(Player who, string message, Values values)` | a client sent one of this game's own messages, carrying the fields its model declared |
| `public function OnNpcSpawned(Npc it)` | a creature has just come into the world and is already standing on its tile. 🔴 This is where a creature GETS ITS NUMBERS: Core spawns a body carrying a copy of its template and has never heard of health or of what one is worth to kill, so a game with either writes them on here. It is also the only place a fresh body can be told apart from the one before it, so anything that varies per spawn - a champion, a night-time boost - is decided here. ⚠ Raised for EVERY arrival: the respawn clock, a chase guest coming home, and a map being refilled |
| `public function OnContact(Npc it, Player who)` | a creature reached the player it was chasing. ⚠ Only for a PLAYER quarry — the parameter says Player, and a handler handed the wrong kind of body is worse than one that is not called. A creature that reached another creature raises OnNpcContact |
| `public function OnNpcContact(Npc it, Npc other)` | a creature reached the creature it was chasing. Creatures notice each other on their own — anything not its own kind and not sharing its group, within its range — so a world with two hostile species needs nothing but this handler to make them fight |
| `public string function OnMayUse(Player who, integer item, integer slot)` | somebody is about to use something out of their bag; yield a reason to stop it, or blank to let it happen. Asked BEFORE anything happens, unlike OnItemUsed: a rule told afterwards can only take the gear off again, and the player sees a flicker. A refusal costs them neither the item nor the beat |
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
| `Account` | `string` | The account behind them - the ONE name here that outlives a session. A handle stops meaning anything the moment they log out and a character can be deleted; this is what the engine files mail and guild membership under, so it is what to write down when the thing you are promising will be settled later. ⚠ NOT a character name and NOT for showing to players: it is how they sign in. Print Name in anything anybody reads. |
| `Map` | `integer` | Which map they are standing on, or zero when they are nowhere. |
| `X` | `integer` | How far across that map they are. |
| `Where` | `Spot` | The square they are standing on, as a value - map, tile and PLANE together. What to hand anything that puts something where somebody is, because a bridge and the water under it are the same three numbers and two different places. |
| `Y` | `integer` | How far down it. |
| `Has(string key)` | `boolean` | Whether they carry that key at all, so absence is told from zero. |
| `Number(string key)` | `integer` | What they carry under that key, or zero where they carry nothing. |
| `Text(string key)` | `string` | The same, as text, or empty where they carry nothing. |
| `SetNumber(string key, integer amount)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string key, string value)` | — | The same, with text. |
| `WarpTo(integer map, integer x, integer y)` | `boolean` | Puts them on that map, x and y. False for a square that is not a real tile. |
| `Give(integer item, integer many)` | — | Puts that many of an item in their bag. |
| `Take(integer item, integer many)` | — | Takes that many out of it, worn ones included. |
| `Carrying(integer item)` | `integer` | How many of that item they are carrying, worn ones and every stack counted together. The read that goes with Give and Take: a rule charging somebody in a currency of its own asks this first, because taking more than they have and taking what they have look the same afterwards. |
| `Wear(integer item)` | `boolean` | Puts something they are carrying on, taking off whatever was in its slot. A game that hands somebody a sword has no other way to put it in their hand - an opening kit, a quest reward, a curse that arms them against their will. ⚠ None of the refusals a player pressing the button meets apply: a rule arming somebody mid-fight meant to. False for a body not carrying it, and for a piece naming a slot this world does not declare. |
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
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, the way they are facing. True flings sparks with it, so the swing reads as having hit something rather than passing through air. What the crescent means is yours: a sword, a claw, a thrown net. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at a creature: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at another player: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. ⚠ Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes them out of the world, with a cause OnMayDie can read. False where something refused to let them die, which is what a death policy may do. |
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
| `AskAtCreation(string key, string caption, string records)` | — | Something to ask BEFORE a character exists - a class, a bloodline, a starting town. The one question a game cannot ask any other way: everything else it wants to know it asks of a body already in the world, and what a character is has to be settled before there is one. The player picks from the records you name, listed by their own names, and the number they picked is written onto them under your key before OnPlayerJoined runs, so you read it the ordinary way and need no handler. ⚠ A blank record is not offered, because a list of unnamed slots is a screen nobody can use. |
| `Panel(string id, string title, integer width, integer height)` | `Panel` | A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, so its rows and its buttons are written underneath it. |

### Records

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Are(string plural, string singular, integer limit)` | — | What these records are called in the editor — the plural, then the singular — and how many there may be. A caption left blank keeps the model's own name. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Extend(string records)` | — | Adds this model's fields to records that already exist rather than declaring a kind of its own - the engine's 'Items' or 'NPCs', or another module's. This is how a game hangs its own facts on a record the engine owns: which classes may wield a sword, which spell is written on a scroll. An item's own properties are a closed set because Core cannot act on one it has never heard of; yours are open, and they belong on the sword rather than in a table beside it. ⚠ What these records are called, where they live and how many there may be are the other family's answers, so Are and Stored say nothing here. |
| `Stored(string folder, string prefix)` | — | Where these records live: the folder under the world, and what each file is called before its number. Only needed for records already on disk. A new game leaves it out, and the model's name decides. |
| `Caption(string field, string caption)` | — | What one field is called on the form. Only needed where the field's own name is not the words an author should read. |
| `Range(string field, integer least, integer greatest)` | — | The bounds of a whole-number field. Equal bounds mean unbounded. |
| `Length(string field, integer characters)` | — | How long a text field may be. Zero means no limit. |
| `Points(string field, string records)` | — | Makes a whole-number field a picker over another kind of record, listing them by name. Use it to point at the engine's own records - 'Items', 'NPCs', 'Maps', 'Shops', 'Conversations' - which have no model to type a field as. A field typed as one of your own models already does this and needs nothing here. ⚠ The number is still what is stored: this changes what the form draws and nothing about what a rule reads back. |

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
| `Where` | `Spot` | The square it is standing on, as a value - map, tile and PLANE together. What to hand anything that puts something where a body is, because a bridge and the water under it are the same three numbers and two different places. |
| `Y` | `integer` | How far down it. |
| `Has(string key)` | `boolean` | Whether it carries that key at all, so absence is told from zero. |
| `Number(string key)` | `integer` | What it carries under that key, or zero where it carries nothing. |
| `Text(string key)` | `string` | The same, as text, or empty where it carries nothing. |
| `SetNumber(string key, integer amount)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string key, string value)` | — | The same, with text. |
| `Float(string line, integer red, integer green, integer blue)` | — | Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. ⚠ The one place a script asks the client to DRAW: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state. |
| `IsEngaged` | `boolean` | Whether they are in a fight right now. What being in one MEANS is yours - holding regenthrough it, refusing a warp out of it - and this is the clock you set, read back. |
| `IsMarked` | `boolean` | Whether they carry a mark right now. |
| `IsAggressor` | `boolean` | Whether they are flagged as having started it. |
| `IsWaiting` | `boolean` | Whether they are still held off acting. ⚠ Measured FORWARD from when the cooldownstarted, which is the direction the bar drawing it measures. |
| `Engage(integer seconds)` | — | Marks it as in a fight for that many seconds, which brings up its overhead bars. What being in a fight means is the game's; the engine keeps the clock. |
| `Mark(integer seconds)` | — | Marks it for that many seconds - a flag a game puts on a body and reads back later, which is how a kill gets claimed by whoever earned it. |
| `Flag(integer seconds)` | — | Marks it as the one that started it, for that many seconds. |
| `Wait(integer seconds)` | — | Holds it off acting again for that many seconds. |
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, the way they are facing. True flings sparks with it, so the swing reads as having hit something rather than passing through air. What the crescent means is yours: a sword, a claw, a thrown net. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at a player: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at another creature: 'bolt', 'glitter' or 'parcel', and a color. ⚠ A number floated at the same target waits until it LANDS, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. ⚠ Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes it out of the world, with a cause the death policy can read. False for a body that was not there, or that something refused to let die. |
| `Kind` | `integer` | Which creature it is a copy of - the number of its record in this world's creatures. ⚠ What a rule about a species keys on: a bounty per creature, a drop table, which bodies a quest counts. Name answers with what a player READS, and two records may share a name, so a rule written against one silently follows an editor rename. Zero for a body that has left the world. |
| `Chase(Player who)` | `boolean` | Sends it after a player, whether or not it would ever have noticed them itself - which is how a creature that only fights back gets written: author it to amble, and chase whoever hits it. It commits to the approach rather than walking in, because a body that was SENT is not deciding whether to be interested. ⚠ It overrides the noticing, not the legs: one authored to hold its tile still holds it, and one authored to open the gap runs from them instead. False where either body has left the world. |
| `ChaseNpc(Npc other)` | `boolean` | The same, at another creature - a guard sent at whatever wandered in, a beast set on one it would have ignored. False for a creature sent after itself. |
| `Forget()` | `boolean` | Lets go of whatever it was chasing, leaving it to its record's own behavior again. Harmless on a body that was chasing nothing. |
| `Behavior` | `string` | How it moves on its own: 'stationary', 'wander', 'pursue', 'flee', or 'scavenge'. ⚠ Five ways of WALKING and deliberately nothing about why - a reason is a property of the game, and 'hostile' means nothing in a world with no fighting in it. Empty once the body has left. |
| `Group` | `integer` | Which pack it keeps to, or zero for one in none. Two creatures sharing a group never notice each other, on top of never noticing their own kind, so a pack does not fight itself. |
| `Range` | `integer` | How far it notices anything, in tiles, as its record was authored. Zero for a body that notices nobody. |
| `IsChasing` | `boolean` | Whether it is after somebody right now - one it noticed, or one you sent it after. The other half of Chase and Forget, which write and never read: without this a rule cannot tell a creature already in a fight from one standing idle. |

### World

Reached through its own name; there are no values of it.

| Written | Yields | What it does |
|---|---|---|
| `NpcAt(integer map, integer x, integer y)` | `Npc?` | The creature standing on that square, or nothing. A verb declared OnNpc arrives at OnAction with the square it was used on, and this is what turns that into the body. |
| `NpcsNear(integer map, integer x, integer y, integer tiles)` | `Npc[]` | Every creature standing within that many tiles of the square, nearest first - which is what a rule about the bodies AROUND something starts from: guards answering a call, a herd that scatters when one of them is startled. ⚠ On that map only, so a body one tile over a border is close and is not in the answer; ask for each map to reach those. |
| `NpcsOn(integer map)` | `Npc[]` | Every creature standing on that map. The whole population rather than a neighborhood, which is what a SWEEP asks for and NpcsNear cannot answer: telling every body that night fell, counting what is still alive, clearing something a spell left behind. ⚠ Visitors on the map are in and natives away chasing elsewhere are out, so a body appears exactly once across a walk of every map. |
| `PlayersNear(integer map, integer x, integer y, integer tiles)` | `Player[]` | Every player standing within that many tiles of the square, nearest first - the mirror of NpcsNear, and what a rule about the PEOPLE around something starts from: who shared a kill, who heard a shout, who was standing too close. ⚠ On that map only, so somebody one tile over a border is close and is not in the answer. |
| `PlayerAt(integer map, integer x, integer y)` | `Player?` | The player standing on that square, or nothing. Answered before a creature when both somehow occupy one tile. |
| `Tell(string line)` | — | Says a line to everybody in the world. For the handful of things that are genuinely everyone's business - a season turning, somebody finishing what only one person can finish. A game that announces ordinary events this way has an unreadable chat log. |
| `TellOn(integer map, string line)` | — | Says a line to everybody who can SEE that map, which is the nearest thing a seamless world has to a room. Not everybody standing on it: somebody on the next map along is looking at this one. |
| `TellNear(integer map, integer x, integer y, string line)` | — | Says a line to everybody within earshot of a square - the tighter audience, the one that hears speech rather than the one that can see the region. |
| `Stain(integer map, integer x, integer y, integer size, integer amount)` | — | Marks the ground, which dries on its own and is drawn to everyone who can see the tile. Amount is 0 to 100. ⚠ Unlike a burst, this LASTS - it is the one worldspace mark a game makes that is still there when somebody walks back. Its color is the world's own, set once rather than per stain. |
| `TellThese(Player[] them, string line)` | — | Says a line to a set of players, wherever they are. Anybody in it who has left the world is skipped rather than refused: a set gathered a moment ago is a set somebody may have logged out of. |
| `Guildmates(Player who)` | `Player[]` | Everybody IN THE WORLD who shares their guild, including them. Empty for somebody in no guild - which is not the same as a guild with nobody online, and World.Guild is what tells those apart. |
| `Party(Player who)` | `Player[]` | Everybody in their party, including them. Empty for somebody in no party. |
| `TileAt(integer map, integer x, integer y)` | `string` | What kind of ground is there: 'walkable', 'blocked', 'warp', 'item', 'npcavoid', 'door', 'plate', or 'ramp'. Empty for a square that is not on a real map. ⚠ The GROUND layer - a bridge deck is a different answer at the same coordinates. |
| `CanSee(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `boolean` | Whether a straight line between two squares crosses nothing that stops sight. The engine runs the same trace the client colors its target arrow with, so a rule gating on this agrees with what the player was shown rather than nearly agreeing. A wall stops sight only if it was authored to: a railing is blocked to walk through and clear to see through. |
| `Distance(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `integer` | How far apart two squares are, in tiles, counting across map borders. The world scrolls contiguously, so a body one tile over a border is one tile away, where arithmetic on the coordinates calls it another map and unreachable. Every range rule wants this rather than subtraction. -1 when the two are too far apart to compare. |
| `TimeOfDay()` | `string` | What time of day it is - 'day', 'dusk', 'night' or 'dawn'. The engine runs the cycle and the client paints it, and a game with anything that is different after dark has nothing to read otherwise: creatures that hunt at night, a shop that shuts, a spell that only works under a moon. ⚠ One answer for the WHOLE WORLD, unlike the weather - a cycle the server runs rather than a property of a place. |
| `Weather(integer map)` | `string` | What the sky is doing over that map: 'clear', 'rain', 'snow', 'heatwave', or 'heavywind'. Empty for a map that is not there. |
| `GuildOf(Player who)` | `integer` | Which guild they belong to, as its number, or zero for none. The number rather than the guild is a record rather than a body: it has no place, nothing walks it, and it name, because a guild outlives every member. Player.Guild answers with the name, which reads rather than what a rule keys on. |
| `GuildNamed(string name)` | `integer` | The guild with that name, or zero. Case-insensitive, the way the engine's own founding check compares - so a rule acting on a name a player typed asks the same question the engine did when it refused a second guild by that name. |
| `GuildName(integer guild)` | `string` | What that guild is called, or empty for a number naming none. |
| `Access(Player who)` | `string` | What their account may do to the world: 'player', 'monitor', 'mapper', 'developer' or 'creator'. Whoever runs a world usually wants its staff outside its rules - no fighting, no loot, no place on a ladder - and which rules that means is yours to decide. Empty for a body that is not here. |
| `GuildRank(Player who)` | `string` | What rank they hold: 'leader', 'officer', 'member', or empty for somebody in no guild. The engine keeps the rank and moves it; what a rank may DO is yours. |
| `GuildNumber(integer guild, string key)` | `integer` | One of the values YOUR GAME hangs on a guild - a war, a level, a season score. Zero for a key it does not carry, and for a number naming no guild. |
| `GuildText(integer guild, string key)` | `string` | The same, as text. |
| `SetGuildNumber(integer guild, string key, integer amount)` | — | Writes one of them, and gets the guild onto disk. ⚠ Saved on EVERY write, because a guild is not a body: nothing logs it out, so there is no later moment where its values would be written anyway. |
| `SetGuildText(integer guild, string key, string value)` | — | The same, with text. |
| `GuildMembers(integer guild)` | `Player[]` | Everybody IN THE WORLD who belongs to that guild. Empty for one with nobody online, which is not the same as a guild that is not there. |
| `Guilds()` | `integer[]` | Every guild there is, by number. ⚠ WHAT ANYTHING RANKED STARTS FROM: every other guild call takes a number you already had, off a body or off a name, and a standing, a league table or a sweep over all of them has none. Counting upward and hoping does not work either - a guild that disbanded leaves a hole in the numbering. |
| `WhoIs(string account)` | `Player?` | Whoever is signed in to that account right now, or nothing. The way back: a rule that wrote an account down reaches the person again with this, and nothing is the answer that says to POST rather than to tell. |
| `AccountsIn(integer guild)` | `string[]` | Every account in a guild, SIGNED IN OR NOT. ⚠ A guild's roster outlives its members' sessions, and World.GuildMembers answers only with the part of it that is here. Anything about the guild rather than about the people in front of you starts from this - a dividend, a census, a rule about who has stopped turning up. |
| `IsActiveIn(integer guild, string account)` | `boolean` | Whether that account is a LIVE member of the guild rather than a name on its roster: signed in for long enough, recently enough, by the engine's own measure. What to ask before counting somebody - who votes, who makes a quorum, who is worth counting when a guild is sized up. |
| `MailTo(string account, string subject, string body)` | `boolean` | Sends an ACCOUNT a letter, whether or not anybody is signed in to it. What World.Mail cannot do: reach somebody who is not here. A rule that wrote an account down when it had the person settles up afterwards, and they find it waiting. |
| `MailItemTo(string account, integer item, integer many, string subject, string body)` | `boolean` | The same, with something attached. A sale settled, a refund, a prize drawn while they were away. |
| `Mail(Player who, string subject, string body)` | `boolean` | Sends them a letter. ⚠ THE ONE THING A RULE CAN SAY THAT OUTLIVES THE MOMENT: a line of chat is gone when they log out, and a letter waits - through a logout, a restart, and a server that was down for a week. |
| `MailItem(Player who, integer item, integer many, string subject, string body)` | `boolean` | The same, with something attached, and the thing waits with it. What a reward that was EARNED rather than picked up looks like - a refund, a prize, a delivery, the rest of a payout that would not fit in a bag. Player.Give is the other one, and it needs room in the bag right now. |
| `MailMembers(integer guild, integer item, integer many, string subject, string body, boolean onlyActive)` | `integer` | Sends every member of a guild that item, and REACHES THE ONES WHO ARE NOT HERE. The only way to pay somebody offline: everything else reaches a body in the world, and what a GROUP earned is owed to its members whether or not they happened to be logged in. It arrives as mail, so it waits for them. 'onlyActive' narrows it to members who have really been playing, by the engine's own measure of a live roster - a payout split among a hundred names nobody has used is a payout nobody feels. Yields how many it reached. |
| `GuildGold(integer guild)` | `integer` | What is in that guild's vault. |
| `GiveGuildGold(integer guild, integer amount)` | — | Puts gold into a vault. The counterpart to SpendGuildGold: a game holding gold aside - an escrow, a stake, a bond - has to be able to give it back. |
| `WornBy(Player who)` | `integer[]` | What they are wearing, as item numbers, in slot order. Empty for somebody wearing nothing. |
| `BagOf(Player who)` | `integer[]` | Which bag slots they have something in, in slot order. Carrying asks about an ITEM; this asks about SLOTS, which is what a rule about somebody's bag needs - two copies of one sword are two slots and two amounts of wear. |
| `ItemInSlot(Player who, integer slot)` | `integer` | What is in that bag slot, by item number. Zero for a slot holding nothing. |
| `CountInSlot(Player who, integer slot)` | `integer` | How many that bag slot holds: the stack size for something that stacks, and one for anything else. Zero for a slot holding nothing. |
| `SlotIsWorn(Player who, integer slot)` | `boolean` | Whether that bag slot holds the copy they are wearing. A rule about what a death scatters asks this, because worn gear and carried gear are dropped by different rules in most games. |
| `DropSlot(Player who, integer slot, integer many)` | `boolean` | Puts what is in that bag slot on the ground where they are standing. 'many' takes part of a stack; zero takes the whole slot. It lands as a player drop, so anyone may pick it up and the world's own limit on litter applies. False for an empty slot. |
| `WornIn(Player who, string slot)` | `integer` | What they are wearing in ONE slot, by item number. Zero for an empty slot, and for a slot this world does not declare. A rule about one place on the body asks this rather than walking the list - whether a shield is up, whether a hand is free. |
| `DurabilityLeft(Player who, integer item)` | `integer` | How much wear is left in the copy they are WEARING. Zero when they are not wearing one - two copies in a bag are two different amounts of wear, and this means the one that was on them. |
| `DurabilityFull(Player who, integer item)` | `integer` | How much that item holds when new. Zero for one with no durability at all, which is an ordinary thing for an item to be. |
| `WearOut(Player who, integer item, integer points)` | `integer` | Wears out that many points of the copy they are wearing, never past nothing. Yields how many were actually taken, which is fewer than asked for when it was nearly worn out. ⚠ An item worn to nothing is NOT destroyed: it stays in the bag, unusable, until it is repaired. |
| `RepairCost(integer item, integer points)` | `integer` | What repairing that many points of that item costs, by the engine's own repair rate - the same rate a repair shop charges, so a game pricing wear agrees with the shop. |
| `DropAt(Spot where, integer item, integer many)` | `boolean` | Puts an item on that square out of nowhere, free to whoever reaches it first. Not out of anybody's bag - a chest that opens, a reward left where a quest ended, a hoard a rule rolled for itself. Player.Drop is the other one, and it moves something that already exists. |
| `DropClaimed(Spot where, integer item, integer many, Player who, integer seconds)` | `boolean` | The same, held for one player for that many seconds: nobody else may pick it up until the time runs out, and the client shows them whose it is. What stops the person who did the work watching somebody else walk off with it. |
| `RepairRate(integer tier)` | `real` | Gold one point of durability costs on ON-TIER gear at that tier, priced off a reference piece rather than off anything anybody is holding. Fractional on purpose: near the bottom of the ladder a point is worth a fraction of a coin. A game charging upkeep in something that is not durability - a reagent, a charge, a ration - prices it against this, so its number follows the repair shop instead of drifting away from it silently. |
| `RegionOf(integer map)` | `integer` | Which map group that map belongs to, or zero. A group is the engine's idea of a region: several maps sharing a name and some settings. A game that owns regions asks this to turn where somebody is standing into which region it is. Read what your game hangs on one with World.Record("MapGroups", ...). |
| `ExitMap(integer map)` | `integer` | Where this map puts somebody who leaves it other than by walking, as a map number, or zero for one that names none. The map author's say over where leaving this place lands you; the engine consults it for nothing on its own, so a game reads it and outranks it as it sees fit. Its region answers for a map that says nothing. |
| `ExitX(integer map)` | `integer` | How far across that exit is. |
| `ExitY(integer map)` | `integer` | And how far down it. |
| `MapNumber(integer map, string field)` | `integer` | One of YOUR OWN fields on a map, as a whole number - a field you added with Records.Extend("Maps"). The map's own value, and its region's where the map leaves it unset, which is how every property a map inherits already works. Zero where neither carries it. |
| `MapText(integer map, string field)` | `string` | The same, as text. |
| `MapTruth(integer map, string field)` | `boolean` | The same, as a yes or no. An unticked box and an absent one are the same answer. |
| `SetRecordNumber(string records, integer number, string field, integer amount)` | — | Writes one of YOUR OWN fields on a record, and saves it. Where a game keeps what belongs to no body and no guild: the last day it settled accounts, a season number, who holds a territory. ⚠ Your own fields only - the engine's properties are written through their own paths, which normalize what they are given. |
| `SetRecordText(string records, integer number, string field, string value)` | — | The same, with text. |
| `Now()` | `integer` | The time now, in seconds since 1970, UTC. What anything dated needs: a cooldown that has to survive a restart, a window that stays open for an hour, a daily reset. Counting ticks answers a different question, since ticks stop when the server does. |
| `Spot(integer map, integer x, integer y)` | `Spot` | That square on the ground, as a value - so a rule can carry it around, keep a set of them, and hand it to anything that puts something somewhere. |
| `SpotRaised(integer map, integer x, integer y)` | `Spot` | The same square on the RAISED surface rather than on the ground - a bridge, a ledge, a gantry, whatever a world built up there. The two are one tile and two places. |
| `SpreadOver(integer region, integer count, string onlyWhere)` | `Spot[]` | That many squares spread across a region, every one reachable ON FOOT from every other. ⚠ Measured by WALKING, across the region's seams - not in a straight line, which is a lie wherever a wall or water stands between two tiles that are near on paper, and not by map number, which piles everything into whichever corner was drawn first. 'onlyWhere' names one of YOUR OWN truth fields on Maps and a square goes only on a map carrying it; blank puts one anywhere in the region. The walk crosses the whole region either way, so a town in the middle of one is walked THROUGH. Fewer than asked for means there was nowhere else to put one. |
| `Empty(integer map)` | `boolean` | Takes every creature off that map and keeps it that way. For ground that has to stop being ordinary for a while: a war fought over it, a ritual nobody should interrupt, an arena cleared for a duel. Nothing comes back until World.Wake - which is what separates this from clearing a map and watching it refill a minute later. |
| `Refill(integer map)` | `boolean` | Lets it hold creatures again, and puts its own back at once rather than leaving it bare until each slot's clock comes round. |
| `IsEmptied(integer map)` | `boolean` | Whether that map is being kept empty of creatures. |
| `Marker(string id, Spot where)` | `Marker` | Puts a mark on that square and hands it back, so its ring, its label, its meter and who sees it are each a line of their own. The twin of an overhead bar, for a PLACE. Marking again under a name already used REPLACES what is there, which is how a mark moves and how its meter counts - one call rather than a remove and a place. |
| `Unmark(string id)` | `boolean` | Takes one away by name. False when nothing was under it, which is an ordinary answer for a rule clearing up after something that ended on its own. |
| `InsideMark(string id, Spot where)` | `boolean` | Whether that square is inside the mark's ring. ⚠ ASK THIS rather than doing the arithmetic: the ring a player can see and the ring a rule scores are then the same mark, and two answers drifting apart is invisible - the line on the screen would sit somewhere other than the line that counts. False for a mark with no ring, and for another map. |
| `LocalOffset()` | `integer` | How far the server's own civil day is from UTC right now, in seconds - east of it positive, west of it negative. ⚠ ANYTHING THAT TURNS OVER AT MIDNIGHT wants this: a daily reset, a weekly tax, a season all mean the operator's own midnight, and dividing World.Now by a day gives the wrong one everywhere but Greenwich. Add it before dividing. Read fresh, so a place that keeps summer time answers differently in July than in January - which is what keeps a boundary at midnight all year. |
| `Number(string key)` | `integer` | One of YOUR OWN values about the world itself, as a whole number, or zero for one never written. The place for what belongs to no body and no record - which season it is, whether an event is running, how many times something has happened. Read back as it was left when the server starts again. |
| `Text(string key)` | `string` | The same, as text. Empty for one never written. |
| `Truth(string key)` | `boolean` | And as a yes or no. False for one never written. |
| `SetNumber(string key, integer amount)` | — | Writes one. Kept until the world is next written, which the engine does on its own cadence and at shutdown. |
| `SetText(string key, string value)` | — | The same, as text. |
| `SetTruth(string key, boolean value)` | — | And as a yes or no. |
| `SpendGuildGold(integer guild, integer amount, Player by)` | `boolean` | Takes gold out of a vault, recording who spent it. Through the engine's own ledger rather than by writing the number: a vault that went down with nothing in the spending log is money a guild cannot account for. False when the vault does not hold that much, so this is the check as well as the payment. |
| `Records(string records)` | `integer` | How many records of that kind this world holds, counting blank slots. Zero for a kind nobody declared. |
| `Record(string records, integer number, string field)` | `string` | One field of one record, as text, or empty where the slot or the field is not there. What a game reads at run time out of the records its own editor authored. |
| `RecordNumber(string records, integer number, string field)` | `integer` | The same, as a whole number. Zero where the slot or the field is not there. |
| `RecordName(string records, integer number)` | `string` | What a record is CALLED - an item's name, a creature's, a map's. The one property of the engine's own a record can be asked for, because a rule that PICKS a record rather than being handed one has to be able to say which. Blank for a slot nobody authored. For your own records it is their name field. |
| `RecordTruth(string records, integer number, string field)` | `boolean` | The same, as a yes or no, which is what a checkbox on the editor's form writes. False where the slot or the field is not there, and an unticked box gives the same answer as an absent one. |

### Values

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Has(string field)` | `boolean` | Whether the message carried that field at all, so absence is told from zero. |
| `Number(string field)` | `integer` | What it carried under that name, or zero where it carried nothing. |
| `Text(string field)` | `string` | The same, as text, or empty where it carried nothing. An enumeration field arrives as the member's own name. |
| `Truth(string field)` | `boolean` | The same, as a yes or no. False where it carried nothing. |

### Spot

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Map` | `integer` | Which map it is on. |
| `X` | `integer` | How far across. |
| `Y` | `integer` | And how far down. |
| `IsRaised` | `boolean` | Whether it is on the RAISED surface rather than on the ground - a bridge, a ledge, a gantry. ⚠ A bridge and the water under it are the same three numbers and two different places, so a rule that acts on a square asks this before deciding it knows where it is. |

### Marker

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Label(string text)` | — | Writes that over it. Left unsaid, a mark carries no label. |
| `Color(integer red, integer green, integer blue)` | — | What color it is drawn in. The pennant, the ring and the label all take it, so a side's own color is one line. |
| `Ring(integer tiles)` | — | Draws the ground within that many tiles of it. ⚠ Drawn as the STAIRCASE of tiles actually inside, never as a circle - so the line on the screen and World.InsideMark are one statement. Nothing at or below zero draws no ring. |
| `Meter(integer value, integer ceiling)` | — | A bar over it, that full out of that. A ceiling of nothing draws no meter, which is how a mark that is only a pin says so. |
| `SeenBy(Player[] them)` | — | Makes it private to those bodies. Left unsaid, everybody who can see the square sees it. Said AGAIN it adds them rather than replacing, so a mark for several sides is one call per side. ⚠ A SNAPSHOT rather than a rule: somebody who joins afterwards is not on it until the mark is placed again. A side whose members come and go says this each time it moves the mark, which it is doing anyway. |

### Spoils

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Item` | `integer` | What this line drops, by item number. |
| `Many` | `integer` | How many of it. ⚠ Only an item that STACKS reads this; anything else lands as one however large the number is. |
| `Chance` | `integer` | How often the line lands, as a plain percent - one in a hundred at 1, every time at 100 or more, never at nothing. |
| `Kind` | `integer` | Which creature this was a copy of. What a rule about a KIND of creature keys on, and it outlives the body - which is about to stop being there. |
| `From` | `Npc?` | The body itself, still standing on the tile it fell on. ⚠ For this call only: the slot is cleared the moment the table finishes rolling, and a handle kept past that answers IsHere with no. |
| `Killer` | `Player?` | Who killed it, or nothing when the world itself did. |
| `Rolls(integer chance)` | — | Makes the line land that often instead. Nothing at or below zero drops the line without rolling it at all, which is how a rule says this creature owes this player nothing. |
| `Yields(integer many)` | — | Makes it that many instead - a doubled purse, a halved one. Read only for an item that stacks, as above. |
| `ClaimedBy(Player who, integer seconds)` | — | Holds the drop for that player for that long: nobody else may pick it up until the time runs out, and the client shows them whose it is. What stops the person who did the work watching somebody else walk off with it. Left unsaid, a drop is free to whoever reaches it first. |

## What a module may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when
the module is read, not when it runs. A world folder is something one person hands to another,
and a module that could open a file could read the accounts beside it.

Everything else a script can name is on this page. There is no way to reach a type the engine
did not register.
