# Technical decisions

Choices that are not obvious from reading the code, recorded with the reasoning that
produced them.

| Area | VB6 | C# |
|---|---|---|
| Language / runtime | Visual Basic 6 | C# 14 / .NET 10 |
| Wire protocol | Binary, Chr(255) delimiters | Newline-delimited JSON over TLS (encrypt-only, ephemeral self-signed cert) |
| Data storage | Binary `.dat`, INI files | UTF-8 JSON |
| Rendering | DirectX COM (DirectDraw) | MonoGame 3.8 DesktopGL |
| Music | MIDI via `mciSendString` (Windows only) | OGG Vorbis, gapless streaming via NVorbis |
| Editor | Admin-gated forms inside the client exe | Standalone Avalonia 12 app |
| Concurrency | Single-threaded Winsock callbacks | Async TCP, per-player channel |
| Platform | Windows only | Windows, macOS, Linux |

## Why compiled content is committed

Build outputs do not belong in a source tree. This one is the exception, and it is worth stating why so
nobody "tidies it up" later — or copies the pattern somewhere it does not apply.

**The client's content pipeline cannot run anywhere but Windows, for two unrelated reasons.**

- The effect compiler is `SharpDX.D3DCompiler` plus `libmojoshader_64.dll`. The `mgcb` tool ships
  freetype, FreeImage, nvtt and PVRTexLib as `.so` and `.dylib`, but neither of those two. MonoGame's own
  answer off Windows is to run the compiler under **Wine**.
- Four of the five spritefonts are drawn from **Tahoma**, resolved out of `C:\WINDOWS\Fonts`. Tahoma ships
  with Windows and with nothing else, and it is proprietary, so it cannot be bundled beside the
  descriptions the way `FSEX300.ttf` is.

That leaves three options, and the cost of each is the argument:

| | Cost |
|---|---|
| Require Wine and a font install to build | Breaks "the .NET SDK and nothing else" on two of the three platforms it is claimed for, for a first-time contributor, on a clone |
| Replace Tahoma with a bundled free face | Changes the typeface of every string in the game — a design decision made to satisfy a build constraint |
| **Commit the compiled output** | ~263 KB of binaries in the tree, and a staleness risk |

The third is the one taken. It is 263 KB against a 15 MB spritesheet, every platform renders identically
because they all ship the same atlases, and the staleness risk — the only real objection — is the one
that can be *mechanically* removed: `check-prebuilt-content.mjs` hashes every content source against a
committed manifest and fails the build when they disagree.

The generalizable rule: **committing a build output is defensible when reproducing it requires something
the platform cannot have, and only when a check can prove the committed copy is current.** Without that
second half this would be an ordinary bad idea. See
[Building](building.md#compiled-content-is-committed) for the workflow.

Nothing else in the tree works this way — the seed data is generated but its generators run anywhere, and
`git status` after a regeneration is the same review this uses.

## Diagnostics: every app keeps a log

All three deployables log to a daily-rolling Serilog file, with the same output template, so one report
reads like another and a timeline can be assembled across them. This is the whole troubleshooting story:
**there is always a file to ask for.**

| App | Files | Kept | Where |
|---|---|---|---|
| Client | `client-*.log` | 10 days | its **cache** dir (beside the map cache) |
| Editor | `editor-*.log` | 3 days, configurable to 30 or forever | its **data** dir (`logs/`) |
| Server | `server-*.log` | 7 days | its **data** dir (`logs/`) |
| Server | `network-*.log` | 3 days | packet traffic, split out because it is noisy |
| Server | `chat/*.log` | 30 days | what was said, kept longest — it is the moderation record |

Each also has a way to watch the log live, which is the same stream, not a second one:

- **Client** — backtick (`` ` ``) opens an in-game console, on every screen including the login screen.
- **Editor** — View > Console, a modeless window beside World Preview and Layer Visibility. How much
  detail it shows is Help > Logging's business, not the window's: the level switch governs the file and
  this sink together, so raising it to `Debug` fills both with no restart, and the window can never
  disagree with the file it is showing.
- **Server** — the shell's Console tab, which attaches and detaches without stopping the server.

Three properties are what make these useful for a bug report rather than only for live watching:

- **They record before anyone thinks to look.** Nothing here starts when a window is opened. The client's
  console reads a sink that has been filling since launch, so opening it after something odd still shows
  the odd thing.
- **A crash is the last thing in the file.** This matters most on the client: it is a `WinExe`, so standard
  output goes nowhere on Windows, and a crash reaches the server as an ordinary disconnect —
  indistinguishable from someone closing the window. Unhandled exceptions on the game thread and on
  background threads both land in the log, which is the only artifact a crashed client leaves behind.
- **A failure that was handled still leaves a mark.** The paths that swallow an exception to keep running —
  a map that would not cache, a neighbor that would not resolve — log it on the way past. A silent
  `catch` is how a reproducible bug becomes an unreproducible one.

🔴 **Client log lines are shown on screen, so treat them as public.** A player can open the console and
screenshot it. What the client already knows and displays is safe to log; a credential, a session token,
or a server-side input the client is merely passing through is not. The server's log has no such audience
and is held to the ordinary rule instead: it is an operator's record.

**The client console displays and nothing else** — no command line, no input of any kind. It answers "what
just happened"; a console that also *did* things would need a permission model, a parser, and a reason to
trust whoever is typing. The server shell is where commands belong, and it has them.

## Per-user file locations (why the map cache is under Local, not Roaming)

> Author's note-to-self: this is written down purely so future-me remembers *why* the split is the way it is — it's an easy detail to second-guess or "tidy up" into one folder later. Don't.

Client and editor writable paths sort into three kinds, each mapped to the OS-conventional location:

| Kind | Purpose | Windows | macOS | Linux (XDG) |
|---|---|---|---|---|
| Config | Settings that follow the user across machines | Roaming `%AppData%\<GameName>\` | `~/Library/Application Support/<GameName>` | `~/.config/<game-name>` |
| Data | Persistent, machine-local app data | Local `%LocalAppData%\<GameName>\` | `~/Library/Application Support/<GameName>` | `~/.local/share/<game-name>` |
| Cache | Regenerable data (the downloaded map cache) | Local `%LocalAppData%\<GameName>\` | `~/Library/Caches/<GameName>` | `~/.cache/<game-name>` |

The client uses Config (its `appsettings.json`) and Cache (the map cache); Data is mainly the editor's authored maps and items.

**So on Windows there are two `<GameName>` folders by design** — Roaming for config, Local for cache/data. The map cache goes to **Local**, not Roaming, because: (1) it's **regenerable** — re-downloaded from the server on demand, so syncing it across machines is pointless; (2) it can get **large**, and Windows copies the whole Roaming profile on login/logout, so a fat cache there would slow logins and bloat (or blow the quota on) the roaming profile; and (3) it's inherently **machine/session-specific** — which maps you've observed, at which revision. Roaming is meant for a few KB of settings you'd want identical on any machine you sign into; dropping a regenerable download cache in it is exactly the anti-pattern the Roaming/Local distinction exists to prevent. (This two-bucket split is Windows-specific: macOS folds Config+Data together and only breaks out Caches, while Linux/XDG splits all three.) The upshot for clearing the map cache: delete `%LocalAppData%\<GameName>\maps\` — the Roaming `appsettings.json` is unaffected.
