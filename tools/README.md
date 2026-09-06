# tools/

**Scripts for people running the game — not for building it.**

Everything here is meant to be run by someone who has downloaded Mirage Source Remastered and wants to
do something with it. They are a feature of the project, documented in `docs/`, and they ship with the
source rather than existing to produce it.

| script | what it does |
|---|---|
| [`pack-client.ps1`](pack-client.ps1) | Rebrand and repackage the client under your own name, icon and version. Needs only the .NET SDK — it installs `vpk` itself. See [docs/branding.md](../docs/branding.md). |

## What does NOT belong here

This folder used to be a catch-all, which is how it ended up holding three unrelated things. The line
now is what the script is *for*, and there are two other homes:

- **Repository self-checks** live in [`.github/checks/`](../.github/checks) — `check-doc-links.mjs`,
  `check-seed-counts.mjs` and `check-readme-facts.mjs`. They validate this repo's own docs and seed data
  and are run by CI. They are nobody's feature; a player has no reason to run them.
- **Content and asset generators** live in the sibling **`MirageSourceRemastered.Tools`** repository —
  the seed-data pipeline, the icon and control-image scripts, and the balance simulations. Their output
  is committed here; the code that produces it is not part of the game.

The test: *would a person who downloaded the game ever run this?* If no, it goes to one of the other two.
