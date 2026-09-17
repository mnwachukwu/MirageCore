# MSR

Mirage Source Remastered, rebuilt on Core as a world's own rules.

This is the proof the engine carries a genre. Survey shows that a game can be written in Compass;
this shows that a whole RPG can — stats, pools, fights, classes — on an engine that has never
heard of a hit point.

**It is a port, not a design.** Ground truth is `D:\Repos\MirageSourceRemastered`, which still stands
at the state this engine was cut from. Every number here was read out of that source and is held to
it by [`MsrStatsTests`](../../tests/src/Mirage.Server.Tests/Modules/MsrStatsTests.cs), which computes
nothing of its own: the expectations were produced by running the original's `StatFormulas` and
`ExpFormulas` and are pasted in. A character built here has the pools it would have had there.

⚠ **The risk that test exists for is arithmetic that is nearly right.** A port that drops the curve's
shift, rounds the wrong way, or divides before it multiplies gives numbers that look fine at level 1
and are wrong by hundreds at level 255 — which nobody notices until somebody plays that far. So the
sample spans the whole curve rather than its comfortable middle.

## What is here

Two folders, split by who writes what. `declarations/` holds the record models a *world author*
fills in through the editor; `behavior/` holds the rules, which nobody authors. `rules.cm` sits
above both as the engine's door.

| File | What it is |
|---|---|
| `scripts/rules.cm` | the engine's door, and nothing else |
| `scripts/declarations/character.cm` | a class, and the kit it opens with |
| `scripts/declarations/magic.cm` | a spell, who may learn it, and what a class already knows |
| `scripts/declarations/gear.cm` | what this game adds to the engine's own items, and who may wield them |
| `scripts/declarations/quests.cm` | a quest, what finishing it takes, and who may take it |
| `scripts/declarations/places.cm` | what this game adds to the engine's own maps |
| `scripts/declarations/beasts.cm` | what this game adds to the engine's own creatures |
| `scripts/behavior/stats.cm` | what a character sheet IS, and the arithmetic behind it |
| `scripts/behavior/sheet.cm` | what the engine is told to carry, and what a new character starts with |
| `scripts/behavior/vitals.cm` | the three pools, their ceilings, and what puts back into them |
| `scripts/behavior/combat.cm` | what a swing is worth, and what a body turns aside |
| `scripts/behavior/fight.cm` | where that lands on somebody: the rolls, the picture, the death |
| `scripts/behavior/duel.cm` | two players, and who is allowed to fight whom |
| `scripts/behavior/death.cm` | what dying costs, and where you wake up |
| `scripts/behavior/places.cm` | what a map means to a fight |
| `scripts/behavior/beasts.cm` | what a creature is, on top of how it walks |
| `scripts/behavior/classes.cm` | applying the class they chose, and what it is worth afterwards |
| `scripts/behavior/spells.cm` | what casting costs, and what a cast is worth |
| `scripts/behavior/book.cm` | what a character knows, has ready, and does with it |
| `scripts/behavior/gear.cm` | who may wield what, and how gear wears out |
| `scripts/behavior/quests.cm` | taking one, counting toward it, and handing it back |
| `scripts/behavior/levels.cm` | experience, and what it buys |
| `scripts/behavior/chat.cm` | the channels this world’s own lines read on, and the tab they start in |
| `scripts/behavior/guilds.cm` | what a guild is for: a level, a war, and who may fight whom |
| `scripts/behavior/ledger.cm` | what a guild is charged, and what it is paid |
| `scripts/behavior/perks.cm` | what a guild is worth to the people in it |
| `scripts/behavior/valor.cm` | the war currency |
| `scripts/behavior/guildquest.cm` | a guild's standing quest, and what finishing it pays |
| `scripts/behavior/calendar.cm` | the calendar, worked out from the clock |
| `scripts/behavior/staff.cm` | what somebody staging the world can force rather than wait for |
| `world.json` | the looks on offer at character creation, two per class |

`world.json` is the world's own manifest rather than a script, because a look is art a world author
picked and not a rule. Each entry names a sprite and the class it belongs to, so the screen shows a
man and a woman once a class is chosen - which is the original's own two buttons, reached the way
this engine reaches everything a world decides.

⚠ Every look in it is gated on a class, so a world holding no authored classes offers none and
character creation says so. Author the classes first.

`stats.cm` is pure arithmetic with no opinion about a world; `sheet.cm` declares the keys and puts the
opening numbers on a body. One is a formula and the other is a decision, so they are separate
files.

A pool is two numbers, and only one of them is really stored. The ceiling is derived — the
formula in `stats.cm`, recomputed whenever anything it reads changes — and it is carried as an
attribute only because the CLIENT draws the bars from it and cannot run a formula. A stored maximum is
how the two drift apart, and a character whose maximum disagrees with their level stays wrong
until they heal.

### The keys

**E swings, Q casts, R reaches**, as the original had them. **J** opens the spellbook, **U** the
journal, and **K** the guild vault.

⚠ **G is not a key a game may bind.** Core reserves most of the keyboard — WASD, Shift, F, the action
bar, and a dozen letters that open its own windows — and a game chooses from `GameKey.Offered` and
nothing else. The guild panel would have been G and is K instead.

### The action bar

Four slots, on **1** through **4**, declared by `game.HotkeyBar(4)` in `rules.cm`. The original had
three potion keys and this is that with one spare; a world that wanted none would declare none, and
the row would not be drawn at all.

Two things go in a slot. An **item** is bound by right-clicking it in the bag, and firing the slot
drinks it the way the bag does — the box shows the item's own art, how many are left, and grays when
there are none. A **page of the spellbook** is bound by right-clicking its row, which `book.cm` allows
with `book.Hotkeys(Book.Cast)`; firing it prepares that page and throws it, so casting a particular
spell is one press rather than two.

⚠ Casting from a slot changes what is prepared, because the original made you prepare first. The slot is a shortcut through both steps, not a second way to cast.

⚠ Binding E takes the engine's reach key outright. That is the engine's own rule and it is
deliberate: sharing a key between a game's verb and Core's reaching is worse than taking it. But
taking it would cost the world its shops and its conversations, so a verb can now say
`reach.Interacts()` — picking it does what E used to. MSR puts that on R.

### The five behaviors

The original gave a creature one of five behaviors, and four of them said something about fighting.
Core's five say how a body moves and nothing about why, so the word still means something in a game
with no combat in it. `beasts.cm` is the translation, and it needs no engine word of its own:

| The original | How it is written here |
|---|---|
| Attack on sight | authored **Pursue**, and `Rules.OnContact` swings when it arrives |
| Attack when attacked | authored **Wander**, and `Beasts.Answer` points it at whoever lands a hit |
| Friendly | authored **Wander**, with no rule pointing it at anybody |
| Stationary | authored **Stationary** |
| Guard | authored **Wander** carrying the `guard` key, roused by `Beasts.CallGuards` |

`it.Chase(who)` does all of it. A body authored to amble never notices anybody, so a game
that could not point one at somebody could not write a creature that fights back. Chasing overrides
the noticing and not the legs: one authored to hold its tile still holds it, and one authored to open
the gap runs from whoever it was pointed at, as a body that flees should when it is hit.

⚠ **A guard is marked on the creature RECORD**, in the attributes an editor hangs on the template —
`guard = 1`. Every copy of that creature is a guard; a body that became one at spawn would be one by
accident.

### Creature against creature

Core sets this fight up on its own. A body closes on anything within its range that is not its own
kind and not sharing its group, and raises `OnNpcContact` when it arrives — so a world with a wolf and
a deer in it needs nothing but that handler to have them fight. `Fight.Clash` is the same four steps
as the other two directions, and a kill pays nobody: a creature has no sheet to credit.

### Classes

A class is authored, not written. `declarations/character.cm` declares the *shape* of one — a
name, a pitch, and four numbers — and the editor builds a Classes section from that model without
having been compiled for this game. How many there are and what they open with is a world author's,
exactly as it was in the original.

A second family, `ClassKit`, is one row per grant: which class, which item, how many. A row rather
than a list on the class, because a record holds fields and not lists — and because one item can then
be granted by several classes without being authored twice. Its `forClass` field is typed as the
`Class` model, so the editor draws a picker listing the classes by name.

⚠ **A class counts twice, and the second time is the one that matters.** Its spread becomes the
character's four stats at enlistment, and it is *also* added to every pool ceiling for as long as they
play. So two characters at the same level with identical stats still have different pools if they
enlisted differently, so a class still matters after the opening twenty points stop
mattering. A port that applied only the first half would read correctly on the character sheet and be
wrong on every bar.

**A class is picked on the character-create screen**, as the original picked it. The engine draws a
list of whatever classes a world author wrote, and writes the number onto the new character before
anything is told they joined — so enrolling *applies* a class rather than asking for one. What
somebody IS has to be settled before there is somebody; a spread applied afterwards is a correction.

A kit line marked **worn** arrives on, as the original handed it over. Asking somebody who has never
played before to open a bag and work out what goes where is a worse opening than any gear is worth.

### Magic

A spell is a swing delivered at range. Mind and strength are the same offense stat running the
same curve, so a caster and a warrior of equal investment deal identical damage. Range is paid for in
mana and in the wait after a cast, never in the numbers — `spells.cm` reuses `Combat.Swing` rather
than mirroring it, so the two cannot drift.

⚠ **The authored magnitude does three jobs**: how much the spell does, the intelligence needed to
learn it, and what it costs in mana. One number, so nobody can author a spell that is powerful, cheap,
and free to pick up — and a tier ladder falls out of the magnitudes rather than being balanced by
hand.

Two types break the pricing rule, and both have to:

| | |
|---|---|
| Restoring **mana** | prices off what it hands over, not off the authored amount. Its output is its own input, so an amount-only price is a constant while the restore grows without bound in the caster's intelligence — and no constant outruns a curve. |
| Draining **health** | pays a flat share of the pool rather than the utility price. It is the caster's sustainable weapon, so mana is a distant ceiling on a marathon rather than a gate on every cast. |

**The gate is a family of its own.** `SpellGate` is one row per spell-and-class pairing — and **no row
for a spell means every class may learn it**, which is the common case and so the one that costs
nothing to author.

**Q casts, J opens the book.** The book has twenty pages; preparing one is a form rather than twenty
verbs saying the same thing.

**A scroll teaches**, as the original had it: use one out of the bag and the spell written on it goes
into the book, spending the scroll only if it was read. Which spell is written on it is a field this
game **added to the engine's own Items** — a fact about the scroll, stored on the scroll.

A cast is gated on the **engine's own reach and its own sight trace** — counted across map borders, and
the same trace the client colored the target arrow with. A refusal therefore agrees with what the
player was shown rather than nearly agreeing.

### Quests

The NPC roles live on the quest, not on the creature. A quest names who offers it and who takes
it back, so adding one is a row and touches nothing else — and a creature can give as many quests as an
author likes without ever being edited. A chain is nothing more than `prereq` pointing at another
quest.

Three families again: the quest, a row per **goal**, and a row per **class gate**. Killing is the only
thing the original ever counted, so it is the only goal kind here — fetching and exploring were
declared in MSR and never wired, and a goal nothing advances leaves a quest unfinishable.

⚠ **A kill counts from `Fight.Slain`**, which is the one place a player kills a creature. A swing and a
spell both end there, so neither can be the one that was forgotten.

**The journal is a window, not a list.** A character carries one number per quest and one per goal — a
fact about the character belongs on the character — and the panel shows the eight underway, rewritten
whenever they change.

**A conversation can open it.** A choice names a game's verb by its id, and picking one now opens
whatever panel that verb opens, so a giver's own authored dialogue can end in "tell me more" and
the journal comes up.

### What the port could not carry

Each of these is an engine gap rather than something the port chose to skip:

| | |
|---|---|
| Reagents | a damage cast consumed one, at a rate tied to item durability the port has no hold on. |
| Safe zones | regen was faster on a safe tile. What a map MEANS is now a field on the map (`declarations/places.cm`), and combat reads it; regen does not yet. |
| Seasonal quest cadence | a season is thirteen weeks counted from the epoch rather than a season the world is keeping, because territory seasons are not ported. |
| Local midnight | every boundary here — a day, a week, a guild's tax, a quest's cadence — turns over at UTC midnight. The original used the server's own local midnight. |
| Dropping a bag on death | the original scattered inventory on a death, per slot and per quantity. Core offers no way to read a bag or put something on the ground, so nothing here drops anything. |
| Monitors and above out of PvP | an account's access level is not something a script can ask about. |
| Two of the five guild privileges | what a creature drops and how a swing wears a weapon are the engine's. A rule can raise a drop rate or skip a point of wear only if it does the dropping and the wearing itself. The other three — bonus experience, the vault trickle, and the tax gate on all of them — are ported. |
| Territory and seasons | a contest needs a walkable graph across map seams, capture points standing on tiles, NPC suppression over a set of maps, and a contested-zone overlay the client draws. None of those is reachable from a script, and none is a small seam. |

## Where it is up to

Seven modules were planned, in an order where each one only needs what the ones before it declared.

| | | |
|---|---|---|
| 1 | **Stats** | ✅ the four stats, the three pools, regen, the experience curve, the point budget |
| 2 | **Vitals** | ✅ three pools, three bars over every head, three meters on the sidebar, and regen that stops for a fight |
| 3 | **Combat** | ✅ all three directions, blocks, dodges and crits with their stamina costs and gear gates, per-hit wear, weather, kill credit, experience, levelling, death, and the five behaviors |
| 4 | **Classes** | ✅ two authored record families, the spread that counts twice, the pick at creation, and the kit |
| 5 | **Spells** | ✅ three more families, the pricing, the gates, the book, and casting on Q |
| 6 | **Quests** | ✅ three authored families, chains, the class gate, the kill count, a journal, and the four repeat cadences with their own reward set |
| 7 | **Guilds** | 🚧 levels, wars, peace, wagers, the officer queue, war deaths, the weekly settlement, valor, the privileges, and bounties. **Territory and seasons are not ported** — see below |

## Running it

Copy `world/` over a server's world folder and restart. There is no build step — the server compiles
the scripts when it starts.

⚠ `world/.mirage/` and `world/world.cmp` are **generated by the server** so an editor can check these
files. They are committed so the folder opens in VS Code before a server has ever run it; do not edit
either by hand.
