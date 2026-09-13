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

**Install the VS Code extension.** Compass has a language server — the compiler's own, answering about
files as they are being typed — so a type error, an unassigned local, a switch missing an enumeration
member, or an optional read without proving it holds something are all reported where you wrote them.
Breakpoints, stepping, a call stack, and a variables pane work too.

⚠ **One thing does not work yet, and it is on this repository's side.** The engine's own types —
`Builder`, `Player`, `Records`, `Verb`, `Panel` — are handed to the compiler by the SERVER while the
world loads, so a standalone `cm check` has never heard of them and reports *"There is no type named
'Builder'"* on every declaring line. Everything else resolves, cross-file references included. Until the
engine exports its catalog for the tooling to read, **trust the editor about your code and ignore it
about the engine's**; the server's own log is what tells you whether a declaration landed.

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
three numbers are red, green, and blue; all three nought leaves the color to the client.

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
end model
```

```
Records sp = game.Records("Species", "Species", "Species", 200);

sp.Caption("name", "Common name");
sp.Length("name", 40);
sp.Length("notes", 240);
```

That is one editor section, one folder on disk, load and save, row locking, hot reload, world transfer,
and the world check — for a kind of record the engine has never heard of.

| In the model | On the form |
|---|---|
| `string` | a text box |
| `integer` | a spinner |
| `real` | a box that parses a fraction |
| `boolean` | a checkbox |
| an **enumeration** | a drop-down over its members |
| another **model** | a picker listing that model's records by name |

**Two seams close themselves.** A field typed as an enumeration declares its own set of choices; a field
typed as another model declares its own link. Neither is named twice, so neither can drift.

⚠ **A schema model's fields must be `public`.** The engine reads them and your script does not, and a
private field nothing names is one Compass will tell you to delete.

⚠ **The model is named as TEXT**, because Compass has no type values. A typo is a refusal at load, not
an error at compile — the message lists the models that do exist.

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
| `NeedsAtLeast(key, n)` | greyed below that, and lit the moment they have it |

Bindable keys are **B, E, J, K, N, P, Q, R, T, U, Y, and Z**. Anything the engine reserves, or a key
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

**A packet of your own is the one thing this route cannot do.** A typed message needs a client compiled
against it, which is the compiled route by definition. Verbs, panels, and attributes cover what a stock
client can be told about, and that is most of a game.

## 8. Changing it

Edit the file, restart the server. There is no build step and no redeploy. The world folder is the
game — copy it, hand it to somebody, and they have your game.
