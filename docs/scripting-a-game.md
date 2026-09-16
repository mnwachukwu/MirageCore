# Scripting a game on Core

**A game that is content.** No compiler, no SDK, no rebuild: a folder of Compass files inside a world,
read when the server starts. Everything on this page is something you can do by editing a text file and
restarting.

The other route is [building a C# module](building-on-core.md), and
[choosing between them](implementing-a-game.md) is the honest comparison. Neither is the poor cousin.

The worked example is **Survey**, the game this repository ships, written entirely this way:
[`server/src/Mirage.Server.Host/world/scripts/`](../server/src/Mirage.Server.Host/world/scripts/). Every
snippet below is from it.

---

## 1. Writing it

**Install the VS Code extension, and the compiler it runs.** Compass has a language server — the
compiler's own, answering about files as they are being typed — so a type error, an unassigned local,
a switch missing an enumeration member, or an optional read without proving it holds something are all
reported where you wrote them. Breakpoints, stepping, a call stack, and a variables pane work too.

⚠ **Two installs, not one.** The extension is a front end: coloring works on its own, and the
language server, the debugger, and the build commands all shell out to `cm`. Without the compiler on
the path the extension does almost nothing, which reads as a broken extension rather than a missing
one. Both are one command:

```
code --install-extension Pluperfect.compass-editor
```

The compiler is one command per platform at <https://compass.pluperfect.dev/install>, self-contained,
with nothing to install first.

**The engine's own types resolve too.** `Builder`, `Player`, `Records`, `Verb`, and `Panel` are
handed to the compiler by the SERVER while a world loads, so a checker outside the server would never
have heard of them. The server writes them down instead: `world/.mirage/engine.cm` declares each one
as an abstract model, and `world/world.cmp` ties that folder to your scripts so opening any of them
brings the whole thing in. Both files are generated — do not edit either.

What each member does is written above it as a documentation comment, so hovering a call in the
editor answers what it does and what each parameter is called without leaving the file.

🔴 **A handler is matched by NAME AND ARITY.** A function whose signature drifts by one parameter is
not a broken handler — it is a function nobody calls. It compiles, it loads, and the rule it served
quietly stops working. If something you wrote never runs, count the parameters first.

⚠ **Every handler must be `public`.** The engine calls it and nothing inside your module does, so
without `public` the compiler reports it as a function nobody uses — which it is.

**Every handler is optional.** Write the ones you want.

`Rules` is the door and nothing else. Survey's is forty lines, and each one hands the work somewhere:

```
shared model Rules

    public function Configure(Builder game)
        Surveyor.Declare(game);
        Land.Declare(game);
        Book.Declare(game);

        game.TickEvery(20);
    end function

    public function OnPlayerMoved(Player who, integer fromX, integer fromY)
        Walking.Stepped(who);
    end function

end model
```

## 2. Where the files go

A module is every `.cm` file under a world's `scripts/` folder, **however deep**. Folders organize; they
do not divide. One module, many files.

```
world/
  scripts/
    rules.cm            the engine's door: every handler it calls
    ranks.cm            a lookup this game owns
    declare/
      surveyor.cm       what a surveyor carries
      land.cm           what the world holds
      book.cm           the field book and every verb
    rules/
      walking.cm        stepping costs stamina
      recovery.cm       resting gives it back
      notes.cm          what each verb does
  species/              the records an author writes in the editor
  maps/
```

A model in one file reaches a model in another by name. There is no import, no ordering, and no
manifest — the folder is the module.

## 3. The engine's door

The engine looks for a `shared model` called **`Rules`** and for the handlers below on it. Nothing else
is ever called from outside.

| Written | Called when |
|---|---|
| `public function Configure(Builder game)` | once, before the world exists |
| `public function OnPlayerJoined(Player who)` | they are in and have everything they need |
| `public function OnPlayerLeft(Player who)` | they have gone, while their record is still readable |
| `public function OnPlayerMoved(Player who, integer fromX, integer fromY)` | every accepted step |
| `public function OnAction(Player who, string action, string on, integer map, integer x, integer y)` | they picked one of your verbs |
| `public function OnTick()` | your tick came round |
| `public function OnPlayerTick(Player who)` | the same tick, once per player in the world |
| `public string function OnMayDie(Player who, string cause)` | somebody is about to die |
| `public integer function OnLinger(Player who)` | their connection dropped |

[`docs/scripting-api.md`](scripting-api.md) is generated from the engine and lists every one of these
with everything a `Player` and a `Builder` offer. It is the reference; this is the playbook.

## 4. Declaring: what the game IS

`Configure` runs **once, before the world exists**. Everything it says is about the game, never about
anybody in it — there is nobody in it yet. Reaching for a player here is refused rather than crashing.

### What a body carries

```
game.Attribute("stamina", "viewport");
game.Attribute("specimens", "owner");
```

Three visibilities, and the choice is about who can see the number:

| Word | Who reads it |
|---|---|
| `none` | the server alone |
| `owner` | the player it belongs to |
| `viewport` | everyone who can see them |

⚠ **A bar over somebody's head needs `viewport`.** The bar is drawn from two keys, and a key only the
owner can read draws nothing on anybody else's screen. That failure is silent and it looks like a
broken client.

```
game.EquipSlot("satchel", "Satchel");
game.Bar("stamina", "staminaMax", 120, 190, 90);
```

### The sidebar

```
game.Heading("Survey");
game.Field("rank", "Rank", 200, 200, 160);
game.Field("specimens", "Specimens", 200, 200, 160);
game.Meter("stamina", "staminaMax", "Stamina", 120, 190, 90);
```

Four row shapes — `Heading`, `Field`, `Badge`, `Meter` — each reading a key live off the player. The
three numbers are red, green, and blue; all three zero leaves the color to the client.

### Records of your own

🔴 **The model IS the declaration.** You already wrote what a record holds; the engine reads it rather
than making you say it twice.

```
enumeration Habitat
    Shore, Woodland, Meadow
end enumeration

model Species
    public string name;
    public Habitat habitat;
    public string notes;

    public shared function Describe(Records these)
        these.Are("Species", "Species", 200);

        these.Caption("name", "Common name");
        these.Length("name", 40);
        these.Length("notes", 240);
    end function
end model
```

That is one editor section, one folder on disk, load and save, row locking, hot reload, world transfer,
and the world check — for a kind of record the engine has never heard of. Nothing in `Configure`
mentions it; the engine calls `Describe` on every model that has one, before your rules run.

| In the model | On the form |
|---|---|
| `string` | a text box |
| `integer` | a spinner |
| `real` | a box that parses a fraction |
| `boolean` | a checkbox |
| an **enumeration** | a drop-down over its members |
| another **model** | a picker listing that model's records by name |
| a whole number named by `Points` | a picker over any family, **the engine's own included** |

**Two seams close themselves.** A field typed as an enumeration declares its own set of choices; a field
typed as another model declares its own link. Neither is named twice, so neither can drift.

### Pointing at the engine's own records

`Items`, `NPCs`, `Maps`, `Shops`, and `Conversations` are records a world holds like any other, and no
model names them — so a field typed as a model cannot reach them. `Points` does:

```
public integer item;
...
these.Points("item", "Items");
```

The form draws a picker listing them by name; the number is still what is stored. Authoring a kit line
by typing 214 when the answer is "Iron Sword" is exactly what this exists to stop.

### Adding your own fields to the engine's records

🔴 **An item's own properties are a closed set, because Core cannot act on one it has never heard of.**
It knows a weapon has power and durability because it spends both. What *your game* knows about that
sword is open — a class gate, an element, the spell written on a scroll — and those belong on the sword
rather than in a table beside it.

```
model ItemRules
    public integer levelReq;
    public Spell teaches;

    public shared function Describe(Records these)
        these.Extend("Items");
        these.Caption("teaches", "Spell written on it");
    end function
end model
```

The editor's own Items form grows those rows. There is no new folder, no new file, and no new packet —
**an extended family is the same family with more rows on it.** A rule reads them back the ordinary
way:

```
integer written = World.RecordNumber("Items", item, "teaches");
```

| | |
|---|---|
| `Are`, `Stored` on an extending model | ignored — the family being extended already answers all three |
| A key the family already has | refused by name, because two modules writing one key overwrite each other |
| What the engine's own properties mean | unchanged; a game reads and writes only its own keys |

⚠ **A field holds one value, so a many-to-many fact is still a family.** "Which classes may wield
this" is a list, and a list is rows — the same shape a spell gate takes.

⚠ **A schema model's fields must be `public`.** The engine reads them and your script does not, and a
private field nothing names is one Compass will tell you to delete.

⚠ **`Describe` has to be `shared`.** It describes a KIND of record rather than one record, so there is
no particular `Species` to hand it. An instance function of that name compiles, loads, and is never
called; the engine refuses it by name so that does not read as a broken editor.

⚠ **A field is named as TEXT**, because Compass has no type values. A typo in `Caption` or `Length` is
a refusal at load, not an error at compile — the message lists the fields that do exist.

### The glyph beside it

Records, panels, and verbs each pick one, from a list the engine offers:

```
these.Icon("leaf");          # on a model's Describe
book.Icon("book");           # on a panel
note.Icon("scroll");         # on a verb
```

🔴 **Without one, every game's records wear the same glyph** — so a world with two kinds of record
has two editor sections a reader cannot tell apart, and cannot tell from one of Core's own.

A **name** travels and each surface draws its own shape for it: a game cannot ship geometry to a
client or to an editor it does not control, which is the same bargain the key list makes. Twenty-seven
of them, chosen so no genre is stuck:

`grid` `quads` `pin` `flag` · `bag` `gem` `coin` `sword` `flask` `cog` · `person` `paw` `leaf` `heart`
· `book` `scroll` `list` `bubble` · `key` `shield` `star` `spark` `flame` `clock` `note` `dice` `shop`

⚠ **A name that is not one of those is refused at load, with the list.** A typo would otherwise be a
section that looks like every other section, which reads as an engine ignoring the line rather than as
a misspelled word. A RENDERER is the tolerant one: it falls back, so a client older than the game it
joined still draws something.

⚠ **`Stored` is only for records that already exist.** Left unsaid, the folder is the model's name
lowercased and the files are that without a trailing "s" — right for `sites/site1.json`, wrong for
`species/species1.json`, because English is not a rule. A new game should say nothing and let the
default name the files.

### Verbs

A verb is declared once and then told what it is offered on, what reaches it, and what it needs.
**Nothing depends on the line above it.**

```
Verb note = game.Action("survey.note", "Note this down", "Survey");
note.Key("Q");

Verb open = game.Action("survey.openbook", "Field Book", "Survey");
open.OnHud();
open.Opens("survey.fieldbook");

Verb compare = game.Action("survey.compare", "Compare notes", "Survey");
compare.OnPlayer();
compare.NeedsAtLeast("specimens", 1);
```

| Said about a verb | What it means |
|---|---|
| `OnTile()` | in the menu of a square — the default |
| `OnPlayer()` | in another player's menu; they arrive as `on` |
| `OnNpc()` | in a creature's menu; declaring one is what gives a plain creature a menu at all |
| `OnHud()` | a button on the HUD, about the player rather than about the ground |
| `Key("Q")` | a key that reaches it without the menu |
| `Opens(panel)` | it opens one of your panels instead of calling `OnAction` |
| `NeedsAtLeast(key, n)` | grayed below that, and lit the moment they have it |

Bindable keys are **B, C, E, J, K, N, P, Q, R, T, U, Y, and Z**. Anything the engine reserves, or a key
another declaration already took, is refused by name rather than quietly overriding.

### A screen of your own

```
Panel book = game.Panel("survey.fieldbook", "Field Book", 240, 180);
book.Key("B");
book.Button("Note this down", "survey.note");

book.Heading("Field record");
book.Field("rank", "Rank", 200, 200, 160);
book.Meter("stamina", "staminaMax", "Stamina", 120, 190, 90);
```

A panel is a title, a column of rows, and a row of buttons, in that order. Its rows sit on a surface
derived from its id, so a row cannot land on a surface nothing draws. A game wanting columns or a grid
wants a UI toolkit on the wire, which is a much larger thing than this.

## 5. Reacting: what happens to somebody

```
public function Stepped(Player who)
    integer left = who.Number("stamina");

    if left <= 0
        yield;
    end if

    who.SetNumber("stamina", left - 1);

    if left == 1
        who.Message("You are too tired to go much further today.");
    end if
end function
```

`who.Number(key)` reads, `who.SetNumber(key, n)` writes and ships it to everyone entitled to see it.
`who.Has(key)` is what tells absence from zero.

### A verb used on somebody

`OnAction` carries the target as a **name**, because the boundary has no way to say "somebody, or
nobody" in an argument. `who.Find(name)` is how you reach the body behind it:

```
Player? them = who.Find(on);

if not them.HasValue()
    who.Message("There is nobody there to compare notes with.");
    yield;
end if

them.Message("...");
```

Past that guard the compiler knows there is a body there, so nothing below has to ask again.

### Deciding

```
public string function OnMayDie(Player who, string cause)
    yield "Nobody dies out here.";
end function
```

Blank allows it; anything else refuses it and is what the player is told. 🔴 **A handler that fails
allows the death** — a world where nobody can die because of a bug is worse, and much harder to notice,
than one where somebody died who should not have.

```
public integer function OnLinger(Player who)
    yield 30;
end function
```

## 6. Things that fail silently

Every one of these has happened, and none of them looks like an error:

| What you wrote | What happens | How you notice |
|---|---|---|
| a handler one parameter off | never called | the rule just does nothing |
| a handler without `public` | never called | the compiler says nothing uses it |
| `viewport` forgotten on a bar's keys | the bar is blank for everyone else | it looks right on your own screen |
| a verb opening a panel you did not declare | the button does nothing | refused by name in the log |
| a field typed as a model you declared no records for | the picker lists nothing | refused by name in the log |
| a model name misspelled in `Records` | no editor section | refused by name, listing the models that exist |

**The first three are the seam's own, and no compiler can see them** — not Compass's, and not C#'s if
you wrote the same mistake there. The rest are refusals, which means the engine caught them.

⚠ **Read the server log on the first start after a change.** A refusal names what it turned down and
why, and the rest of the file still declares — so a world with one mistake in it is a world that runs
with one thing missing, not a world that fails to start.

## 7. What a script may not reach

The language's own way out of a program — the filesystem, the machine clock — is refused when the module
is read, not when it runs. A world folder is something one person hands to another, and a module that
could open a file could read the accounts beside it.

There is no way to name a type the engine did not register. Everything reachable is on
[the reference page](scripting-api.md).

## 8. A message of your own

A verb is the only thing a STOCK client originates for a game, and it carries an action id and a square
and nothing else. When you want a client to send you values, declare a message — from a model, the same
way records are declared:

```
model Note
    public integer species;
    public string comment;
    public boolean sure;
end model
```

```
game.Message("Note");
```

```
public function OnMessage(Player who, string message, Values values)
    if message != "Note"
        yield;
    end if

    integer species = values.Number("species");
    string comment = values.Text("comment");
end function
```

A line arriving as `{"cmd":"Note","species":3,"comment":"by the shore","sure":true}` reaches that
handler with those three fields. **Nothing is compiled** — the registry takes a parse delegate, so the
model is what says which field is a number and which is text.

🔴 **A field the model did not name is dropped rather than carried.** A sender cannot reach past what
the rules said they may send, and a field the line left out is absent rather than zero —
`values.Has(name)` is what tells the two apart.

### A form on a panel, which is where one comes from

`game.Message` puts a command on the wire for a client, a tool, or a bot that knows it. To have the
PLAYER send one, a panel asks for it:

```
Panel book = game.Panel("survey.fieldbook", "Field Book", 240, 180);
book.Asks("Note", "Record it");
```

One line, and the stock client draws a control for every field of the model — text a box, a whole
number a box that takes digits, a truth a checkbox, an enumeration a drop-down over its members —
with a button underneath carrying that caption. Pressing it sends the line, and the line arrives at
`OnMessage` under the model's name.

🔴 **Both halves come from the one call, because either alone is silent.** Controls with no message
would collect values nothing sends; a message with no controls is one the player has no way to
compose.

| | |
|---|---|
| A field typed as another **model** | left out — a picker needs the records, which a client does not hold |
| A **set** field | left out, and named, the same as it is for records |
| A caption | the field's own name as words, unless `Caption` said otherwise |
| A second `Asks` on one panel | refused by name — one panel, one message, one button |

⚠ **Asking is enough.** A panel that asks declares the message too, so `game.Message` is only wanted
when something other than a panel also sends it. Writing both is fine, in either order.

## 9. Creatures, and the world as it is

`Builder` says what the game IS, once, before there is a world. **`World` answers about the world
running right now**, and is reached by its own name from any handler:

```
public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
    Npc? it = World.NpcAt(map, x, y);

    if not it.HasValue()
        who.Message("There is nothing there.");
        yield;
    end if

    who.Message("You hit " + it.Name + ".");
    it.SetNumber("hp", it.Number("hp") - 3);

    if it.Number("hp") <= 0
        it.Kill("slain");
    end if
end function
```

🔴 **A handler is given a SQUARE, and this is what turns a square back into a body.** Everything else
a script holds — the `Player` a handler opens with — was handed over by the engine. A verb used on a
creature arrives at `OnAction` with the square it was used on, and nothing else, so without `NpcAt` a
game can say what happened and cannot say who it happened to.

| | |
|---|---|
| `World.NpcAt(map, x, y)` | the creature there, or nothing |
| `World.PlayerAt(map, x, y)` | the player there, or nothing |
| `World.NpcsNear(map, x, y, tiles)` | every creature within that many tiles, nearest first |
| `World.TileAt(map, x, y)` | what kind of ground is there |
| `World.CanSee(fromMap, fx, fy, toMap, tx, ty)` | whether a straight line between two squares is clear |
| `World.Distance(fromMap, fx, fy, toMap, tx, ty)` | how far apart they are, **counting across map borders** |
| `World.Weather(map)` | what the sky is doing |
| `World.Records(kind)` | how many records of that kind the world holds |
| `World.Record(kind, number, field)` | one field of one record, as text |
| `World.RecordNumber(kind, number, field)` | the same, as a whole number |

⚠ **`OnAction` has not changed and will not.** A handler is matched by name AND arity, so retyping
its `on` parameter would leave every script already written matching, loading, and being handed a
value of a type its body does not expect. The reach was ADDED beside it.

**An `Npc` is a handle, exactly as a `Player` is.** It answers `Name`, `IsHere`, `Map`, `X`, `Y`, the
same four attribute members a player has, and `Kill`. It is named by where it SPAWNS rather than where
it stands, so one kept while the body walks onto the next map still names it — and `IsHere` is what
says the body is still there.

It also answers `Kind`, which is the number of the creature record it is a copy of.

🔴 **A rule about a species keys on `Kind`, never on `Name`.** A name is what a player reads: two
records may share one, a name may be translated, and a rename in the editor would silently rewrite the
arithmetic of every rule written against it.

### What a creature does, and why

A creature's record says how it MOVES — it holds its tile, ambles, closes on what it notices, opens
the gap, or clears litter. Five ways of walking, and deliberately nothing about a reason: a reason is
a property of the game, and a word like "hostile" means nothing in a world with no combat in it.

So a game says why, with two verbs on the handle:

```
it.Chase(who);        # send it after a player
it.ChaseNpc(other);   # or after another creature
it.Forget();          # and let go, leaving it to its record again
```

⚠ **It overrides the noticing, not the legs.** A body authored to hold its tile still holds it —
pointing at somebody does not make a statue walk. One authored to open the gap runs from whoever it
was pointed at, because retreating is what that body does about somebody.

**This is what a creature that fights back is made of.** Author it to amble, so it notices nobody, and
chase whoever lands a hit:

```
public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
    Npc? it = World.NpcAt(map, x, y);

    if it.HasValue()
        it.Chase(who);
    end if
end function
```

A guard is the same verb with a different trigger: find the bodies near a crime with
`World.NpcsNear`, keep the ones carrying whatever key marks a guard, and send those.

### One creature reaching another

`OnContact` is raised when a creature reaches the **player** it was chasing. `OnNpcContact` is raised
when it reaches a **creature**.

```
public function OnNpcContact(Npc it, Npc other)
    Fight.Clash(it, other);
end function
```

🔴 **Two handlers rather than one**, because the target is two different kinds of body. One handler
would have to name a `Player` or an `Npc` in its signature and be handed the other, and a rule that
runs on the wrong kind of body is worse than one that is never called. A game that answers both the
same way writes one function and calls it from each.

A creature also answers what its record was **authored** as — `it.Behavior`, `it.Group`, `it.Range` —
and `it.IsChasing` says whether it is after anybody right now.

⚠ **A rule about a species keys on `Kind`, and a rule about how a body walks keys on `Behavior`.**
Neither is `Name`.

### Measuring, and seeing

🔴 **Never subtract coordinates.** The world scrolls contiguously, so a body one tile over a map
border is one tile away — and arithmetic says it is on another map and unreachable. That is the single
most common thing a range rule gets wrong.

```
integer gap = World.Distance(who.Map, who.X, who.Y, map, x, y);

if gap < 0 or gap > 5
    who.Message("That is too far away.");
    yield;
end if
```

`World.CanSee` is the **engine's own sight trace**, which is the same one the client colors its target
arrow with — so a rule gating on it agrees with what the player was shown rather than nearly agreeing.
A wall stops sight only if it was authored to: a railing is blocked to walk through and clear to see
over. A closed door always stops it.

`World.TileAt` answers `walkable`, `blocked`, `warp`, `item`, `npcavoid`, `door`, `plate`, or `ramp`,
on the ground layer. `World.Weather(map)` answers `clear`, `rain`, `snow`, `heatwave`, or `heavywind`.

**The engine sets this fight up on its own.** A body closes on anything within its range that is not
its own kind and not sharing its group — so a world with a wolf and a deer in it needs nothing but
this handler to have them fight. Grouping is authored on the creature record, and it keeps a pack from
turning on itself.

### Reading the records your own editor authored

Declaring a kind of record has been covered above. `World.Record` is how a rule reads one back while
the world runs — a game whose class stats or species traits live in records can do nothing with them
until it can.

```
integer span = World.RecordNumber("Species", 1, "wingspan");
```

Slots are 1-based and may be blank, which is what `World.Records` counts. A slot or a field that is
not there answers empty or zero rather than failing.

### Saying it to more than one person

`who.Message` is a whisper. Three audiences cover the rest:

```
World.Tell("The season turns.");                  # everybody in the world
World.TellOn(who.Map, "The gate grinds open.");   # everybody who can SEE that map
World.TellNear(map, x, y, "Something snaps.");    # everybody within earshot
```

🔴 **A room is who can SEE the map, not who is standing on it.** The world scrolls contiguously, so
somebody on the next map along is looking at this one — scoped to occupants, they would watch the gate
open in silence.

⚠ Keep `World.Tell` for the things that are genuinely everyone's business. A game that announces
ordinary events to the world has a chat log nobody reads.

**A guild is not a place**, so it takes a fourth shape: gather the set, then tell it.

```
Player[] mates = World.Guildmates(who);
World.TellThese(mates, who.Guild + " gains a member.");
```

`World.Party(who)` is the same for a party, and `TellThese` takes any set a game gathers for its own
reasons — a raid, everybody carrying a key. Anybody in it who has left the world is skipped rather
than refused: a set gathered a moment ago is a set somebody may have logged out of.

⚠ **An empty set and a blank `who.Guild` say different things.** No guild at all, against a guild
with nobody else online.

### Text that floats off a body

```
it.Float("-12", 255, 80, 80);
who.Float("hit!", 0, 255, 0);
```

Red, green, and blue, each 0 to 255, and out-of-range numbers are clamped rather than refused.

🔴 **This is the one place a script asks the client to DRAW.** Everything else a game does sets state
and lets the client decide what that looks like. A damage number is not state — it happened once, and
there is nothing for a client to derive it from.

It is addressed to a BODY rather than to a square, which is what makes it follow: the client centers it
on an oversize footprint, keeps it anchored across a seam crossing, and holds it until an in-flight
projectile lands so the text and the impact read as one event.

### What a fight looks like

Three effects, and they are the **only** draws a game may call:

```
who.Sweep(true);                                  # a crescent, the way they face
who.ThrowAtNpc(it, "bolt", 255, 220, 90);         # bolt, glitter, or parcel
it.Burst(190, 20, 20, 70);                        # droplets, power 0 to 100
World.Stain(map, x, y, 2, 80);                    # and this one LASTS
```

🔴 **A throw is named for what it is aimed at** — `ThrowAtNpc` and `ThrowAtPlayer`, on both —
because Compass has no union type and a player throwing at a creature is the common case.

🔴 **A number floated at a target waits for the throw to LAND.** That is most of why a throw is
worth using over a burst: a damage number that appears before its bolt arrives reads as two unrelated
events. Several throws at one target stagger across their own arrivals.

**None of them means anything on its own.** A crescent is a sword, a claw, a thrown net, or a shop door
opening; a burst is blood, sparks off an anvil, water, or dust. Which one it is belongs to the game,
which is what lets Core carry a genre it has never heard of.

| | |
|---|---|
| `Sweep(connected)` | true flings sparks, which is what makes a swing read as having HIT something |
| `ThrowAtNpc` / `ThrowAtPlayer` | style as text; an unknown one falls back to a bolt rather than refusing |
| `Burst(r, g, b, power)` | power 0 to 100 |
| `World.Stain(map, x, y, size, amount)` | still there when somebody walks back; the world's own color |

⚠ **Weather is not on this list and is not yours.** It runs on the world's clock and every client
draws it from state it already holds, so there is nothing to ask for.

### What a fight costs

The engine keeps five clocks about a player, and a game says when they start:

| | |
|---|---|
| `who.Engage(seconds)` | they are in a fight |
| `who.Down(seconds)` | they are out of it |
| `who.Mark(seconds)` | somebody's rule has flagged them |
| `who.Flag(seconds)` | they started it |
| `who.Wait(seconds)` | they may not act again yet |
| `who.Kill(cause)` | they die, unless `OnMayDie` refuses |

What any of them MEANS is the game's. The engine keeps the clock and the client draws it.

**A creature takes four of the five.** `it.Engage`, `it.Mark`, `it.Flag`, `it.Wait` — and engaging
one is what makes its overhead bars appear, which is most of what a fight looks like.

⚠ **`Down` stays a player's.** Downed is a body lying there waiting to get up; a creature that runs
out of health despawns and its slot counts down to a respawn, which the spawn clock already owns. A
second answer to "when does it come back" would be two clocks disagreeing.

## 10. Changing it

Edit the file, restart the server. There is no build step and no redeploy. The world folder is the
game — copy it, hand it to somebody, and they have your game.
