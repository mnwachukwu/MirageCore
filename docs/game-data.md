# Game data conventions

Rules the authored content under the data and assets directories is expected to follow.

Three item slots are reserved by the engine and must be authored in the editor. Indices 4 and up are free for game content.

| Item index | Type | Description |
|---|---|---|
| **1** | Currency | **Gold.** Every system that charges or rewards gold (shop repairs, Inn set-spawn cost, marketplace) operates on this index. It **must exist** and **must be a Currency-type item** or all gold transactions silently fail. |
| **2** | Any | **Casting reagent.** Consumed by every SubHp (damage) spell cast; a caster with none is refused the cast. See *Caster resource model* under Balance Changes. |
| **3** | Currency | **Valor.** Guild-war currency, held in the guild vault. Should be authored character-bound — non-tradeable, non-listable, non-mailable and destroy-on-drop — since each of those is otherwise a route to moving earned war currency between players. |

The shipped seed authors all three (`Gold`, `Magical Reagent`, `Valor`) and the generator that produces it treats them as fixed slots, so a world built on top of that seed inherits them.

## Music loop points

Background tracks (`assets/music/music{n}.ogg`) loop seamlessly, in full by default. To give one a play-once **intro** — or to exclude a non-looping **outro** — tag it with Vorbis comments measured in **sample frames** (per channel; not seconds, milliseconds, or bytes).

| Tag | Meaning |
|---|---|
| `LOOPSTART` | frame the loop returns to, i.e. the end of the intro |
| `LOOPLENGTH` | length of the loop body in frames; loop end = `LOOPSTART + LOOPLENGTH` |
| `LOOPEND` | alternative to `LOOPLENGTH` — the absolute end frame. Use one or the other; if both are present, `LOOPLENGTH` wins. |

Give `LOOPSTART` plus **either** length tag, or neither (loops from `LOOPSTART` to end of file). The track plays once up to `LOOPSTART`, then repeats the body forever. `LOOPSTART=0` with a shorter length trims a non-looping tail. Untagged files — and out-of-range values — loop in full, so **no track requires tags**. Frames convert as `seconds × SampleRate`: a 6-second intro at 44.1 kHz is `LOOPSTART=264600`.

Write the tags in losslessly (no re-encode) with either:

```
ffmpeg -i in.ogg -c copy -metadata LOOPSTART=264600 -metadata LOOPLENGTH=4410000 out.ogg
vorbiscomment -a track.ogg -t "LOOPSTART=264600" -t "LOOPLENGTH=4410000"
```

Choose the two points a whole number of bars apart and on zero-crossings, or the seam will click. **In Audacity:** set the Selection Toolbar readout to **samples**, select from the loop-back point to the loop-end point, press **`Z`** (Select → At Zero Crossings), then read **Start** as `LOOPSTART` and toggle the second readout between **Length** (`LOOPLENGTH`) and **End** (`LOOPEND`). Audacity counts samples per channel, exactly the unit the tags use. The bundled tracks ship untagged — their silence padding was trimmed, so they already loop cleanly.
