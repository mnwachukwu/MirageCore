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
| `public function OnAction(Player who, string action, string on, integer map, integer x, integer y, string picked)` | the player picked one of this module's own verbs; 'on' names the body it was used on, and is blank for a verb offered on a square or on the HUD; 'picked' is the line of a panel's list that was selected, and is blank everywhere else |
| `public function OnTick()` | the module's tick came round, however often game.TickEvery asked for |
| `public function OnPlayerTick(Player who)` | the same tick, once for each player in the world, which a script has no other way to walk |
| `public string function OnMayRun(Player who)` | somebody is about to take a step at a run; yield a reason to bring them down to a walk, or blank to let them run. Asked on every running step, so keep it to reading a number off the body |
| `public function OnRan(Player who)` | they took a step at a run, under their own power, onto a tile of the same map. What a run costs is charged here - a walk, a warp and a step across a map edge never reach it |
| `public string function OnMayDie(Player who, string cause)` | somebody is about to die; yield a reason to stop it, or blank to let it happen |
| `public function OnDied(Player who, Player killer, string cause)` | somebody died, and the body has not moved yet. What a death costs is written here - gear wear, a dropped bag, lost experience - and it runs while they are still lying where they fell, so anything shed lands on the tile they can go back for. 'killer' is nobody when the world itself did it, which Player.IsHere answers. Call Player.RespawnAt from in here to say where they come back |
| `public function OnRose(Player who)` | they got up, and are standing where they will actually be. The other end of being put out of action: Core puts NOTHING back, so a body comes back exactly as it fell unless this says otherwise - full pools, an empty bag, a penalty that lingers. Called after the move, so a rule writing onto them is writing onto the body in its new place |
| `public function OnLoot(Spoils drop)` | a creature is about to drop one line of its table, before the roll. Called once per line, so a table of three things calls it three times. Write on what you are handed: drop.Rolls sets how often the line lands, drop.Yields how much of it there is, and drop.ClaimedBy who may pick it up and for how long. The body leaves its tile the moment this returns, so read it now and do not hold on to it |
| `public integer function OnLinger(Player who)` | their connection dropped; yield how many seconds the body stays in the world |
| `public function OnMessage(Player who, string message, Values values)` | a client sent one of this game's own messages, carrying the fields its model declared |
| `public function OnNpcSpawned(Npc it)` | a creature has just come into the world and is already standing on its tile. This is where a creature gets its numbers: Core spawns a body carrying a copy of its template and has never heard of health or of what one is worth to kill, so a game with either writes them on here. It is also the only place a fresh body can be told apart from the one before it, so anything that varies per spawn - a champion, a night-time boost - is decided here. Raised for every arrival: the respawn clock, a chase guest coming home, and a map being refilled |
| `public function OnContact(Npc it, Player who)` | a creature reached the player it was chasing. Only for a player target — the parameter says Player, and a handler handed the wrong kind of body is worse than one that is not called. A creature that reached another creature raises OnNpcContact |
| `public function OnNpcContact(Npc it, Npc other)` | a creature reached the creature it was chasing. Creatures notice each other on their own — anything not its own kind and not sharing its group, within its range — so a world with two hostile species needs nothing but this handler to make them fight |
| `public string function OnMayUse(Player who, integer item, integer slot)` | somebody is about to use something out of their bag; yield a reason to stop it, or blank to let it happen. Asked before anything happens, unlike OnItemUsed: a rule told afterwards can only take the gear off again, and the player sees a flicker. A refusal costs them neither the item nor the beat |
| `public function OnItemUsed(Player who, integer item, integer slot)` | they used something out of their bag. Raised for every use, the engine's own two included: wearing a piece of gear and opening a door have already happened by the time this is called. Everything else an item might mean is a game's, and this is where it is written - a scroll that teaches, a potion that heals, a horn that is heard across the map |
| `public function OnPlayerWarped(Player who, integer fromMap, integer fromX, integer fromY)` | they arrived somewhere they did not walk to. A warp is not a step, so it raises nothing at OnPlayerMoved - a rule that watched only steps would miss every door, every teleport, and every respawn |

## The types

### Player

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Message(string line)` | — | Sends a line of text to this player, and to nobody else. |
| `Name` | `string` | Their character's name, trimmed. Blank once the body has left - the same answer a creature's name gives, because it is the same question. |
| `IsHere` | `boolean` | Whether they are still in the world. A handle outlives the body it names. |
| `Guild` | `string` | The name of the guild their account belongs to, or empty for none. |
| `Account` | `string` | The account behind them - the one name here that outlives a session. A handle stops meaning anything the moment they log out and a character can be deleted; this is what the engine files mail and guild membership under, so it is what to write down when the thing you are promising will be settled later. not a character name and not for showing to players: it is how they sign in. Print Name in anything anybody reads. |
| `Map` | `integer` | Which map they are standing on, or zero when they are nowhere. |
| `X` | `integer` | How far across that map they are. |
| `Where` | `Spot` | The square they are standing on, as a value - map, tile and plane together. What to hand anything that puts something where somebody is, because a bridge and the water under it are the same three numbers and two different places. |
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
| `Wear(integer item)` | `boolean` | Puts something they are carrying on, taking off whatever was in its slot. A game that hands somebody a sword has no other way to put it in their hand - an opening kit, a quest reward, a curse that arms them against their will. None of the refusals a player pressing the button meets apply: a rule arming somebody mid-fight meant to. False for a body not carrying it, and for a piece naming a slot this world does not declare. |
| `TakeOff(integer item)` | `boolean` | Takes something off. It stays in the bag. The other half of Wear, and what a rule about ruined gear needs: a piece worn down to nothing comes off the body it broke on. False for a body not wearing it. |
| `IsRunning` | `boolean` | Whether they are running or walking right now. What running costs is up to you; the engine moves the body, and this is how a rule finds out. |
| `Pace` | `integer` | How quick this body is. Zero is the baseline everybody starts at and higher is faster, with diminishing returns and a ceiling the engine picks - so a game with a speed stat writes the stat here and never has to know the curve. It buys a faster RUN; a walk is a walk. |
| `SetPace(integer pace)` | — | Says how quick they are. A game with a speed stat has no other way to make it mean anything: how far a body gets per second is the engine's, and this is what it reads. Write it whenever the stat behind it moves. |
| `RunMs` | `integer` | How long one tile takes them at a run, in thousandths of a second - what their Pace bought. Divide World.WalkMs by it to say how much faster running is. |
| `Engage(integer seconds)` | — | Marks them as in a fight for that many seconds. What being in a fight means is the game's; the engine keeps the clock and the client shows it. |
| `Down(integer seconds)` | — | Marks them as out of the fight for that many seconds. |
| `Mark(integer seconds)` | — | Marks them for that many seconds - a target somebody else's rule put a flag on. |
| `Flag(integer seconds)` | — | Marks them as the one who started it, for that many seconds. What that costs them is the game's to decide. |
| `Wait(integer seconds)` | — | Holds them off acting again for that many seconds. |
| `Float(string line, integer red, integer green, integer blue)` | — | Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. The one place a script asks the client to draw: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state. |
| `IsEngaged` | `boolean` | Whether they are in a fight right now. What being in one means is yours - holding regenthrough it, refusing a warp out of it - and this is the clock you set, read back. |
| `IsDowned` | `boolean` | Whether they are out of action right now. |
| `IsMarked` | `boolean` | Whether they carry a mark right now. |
| `IsAggressor` | `boolean` | Whether they are flagged as having started it. |
| `IsWaiting` | `boolean` | Whether they are still held off acting. Measured forward from when the cooldown started, matching the direction the bar drawing it fills. |
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, in the direction they are facing. Pass true to fling sparks with it, so the swing reads as connecting instead of passing through air. What the crescent depicts is up to you: a sword, a claw, a thrown net. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at a creature: 'bolt', 'glitter' or 'parcel', and a color. A number floated at the same target waits until it lands, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at another player: 'bolt', 'glitter' or 'parcel', and a color. A number floated at the same target waits until it lands, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes them out of the world, with a cause OnMayDie can read. False when something refused the death, which a death policy is allowed to do. |
| `RespawnAt(integer map, integer x, integer y)` | — | Says where this body comes back. Only from inside OnDied, and only about the body that died - it is read the moment that handler returns, and anywhere else it does nothing. Say nothing and they come back where the world puts them. |
| `Find(string name)` | `Player?` | The body behind a name, or nothing if no one is using it. OnAction hands you a name; this turns it into a player you can read and write. |

### Builder

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Attribute(string key, string seenBy)` | — | Declares a key this game counts, and who may see it: none, owner, or viewport. |
| `Message(string modelName)` | — | A message a client may send this game, taking its fields from one of this world's own models. It arrives at OnMessage with those fields as values. A stock client cannot compose one, so this is for a client, a tool, or a bot that knows it. |
| `TickEvery(integer ticks)` | — | How many ticks between calls to OnTick and OnPlayerTick. A tick is 100ms, so the default of 1 calls them ten times a second, 10 calls them once a second, and 600 calls them once a minute. This sets the rate for the whole module. |
| `EquipSlot(string key, string caption)` | — | A place on a body something can be worn. A caption left blank becomes the key, as words. |
| `Heading(string caption)` | — | A heading on the sidebar, separating the rows under it. |
| `Field(string key, string caption, integer red, integer green, integer blue)` | — | A sidebar row: a key read live off the player, a caption, and a color as red, green, and blue. All three zero leaves the color to the client. |
| `Badge(string key, string caption, integer red, integer green, integer blue)` | — | The same, drawn as a small tag with no caption. |
| `Meter(string key, string outOf, string caption, integer red, integer green, integer blue)` | — | A sidebar bar, filled by one key against another. |
| `Slot(string key, string caption, integer red, integer green, integer blue)` | — | A row on the CHARACTER SELECT screen, beside a saved character's name. The only place you can say anything about a body the engine is not running - a level, a class, a guild - and without one the screen where somebody picks between three characters offers three names and a sprite. Read off the saved character, so anything you want shown here has to be an attribute you keep on them. |
| `Bar(string key, string outOf, integer red, integer green, integer blue)` | — | A bar over every body's head, in a color given as red, green, and blue, each 0 to 255. |
| `Action(string id, string caption, string heading)` | `Verb` | A verb this game offers, under a heading of its own. Picking it calls OnAction. Offered in a square's menu until the verb says otherwise, and handed back so where it is offered, what key reaches it, and what it needs are each their own line. |
| `AskAtCreation(string key, string caption, string records)` | — | Something to ask before a character exists - a class, a bloodline, a starting town. The one question a game cannot ask any other way: everything else it wants to know it asks of a body already in the world, and what a character is has to be settled before there is one. The player picks from the records you name, listed by their own names, and the number they picked is written onto them under your key before OnPlayerJoined runs, so you read it the ordinary way and need no handler. A blank record is not offered, because a list of unnamed slots is a screen nobody can use. |
| `Panel(string id, string title, integer width, integer height)` | `Panel` | A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, so its rows and its buttons are written underneath it. |

### Records

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Are(string plural, string singular, integer limit)` | — | What these records are called in the editor — the plural, then the singular — and how many there may be. A caption left blank keeps the model's own name. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Extend(string records)` | — | Adds this model's fields to a family that already exists - the engine's 'Items' or 'NPCs', or another module's - instead of declaring one of its own. This is how a game hangs its own facts on a record the engine owns: which classes may wield a sword, which spell is written on a scroll. The engine's own item properties are a closed set, because Core cannot act on one it has never heard of; yours are open, and they belong on the sword itself rather than in a second table keyed by item number. The other family decides what these records are called, where they live and how many there may be, so Are and Stored do nothing here. |
| `Stored(string folder, string prefix)` | — | Where these records live: the folder under the world, and what each file is called before its number. Only needed for records already on disk. A new game leaves it out, and the model's name decides. |
| `Caption(string field, string caption)` | — | What one field is called on the form. Only needed where the field's own name is not the words an author should read. |
| `Range(string field, integer least, integer greatest)` | — | The bounds of a whole-number field. Equal bounds mean unbounded. |
| `Length(string field, integer characters)` | — | How long a text field may be. Zero means no limit. |
| `Points(string field, string records)` | — | Makes a whole-number field a picker over another kind of record, listing them by name. Use it to point at the engine's own records - 'Items', 'NPCs', 'Maps', 'Shops', 'Conversations' - which have no model to type a field as. A field typed as one of your own models already does this and needs nothing here. The number is still what is stored: this changes what the form draws and nothing about what a rule reads back. |

### Verb

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `OnTile()` | — | Offer it in the menu of a square. The default, and where most verbs belong. |
| `OnPlayer()` | — | Offer it in the menu of another player, who arrives as 'on' in OnAction. |
| `OnNpc()` | — | Offer it in the menu of a creature. Declaring one is what gives a plain creature a menu at all. |
| `OnHud()` | — | Offer it as a button on the HUD, for a verb about the player rather than about something they are pointing at. |
| `Aimed()` | — | Act on whatever they have TARGETED, when they used it without pointing at anything. Targeting is the engine's: Tab picks the next body, Ctrl+Tab picks themselves, and a click picks whoever was clicked. So a verb that can go either way - a spell that heals or harms - is ONE verb, and aiming it inward needs nothing of yours. Leave it off for a verb about a place. |
| `Nowhere()` | — | Offer it nowhere on its own: YOU say where it appears. A button on one of your own panels, a choice in one of your conversations, or a key you bound. What a verb that belongs to a screen wants - a guild's vault, a training hall - so that right-clicking a passing shopkeeper is not how a player reaches it. |
| `Key(string key)` | — | A key that reaches it without the menu: B, C, E, J, K, N, P, Q, R, T, U, Y, Z. The key acts on the square the player faces. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Interacts()` | — | Picking it also does what the engine's own reach key would have done - a shop, a conversation, or the body's own line. A game that binds E takes that key outright, so this is how it hands interaction back. Only meaningful on a creature. |
| `Opens(string panel)` | — | The panel it opens, by the id given to game.Panel. A panel that was never declared is refused by name, so you get an error instead of a button that does nothing. |
| `Hidden()` | — | Not drawn at all while its condition does not hold, rather than drawn dim. What a verb about something a player may never have wants - a guild hall, a mount - since a dim entry that never lights up is one they read past every time. On the HUD the buttons below it close the gap. |
| `Grayed()` | — | Drawn dim and unclickable while its condition does not hold, which is what happens anyway unless Hidden is asked for. Worth saying out loud on a verb whose condition a player can go and satisfy, since the dim entry is how they learn it is there. |
| `NeedsAtLeast(string key, integer least)` | — | Offered only to a body carrying at least that much under that key. Below it the verb is drawn dim, or not at all if Hidden was asked for. |
| `NeedsCarrying(string key)` | — | Offered only to a body that carries that key at all. |
| `NeedsNothing(string key)` | — | Offered only to a body that does not carry that key. |

### Panel

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Key(string key)` | — | A key that opens it: B, C, E, J, K, N, P, Q, R, T, U, Y, Z. |
| `Button(string caption, string verb)` | — | A button along its bottom: a caption, and the id of a verb it calls. |
| `Smallest(integer wide, integer tall)` | — | How small the player may drag it. Every panel resizes; this is the floor, and it stops a drag turning yours into a title bar with nothing under it. Say nothing and the engine's own floor applies, which knows nothing about what you put on it. |
| `OnlyWhile(string key, integer least)` | — | Only lets it open while that key reads at least that much, and closes it if that stops being true while it is up. Its key and any verb that opens it are refused too. What a screen about something a player might not HAVE wants - a guild, a mount, a house - since otherwise it opens onto blank rows. |
| `HeldWhile(string key, integer least)` | — | Puts it up while that key reads at least that much, and takes it down when it stops - with no close button, because the player did not open it. What a readout wants: a score during a fight, the wait over a body that cannot act, the state of ground being fought over. It has a slot of its own, so it never pushes aside a window somebody opened. Give it a button for the way OUT of whatever it is about - leaving, getting up - because closing the window and leaving the thing are not the same act. |
| `Row(string caption, string id)` | — | One line of a list the player picks ONE of. Both are attribute keys read off the player: the first is what the line reads as, the second what it IS - a line saying 'Ironhelm, at war since Tuesday' and carrying the guild number that names. Whatever is picked arrives as 'picked' in OnAction, on whichever button they press next. A line whose caption is blank is not drawn, so declare as many as the thing behind them can hold and fill the ones that are real. Pass an empty id to let the caption be its own. One list to a panel. |
| `Icon(string glyph)` | — | The glyph beside it. One of: grid, quads, pin, flag, bag, gem, coin, sword, flask, cog, person, paw, leaf, heart, book, scroll, list, bubble, key, shield, star, spark, flame, clock, note, dice, shop. A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section. |
| `Asks(string modelName, string caption)` | — | Asks the player to fill one of this world's own models in, and send it. Every field of the model becomes a control — a number a spinner, a truth a checkbox, an enumeration a drop-down over its members — and the caption names the button under them. What they send arrives at OnMessage under the model's name. A panel asks for one message. |
| `Heading(string caption)` | — | A heading on this panel, separating the rows under it. |
| `Field(string key, string caption, integer red, integer green, integer blue)` | — | A row on this panel: a key read live off the player, a caption, and a color as red, green, and blue. All three zero leaves the color to the client. |
| `Badge(string key, string caption, integer red, integer green, integer blue)` | — | The same, drawn as a small colored tag. A key holding a WORD puts the word in the tag and the caption beside it; a key holding a yes or no puts the caption itself in the tag when it is yes, and draws nothing at all when it is no. |
| `Meter(string key, string outOf, string caption, integer red, integer green, integer blue)` | — | A bar on this panel, filled by one key against another. |

### Npc

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Name` | `string` | What it is called - the name on its record, trimmed. Blank once the body has left. |
| `IsHere` | `boolean` | Whether the body is still in the world. A handle outlives what it names. |
| `Map` | `integer` | Which map it is standing on, or zero when it is nowhere. |
| `X` | `integer` | How far across that map it is. |
| `Where` | `Spot` | The square it is standing on, as a value - map, tile and plane together. What to hand anything that puts something where a body is, because a bridge and the water under it are the same three numbers and two different places. |
| `Y` | `integer` | How far down it. |
| `Has(string key)` | `boolean` | Whether it carries that key at all, so absence is told from zero. |
| `Number(string key)` | `integer` | What it carries under that key, or zero where it carries nothing. |
| `Text(string key)` | `string` | The same, as text, or empty where it carries nothing. |
| `SetNumber(string key, integer amount)` | — | Writes that key, and ships it to everyone entitled to see it. |
| `SetText(string key, string value)` | — | The same, with text. |
| `Float(string line, integer red, integer green, integer blue)` | — | Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. The one place a script asks the client to draw: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state. |
| `IsEngaged` | `boolean` | Whether they are in a fight right now. What being in one means is yours - holding regenthrough it, refusing a warp out of it - and this is the clock you set, read back. |
| `IsMarked` | `boolean` | Whether they carry a mark right now. |
| `IsAggressor` | `boolean` | Whether they are flagged as having started it. |
| `IsWaiting` | `boolean` | Whether they are still held off acting. Measured forward from when the cooldown started, matching the direction the bar drawing it fills. |
| `Engage(integer seconds)` | — | Marks it as in a fight for that many seconds, which brings up its overhead bars. What being in a fight means is the game's; the engine keeps the clock. |
| `Mark(integer seconds)` | — | Marks it for that many seconds - a flag a game puts on a body and reads back later, which is how a kill gets claimed by whoever earned it. |
| `Flag(integer seconds)` | — | Marks it as the one that started it, for that many seconds. |
| `Wait(integer seconds)` | — | Holds it off acting again for that many seconds. |
| `Sweep(boolean connected)` | — | Sweeps a crescent over them, in the direction they are facing. Pass true to fling sparks with it, so the swing reads as connecting instead of passing through air. What the crescent depicts is up to you: a sword, a claw, a thrown net. |
| `ThrowAtPlayer(Player at, string look, integer red, integer green, integer blue)` | — | Throws something at a player: 'bolt', 'glitter' or 'parcel', and a color. A number floated at the same target waits until it lands, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `ThrowAtNpc(Npc at, string look, integer red, integer green, integer blue)` | — | Throws something at another creature: 'bolt', 'glitter' or 'parcel', and a color. A number floated at the same target waits until it lands, so the hit and the damage read as one event - which is most of why this is worth using over a bare burst. |
| `Burst(integer red, integer green, integer blue, integer power)` | — | Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, sparks off an anvil, water and dust are one burst with a different color. Nothing here lasts; something still there a minute later is World.Stain. |
| `Kill(string cause)` | `boolean` | Takes it out of the world, with a cause the death policy can read. False for a body that was not there, or that something refused to let die. |
| `Kind` | `integer` | Which creature it is a copy of - the number of its record in this world's creatures. What a rule about a species keys on: a bounty per creature, a drop table, which bodies a quest counts. Name answers with what a player reads, and two records may share a name, so a rule written against one silently follows an editor rename. Zero for a body that has left the world. |
| `Chase(Player who)` | `boolean` | Sends it after a player, whether or not it would have noticed them on its own. This is how you write a creature that only fights back: author it to amble, then chase whoever hits it. It runs the approach rather than walking in, because it was sent rather than tempted. It overrides the noticing and not the legs, so one authored to hold its tile still holds it and one authored to open the gap runs away instead. False when either body has left the world. |
| `ChaseNpc(Npc other)` | `boolean` | The same, at another creature - a guard sent at whatever wandered in, a beast set on one it would have ignored. False for a creature sent after itself. |
| `Forget()` | `boolean` | Lets go of whatever it was chasing, leaving it to its record's own behavior again. Harmless on a body that was chasing nothing. |
| `Behavior` | `string` | How it moves on its own: 'stationary', 'wander', 'pursue', 'flee', 'scavenge', or 'shadow' - which closes to a distance and keeps it. Six ways of walking and deliberately nothing about why - a reason is a property of the game, and 'hostile' means nothing in a world with no fighting in it. Empty once the body has left. |
| `Group` | `integer` | Which pack it keeps to, or zero for one in none. Two creatures sharing a group never notice each other, on top of never noticing their own kind, so a pack does not fight itself. |
| `Range` | `integer` | How far it notices anything, in tiles, as its record was authored. Zero for a body that notices nobody. |
| `Standoff` | `integer` | How many tiles back it holds, for a body that keeps its distance: the authored number, or what the engine derives from its reach when the record names none. Zero for every other behavior, none of which keeps a distance. Use this to aim rather than a number of your own, because the body stops where the engine says it stops. |
| `IsChasing` | `boolean` | Whether it is after somebody right now - one it noticed, or one you sent it after. The other half of Chase and Forget, which write and never read: without this a rule cannot tell a creature already in a fight from one standing idle. |
| `Target` | `Player?` | The person it is after, or nothing - which is also the answer when what it is after is another creature. The other half of IsChasing: whether a body is after somebody was askable and who was not, and a bolt has to be aimed at something. |
| `TargetNpc` | `Npc?` | And the creature it is after, or nothing. Split in two for the same reason there are two contact handlers: a rule handed the wrong kind of body runs and returns nonsense, where one that is never called at least returns nothing. |

### World

Reached through its own name; there are no values of it.

| Written | Yields | What it does |
|---|---|---|
| `NpcAt(integer map, integer x, integer y)` | `Npc?` | The creature standing on that square, or nothing. A verb declared OnNpc arrives at OnAction with the square it was used on, and this is what turns that into the body. |
| `NpcsNear(integer map, integer x, integer y, integer tiles)` | `Npc[]` | Every creature standing within that many tiles of the square, nearest first - which is what a rule about the bodies around something starts from: guards answering a call, a herd that scatters when one of them is startled. On that map only, so a body one tile over a border is close and is not in the answer; ask for each map to reach those. |
| `NpcsOn(integer map)` | `Npc[]` | Every creature standing on that map. Use it for a sweep NpcsNear cannot answer: telling every creature that night fell, counting what is still alive, clearing something a spell left behind. Visitors on the map are included and natives away chasing elsewhere are not, so walking every map hands you each creature once. |
| `PlayersNear(integer map, integer x, integer y, integer tiles)` | `Player[]` | Every player standing within that many tiles of the square, nearest first - the mirror of NpcsNear, and what a rule about the people around something starts from: who shared a kill, who heard a shout, who was standing too close. On that map only, so somebody one tile over a border is close and is not in the answer. |
| `PlayerAt(integer map, integer x, integer y)` | `Player?` | The player standing on that square, or nothing. Answered before a creature when both somehow occupy one tile. |
| `Tell(string line)` | — | Says a line to everybody in the world. For the handful of things that are genuinely everyone's business - a season turning, somebody finishing what only one person can finish. A game that announces ordinary events this way has an unreadable chat log. |
| `TellOn(integer map, string line)` | — | Says a line to everybody who can see that map - the nearest thing a seamless world has to a room. That is wider than the people standing on it: somebody on the next map along is looking at this one too. |
| `TellNear(integer map, integer x, integer y, string line)` | — | Says a line to everybody within earshot of a square. A tighter audience than TellOn: who can hear speech, not who can see the region. |
| `Stain(integer map, integer x, integer y, integer size, integer amount)` | — | Marks the ground. The stain dries on its own and is drawn to everyone who can see the tile; amount is 0 to 100. Unlike a burst it persists, so it is still there when somebody walks back. The color is the world's, set once for the whole world. |
| `TellThese(Player[] them, string line)` | — | Says a line to a set of players, wherever they are. Anybody in the set who has since left the world is skipped, not refused, because a set gathered a moment ago may already be out of date. |
| `Guildmates(Player who)` | `Player[]` | Everybody in the world who shares their guild, including them. Empty for somebody in no guild - which is not the same as a guild with nobody online, and World.Guild is what tells those apart. |
| `Party(Player who)` | `Player[]` | Everybody in their party, including them. Empty for somebody in no party. |
| `TileAt(integer map, integer x, integer y)` | `string` | What kind of ground is there: 'walkable', 'blocked', 'warp', 'item', 'npcavoid', 'door', 'plate', or 'ramp'. Empty for a square that is not on a real map. The ground layer - a bridge deck is a different answer at the same coordinates. |
| `CanSee(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `boolean` | Whether a straight line between two squares crosses anything that stops sight. This is the same trace the client colors its target arrow with, so a rule gating on it agrees exactly with what the player was shown. A wall stops sight only if it was authored to: a railing blocks walking and not seeing. |
| `Distance(integer fromMap, integer fromX, integer fromY, integer toMap, integer toX, integer toY)` | `integer` | How far apart two squares are, in tiles, counting across map borders. The world scrolls contiguously, so a body one tile over a border is one tile away; subtracting coordinates would call it another map and unreachable. Use this for any range rule. -1 when the two are too far apart to compare. |
| `TimeOfDay()` | `string` | What time of day it is: 'day', 'dusk', 'night' or 'dawn'. The engine runs the cycle and the client paints it. Read it for anything that changes after dark - creatures that hunt at night, a shop that shuts, a spell that needs a moon. One answer for the whole world, unlike the weather, which is per map. |
| `Weather(integer map)` | `string` | What the sky is doing over that map: 'clear', 'rain', 'snow', 'heatwave', or 'heavywind'. Empty for a map that is not there. |
| `GuildOf(Player who)` | `integer` | Which guild they belong to, as its number, or zero for none. Rules key on the number because a guild is a record, not a body: it has no place, nothing walks it, and it outlives every member. Player.Guild gives the name, which is for showing people. |
| `GuildNamed(string name)` | `integer` | The guild with that name, or zero. Case-insensitive, the way the engine's own founding check compares - so a rule acting on a name a player typed asks the same question the engine did when it refused a second guild by that name. |
| `GuildName(integer guild)` | `string` | What that guild is called, or empty for a number naming none. |
| `Access(Player who)` | `string` | What their account may do to the world: 'player', 'monitor', 'mapper', 'developer' or 'creator'. Whoever runs a world usually wants its staff outside its rules - no fighting, no loot, no place on a ladder - and which rules that means is yours to decide. Empty for a body that is not here. |
| `GuildRank(Player who)` | `string` | What rank they hold: 'leader', 'officer', 'member', or empty for somebody in no guild. The engine keeps the rank and moves it; what a rank may do is yours. |
| `GuildNumber(integer guild, string key)` | `integer` | One of the values your game hangs on a guild - a war, a level, a season score. Zero for a key it does not carry, and for a number naming no guild. |
| `GuildText(integer guild, string key)` | `string` | The same, as text. |
| `SetGuildNumber(integer guild, string key, integer amount)` | — | Writes one of them, and gets the guild onto disk. Saved on every write, because a guild is not a body: nothing logs it out, so there is no later moment where its values would be written anyway. |
| `SetGuildText(integer guild, string key, string value)` | — | The same, with text. |
| `GuildMembers(integer guild)` | `Player[]` | Everybody in the world who belongs to that guild. Empty for one with nobody online, which is not the same as a guild that is not there. |
| `Guilds()` | `integer[]` | Every guild there is, by number. what anything ranked starts from: every other guild call takes a number you already had, off a body or off a name, and a standing, a league table or a sweep over all of them has none. Counting upward and hoping does not work either - a guild that disbanded leaves a hole in the numbering. |
| `WhoIs(string account)` | `Player?` | Whoever is signed in to that account right now, or nothing. Use it to reach the person again after writing an account down; nothing means they are offline, so post to them instead of telling them. |
| `AccountsIn(integer guild)` | `string[]` | Every account in a guild, signed in or not. World.GuildMembers answers only with the members currently online; this is the whole roster, which outlives their sessions. Use it for anything about the guild itself: a dividend, a census, a rule about who has stopped turning up. |
| `IsActiveIn(integer guild, string account)` | `boolean` | Whether that account is an active member of the guild or just a name on its roster: signed in for long enough, recently enough, by the engine's own measure. Ask it before counting somebody - who votes, who makes a quorum, how big a guild really is. |
| `MailTo(string account, string subject, string body)` | `boolean` | Sends an account a letter, whether or not anybody is signed in to it. What World.Mail cannot do: reach somebody who is not here. A rule that wrote an account down when it had the person settles up afterwards, and they find it waiting. |
| `MailItemTo(string account, integer item, integer many, string subject, string body)` | `boolean` | The same, with something attached. A sale settled, a refund, a prize drawn while they were away. |
| `Mail(Player who, string subject, string body)` | `boolean` | Sends them a letter. the one thing A rule can say that outlives the moment: a line of chat is gone when they log out, and a letter waits - through a logout, a restart, and a server that was down for a week. |
| `MailItem(Player who, integer item, integer many, string subject, string body)` | `boolean` | The same, with an item attached, which waits in the message until it is collected. Use it for a refund, a prize, a delivery, or a payout too big for a bag. Player.Give is the alternative and needs room in the bag right now. |
| `MailMembers(integer guild, integer item, integer many, string subject, string body, boolean onlyActive)` | `integer` | Sends every member of a guild that item, and reaches the ones who are not here. The only way to pay somebody offline: everything else reaches a body in the world, and what a group earned is owed to its members whether or not they happened to be logged in. It arrives as mail, so it waits for them. 'onlyActive' narrows it to members who have really been playing, by the engine's own measure of a live roster - a payout split among a hundred names nobody has used is a payout nobody feels. Yields how many it reached. |
| `GuildGold(integer guild)` | `integer` | What is in that guild's vault. |
| `GiveGuildGold(integer guild, integer amount)` | — | Puts gold into a vault. The counterpart to SpendGuildGold: a game holding gold aside - an escrow, a stake, a bond - has to be able to give it back. |
| `WornBy(Player who)` | `integer[]` | What they are wearing, as item numbers, in slot order. Empty for somebody wearing nothing. |
| `BagOf(Player who)` | `integer[]` | Which bag slots they have something in, in slot order. Carrying asks about an item; this asks about slots, which is what a rule about the bag itself needs - two copies of one sword are two slots with their own wear. |
| `ItemInSlot(Player who, integer slot)` | `integer` | What is in that bag slot, by item number. Zero for a slot holding nothing. |
| `CountInSlot(Player who, integer slot)` | `integer` | How many that bag slot holds: the stack size for something that stacks, and one for anything else. Zero for a slot holding nothing. |
| `SlotIsWorn(Player who, integer slot)` | `boolean` | Whether that bag slot holds the copy they are wearing. A rule about what a death scatters asks this, because worn gear and carried gear are dropped by different rules in most games. |
| `DropSlot(Player who, integer slot, integer many)` | `boolean` | Puts what is in that bag slot on the ground where they are standing. 'many' takes part of a stack; zero takes the whole slot. It lands as a player drop, so anyone may pick it up and the world's own limit on litter applies. False for an empty slot. |
| `WornIn(Player who, string slot)` | `integer` | What they are wearing in one slot, by item number. Zero for an empty slot, and for a slot this world does not declare. Ask it instead of walking the whole list when you care about one place on the body - whether a shield is up, whether a hand is free. |
| `DurabilityLeft(Player who, integer item)` | `integer` | How much wear is left in the copy they are wearing. Zero when they are not wearing one - two copies in a bag are two different amounts of wear, and this means the one that was on them. |
| `DurabilityFull(Player who, integer item)` | `integer` | How much that item holds when new. Zero for one with no durability at all, which is an ordinary thing for an item to be. |
| `WearOut(Player who, integer item, integer points)` | `integer` | Wears out that many points of the copy they are wearing, never past nothing. Yields how many were actually taken, which is fewer than asked for when it was nearly worn out. An item worn to nothing is not destroyed: it stays in the bag, unusable, until it is repaired. |
| `RepairCost(integer item, integer points)` | `integer` | What repairing that many points of that item costs, by the engine's own repair rate - the same rate a repair shop charges, so a game pricing wear agrees with the shop. |
| `DropAt(Spot where, integer item, integer many)` | `boolean` | Puts an item on that square out of nowhere, free to whoever reaches it first. Not out of anybody's bag - a chest that opens, a reward left where a quest ended, a hoard a rule rolled for itself. Player.Drop is the other one, and it moves something that already exists. |
| `DropClaimed(Spot where, integer item, integer many, Player who, integer seconds)` | `boolean` | The same, held for one player for that many seconds: nobody else may pick it up until the time runs out, and the client shows them whose it is. What stops the person who did the work watching somebody else walk off with it. |
| `RepairRate(integer tier)` | `real` | What one point of durability costs in gold at that tier, priced off a reference piece rather than off any particular item. Fractional on purpose: near the bottom of the ladder a point is worth less than a coin. Price other kinds of upkeep - a reagent, a charge, a ration - against this, and they will track the repair shop instead of drifting away from it. |
| `RegionOf(integer map)` | `integer` | Which map group that map belongs to, or zero. A group is the engine's idea of a region: several maps sharing a name and some settings. A game that owns regions asks this to turn where somebody is standing into which region it is. Read what your game hangs on one with World.Record("MapGroups", ...). |
| `ExitMap(integer map)` | `integer` | Where this map puts somebody who leaves it other than by walking, as a map number, or zero for one that names none. The map author's say over where leaving this place lands you; the engine consults it for nothing on its own, so a game reads it and outranks it as it sees fit. Its region answers for a map that says nothing. |
| `ExitX(integer map)` | `integer` | How far across that exit is. |
| `ExitY(integer map)` | `integer` | And how far down it. |
| `MapNumber(integer map, string field)` | `integer` | One of your own fields on a map, as a whole number - a field you added with Records.Extend("Maps"). Answers with the map's own value, falling back to its region's where the map leaves it unset, the same way every inherited map property works. Zero when neither carries it. |
| `MapText(integer map, string field)` | `string` | The same, as text. |
| `MapTruth(integer map, string field)` | `boolean` | The same, as a yes or no. An unticked box and an absent one are the same answer. |
| `SetRecordNumber(string records, integer number, string field, integer amount)` | — | Writes one of your own fields on a record, and saves it. Where a game keeps what belongs to no body and no guild: the last day it settled accounts, a season number, who holds a territory. Your own fields only - the engine's properties are written through their own paths, which normalize what they are given. |
| `SetRecordText(string records, integer number, string field, string value)` | — | The same, with text. |
| `Now()` | `integer` | The time now, in seconds since 1970, UTC. What anything dated needs: a cooldown that has to survive a restart, a window that stays open for an hour, a daily reset. Counting ticks answers a different question, since ticks stop when the server does. |
| `Spot(integer map, integer x, integer y)` | `Spot` | That square on the ground, as a value - so a rule can carry it around, keep a set of them, and hand it to anything that puts something somewhere. |
| `SpotRaised(integer map, integer x, integer y)` | `Spot` | The same square on the raised surface - a bridge, a ledge, a gantry - instead of the ground. One tile, two places. |
| `SpreadOver(integer region, integer count, string onlyWhere)` | `Spot[]` | That many squares spread across a region, every one reachable on foot from every other. Measured by walking, across the region's seams - not in a straight line, which is a lie wherever a wall or water stands between two tiles that are near on paper, and not by map number, which piles everything into whichever corner was drawn first. 'onlyWhere' names one of your own truth fields on Maps and a square goes only on a map carrying it; blank puts one anywhere in the region. The walk crosses the whole region either way, so a town in the middle of one is walked through. Fewer than asked for means there was nowhere else to put one. |
| `Empty(integer map)` | `boolean` | Takes every creature off that map and keeps it that way. For ground that has to stop being ordinary for a while: a war fought over it, a ritual nobody should interrupt, an arena cleared for a duel. Nothing comes back until World.Wake - which is what separates this from clearing a map and watching it refill a minute later. |
| `Refill(integer map)` | `boolean` | Lets the map hold creatures again and spawns its own back immediately, instead of leaving it bare until each slot's respawn clock comes round. |
| `IsEmptied(integer map)` | `boolean` | Whether that map is being kept empty of creatures. |
| `Marker(string id, Spot where)` | `Marker` | Puts a mark on that square and hands it back, so you can set its ring, label, meter and audience on the lines below. The equivalent of an overhead bar, for a place. Marking again under a name already in use replaces what is there, which is how a mark moves and how its meter advances - one call, not a remove and a place. |
| `Unmark(string id)` | `boolean` | Takes one away by name. False when nothing was under it, which is an ordinary answer for a rule clearing up after something that ended on its own. |
| `InsideMark(string id, Spot where)` | `boolean` | Whether that square is inside the mark's ring. Ask it instead of doing the arithmetic yourself, so the ring the player sees and the ring the rule scores stay the same one - if they drift apart, the line on screen sits somewhere other than the line that counts, and nothing reports it. False for a mark with no ring, and for a square on another map. |
| `LocalOffset()` | `integer` | How far the server's own civil day is from UTC right now, in seconds - east of it positive, west of it negative. anything that turns over at midnight wants this: a daily reset, a weekly tax, a season all mean the operator's own midnight, and dividing World.Now by a day gives the wrong one everywhere but Greenwich. Add it before dividing. Read fresh, so a place that keeps summer time answers differently in July than in January - which is what keeps a boundary at midnight all year. |
| `Number(string key)` | `integer` | One of your own values about the world itself, as a whole number, or zero for one never written. The place for what belongs to no body and no record - which season it is, whether an event is running, how many times something has happened. Read back as it was left when the server starts again. |
| `Text(string key)` | `string` | The same, as text. Empty for one never written. |
| `Truth(string key)` | `boolean` | And as a yes or no. False for one never written. |
| `SetNumber(string key, integer amount)` | — | Writes one. Kept until the world is next written, which the engine does on its own cadence and at shutdown. |
| `SetText(string key, string value)` | — | The same, as text. |
| `SetTruth(string key, boolean value)` | — | And as a yes or no. |
| `SpendGuildGold(integer guild, integer amount, Player by)` | `boolean` | Takes gold out of a vault, recording who spent it. Through the engine's own ledger rather than by writing the number: a vault that went down with nothing in the spending log is money a guild cannot account for. False when the vault does not hold that much, so this is the check as well as the payment. |
| `WalkMs()` | `integer` | How long one tile takes at a walk, in thousandths of a second. The same for everybody, because a walk is a walk - it is Player.RunMs that a body's Pace moves. Divide this by theirs to say how much faster running is for them. |
| `KeptNumber(string store, string key, string field)` | `integer` | One field out of one key of one of your game's own stores. Zero for a store, a key or a field that is not there. A store is a set of named bags: World.Number holds what there is one of, and this holds what there are many of - a row per player on a ladder, a tally per region, whatever your rules pile up while the world runs. |
| `KeptText(string store, string key, string field)` | `string` | The same, as text. Empty for one that is not there. |
| `KeptTruth(string store, string key, string field)` | `boolean` | The same, as a yes or no. False for one that is not there. |
| `SetKeptNumber(string store, string key, string field, integer amount)` | — | Writes one, making the store and the key the first time each is used. Nothing is declared and there is no slot count: a store is as big as what you have put in it. Kept with the world's own values, so it reaches disk on the world's save beat rather than on this call - what has to survive the instant it happens belongs on a character or a guild, which save on write. |
| `SetKeptText(string store, string key, string field, string value)` | — | The same, with text. |
| `SetKeptTruth(string store, string key, string field, boolean value)` | — | The same, with a yes or no. |
| `HasKept(string store, string key)` | `boolean` | Whether that store holds anything under that key at all - the question to ask before counting a zero as a score somebody earned rather than a row that was never written. |
| `Forget(string store, string key)` | — | Drops that key and every field under it. A store with nothing left in it goes too. |
| `KeptCount(string store)` | `integer` | How many keys that store holds. Zero for one nothing was ever put in. |
| `KeptKeyAt(string store, integer index)` | `string` | The index-th key of that store, counting from one, or empty past the end. Walk a store with 'loop for i = 1 to World.KeptCount(store)'. Ordered by the key itself rather than by when it arrived, so a pass reads the same way twice running and the same way after a restart. |
| `Records(string records)` | `integer` | How many records of that kind this world holds, counting blank slots. Zero for a kind nobody declared. |
| `Record(string records, integer number, string field)` | `string` | One field of one record, as text, or empty where the slot or the field is not there. What a game reads at run time out of the records its own editor authored. |
| `RecordNumber(string records, integer number, string field)` | `integer` | The same, as a whole number. Zero where the slot or the field is not there. |
| `RecordName(string records, integer number)` | `string` | What a record is called - an item's name, a creature's, a map's. The one engine property a record can be asked for, so that a rule choosing between records can say which it means. Blank for a slot nobody authored. For your own records this is their name field. |
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
| `IsRaised` | `boolean` | Whether it is on the raised surface - a bridge, a ledge, a gantry - or on the ground. A bridge and the water beneath it share their coordinates, so a rule that acts on a square needs this to tell the two apart. |

### Marker

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Label(string text)` | — | Writes that over it. Left unsaid, a mark carries no label. |
| `Color(integer red, integer green, integer blue)` | — | What color it is drawn in. The pennant, the ring and the label all take it, so a side's own color is one line. |
| `Ring(integer tiles)` | — | Draws the ground within that many tiles of it. Drawn as the staircase of tiles actually inside, never as a circle - so the line on the screen and World.InsideMark are one statement. Nothing at or below zero draws no ring. |
| `Meter(integer value, integer ceiling)` | — | A bar over the mark, that full out of that. Set the ceiling to zero for a mark that is only a pin, with no bar at all. |
| `SeenBy(Player[] them)` | — | Makes the mark private to those bodies. Leave it unset and everybody who can see the square sees it. Calling it again adds to the list, so a mark for several sides is one call per side. The list is a snapshot: somebody who joins after it is placed will not see the mark until it is placed again. |

### Spoils

Held as a value and never made by a script: the engine hands one over.

| Written | Yields | What it does |
|---|---|---|
| `Item` | `integer` | What this line drops, by item number. |
| `Many` | `integer` | How many of it. Only an item that stacks reads this; anything else lands as one however large the number is. |
| `Chance` | `integer` | How often the line lands, as a plain percent - one in a hundred at 1, every time at 100 or more, never at nothing. |
| `Kind` | `integer` | Which creature this was a copy of. What a rule about a kind of creature keys on, and it outlives the body - which is about to stop being there. |
| `From` | `Npc?` | The body itself, still standing on the tile it fell on. For this call only: the slot is cleared the moment the table finishes rolling, and a handle kept past that answers IsHere with no. |
| `Killer` | `Player?` | Who killed it, or nothing when the world itself did. |
| `Rolls(integer chance)` | — | Sets how often the line lands, as a percentage. Zero or less skips the line without rolling it, so this kill owes this player nothing. |
| `Yields(integer many)` | — | Makes it that many instead - a doubled purse, a halved one. Read only for an item that stacks, as above. |
| `ClaimedBy(Player who, integer seconds)` | — | Holds the drop for that player for that long: nobody else may pick it up until the time runs out, and the client shows them whose it is. What stops the person who did the work watching somebody else walk off with it. Left unsaid, a drop is free to whoever reaches it first. |

## What a module may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when
the module is read, not when it runs. A world folder is something one person hands to another,
and a module that could open a file could read the accounts beside it.

Everything else a script can name is on this page. There is no way to reach a type the engine
did not register.
