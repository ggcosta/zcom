# Recorded sound effects

Every clip in this folder is **CC0 1.0 (public domain)** — free to use, modify and redistribute, no attribution
required. Credit is given anyway.

| Folder (sound id) | Source pack | Original files |
| --- | --- | --- |
| `screech` | 80 CC0 creature SFX 2 — rubberduck | `alien_07`, `alien_09`, `alien_10`, `attack_03` |
| `alien_pain` | 80 CC0 creature SFX (1 and 2) — rubberduck | `bug_01` (pack 1); `bug_11`, `bug_13`, `attack_01`, `attack_05` (pack 2) |
| `alien_death` | 80 CC0 creature SFX 2 — rubberduck | `die_01` – `die_04` |
| `roar` | 80 CC0 creature SFX 2 — rubberduck | `roar_04` – `roar_06` |
| `spit` | 80 CC0 creature SFX — rubberduck | `spit_01` – `spit_03` |
| `gib` | 80 CC0 creature SFX 2 — rubberduck | `slime_01`, `slime_03`, `slime_04`, `slime_07` |
| `slam` | 80 CC0 creature SFX 2 — rubberduck; Impact Sounds — Kenney | `stomp_01`; `impactPlate_heavy_001`, `_003` |
| `hit_alien` | Impact Sounds — Kenney | `impactPunch_medium_000` – `_004` |
| `chitin` | Impact Sounds — Kenney | `impactGeneric_light_000` – `_004` |
| `step` | Impact Sounds — Kenney | `footstep_concrete_000` – `_004` |
| `explosion` | Sci-Fi Sounds — Kenney | `explosionCrunch_000`, `_001`, `_003` |

Sources:

- Kenney, *Impact Sounds* — https://kenney.nl/assets/impact-sounds (CC0)
- Kenney, *Sci-Fi Sounds* — https://kenney.nl/assets/sci-fi-sounds (CC0)
- rubberduck, *80 CC0 creature SFX* — https://opengameart.org/content/80-cc0-creature-sfx (CC0)
- rubberduck, *80 CC0 creature SFX 2* — https://opengameart.org/content/80-cc0-creture-sfx-2 (CC0)

Processing: silence trimmed, mono, 44.1 kHz, peak-normalised to −1 dBFS, Ogg Vorbis q6.

## Replacing or adding a sound

`ProcAudio` loads every clip under `Resources/Audio` at start-up. A clip named `<id>_NN` (for example
`screech/screech_02.ogg`) becomes one variant of the sound `<id>`, and a random variant is picked on each play.

- To replace a sound, drop new files named `<id>_00`, `<id>_01`, … into `Audio/<id>/` (and delete the old ones).
- Ids with no files here fall back to the clip synthesised in `ProcAudio.cs` (weapons, UI, ambience, player).
- `roar`, `alien_pain`, `gib` and `chitin` have no synthesised fallback: without files they are silent.

Only add files whose licence allows redistribution — this repository is public.
