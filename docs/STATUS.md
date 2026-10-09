# Status — 2026-10-09

## Current delivery: video feedback fixes 0.3.1

- Coplanar overlap removed with UV-preserving polygon clipping. Output checks:
  zero overlapping coplanar triangles within each rigid bone; zero degenerates.
- Source muzzle ordering matched spatially rather than by variant-dependent
  index. Both barrels remain on-axis for the full source animation + interpolation.
- Terminal destroyed state and wreck age persist for all units, not just towers.
- Fixed grid footprints: 2x2x3 / 2x2x4 / 3x3x4. Static collision bodies, placement
  clearance/support/overlap checks and reserved-cell placement exclusion.
- Actual Neorxna 1.4 loaded in isolated runtime; three names, mod label and footprint
  display verified. Third-party mod archives and user installations unchanged.
- 120 fresh runtime checks passed; 1,783 resource checks plus mesh/barrel regression
  passed. Package: 7,832,302 bytes. See ENDFIELD_THREE.md for limits and upgrade notes.

## Previous delivery: authored gun towers 0.3.0

- Three tower models/atlases now use the approved local `endfield-turret-model/v1`
  assets. Their original source files are unchanged; authored snapshots live in
  `Mod/Assets/Models/Endfield`, hashes in `docs/custom-model-provenance.json`.
- Original Endfield FX/audio/projectile files match 0.2.0 byte-for-byte.
- Original RGBAHalf VAT tracks now drive head/barrel motion (44 frames, 3 tracks,
  5 source clips per tower). Native shader sampling is documented in
  `docs/ENDFIELD_THREE.md`. Cube-to-track binding is an explicit retarget.
- Blender cross-check: all 69,728 geometry/UV face corners match the authored
  source. Asset/provenance/package check: 1,783 checks passed.
- Isolated API 1.9.3.1 game: 81 checks passed, including fixed base vs moving
  barrels, original damaged-pose hold, firing, sound loading and save/reload.
- `dist/MechDefense-0.3.0.scmod`: 7,082,415 bytes. Build: zero warnings/errors.
- Remaining FX/shader and Wwise-mix differences are listed in the current doc.

## Previous delivery: Endfield gun towers 0.2.0

The current slice supersedes the single-turret placeholder effects/audio below.
See `docs/ENDFIELD_THREE.md` for reproducible source ownership and limitations.

- Three original tower assemblies and six original fire sockets are converted.
- Fourteen source FX prefabs, 107 emitters, source projectile meshes and textures
  now drive the Endfield-only effects path. No Destroy Gunners audio is used.
- Sixteen Wwise event graphs resolve to 92 original WEM/WAV stems. Forty-two
  deterministic source-audio mixes are available to the runtime.
- Expanded-tower fourth rounds and Rainstorm sixth rounds use the respective
  original enhanced-effect families. Rainstorm uses its skill's direct effects,
  rather than an invented named projectile template.
- `dist/MechDefense-0.2.0.scmod`: 14,405,734 bytes. Build: zero warnings/errors.
- Asset/provenance/package verifier: 1,275 checks passed. Fresh isolated game
  test passes, including all three autonomous firing paths and save/reload.
- This is a playable conversion, not exact Unity/Wwise playback. Original VAT
  animation, some shader/particle modules and Wwise DSP/RTPC remain unported.
  Complete source data stays in the separate three-gun-towers archive.

## Scope

1. Recover and visually verify one Endfield turret and its texture dependencies.
2. Recover and visually verify a player mech and an enemy from Destroy Gunners Sigma 1.10.
3. Implement persistent placement, boarding/dismounting, driving and firing, enemy combat, turret hostility/visibility checks.
4. Build a complete .scmod and verify it in an isolated Survivalcraft API 1.9.3.1 runtime.
5. Make the first three-unit creative-mode animation and ammunition slice playable.

## Evidence at initialization

- Endfield installed at E:/game/Hypergryph Launcher/games/Endfield Game; 65,296,522,560 bytes at inventory time.
- Destroy Gunners original APK SHA-256: d9ce01e477f22e5c96728ac879ae6200198e2a4b3141abbebf625a868fd8b8b0.
- Its BIN containers have 287 data, 456 graphics, 10 music and 32 sound entries; container decoding succeeded in the previous read-only audit.
- Scene assembly, model conversion and in-game behavior are now verified by the
  isolated runtime smoke test described below.

## Verification record

- `dotnet build Mod/MechDefense.csproj -c Release`: passed with 0 warnings and
  0 errors.
- `python tools/package.py`: produced `dist/MechDefense-0.1.0.scmod` (2,594,444
  bytes) and a SHA-256 asset manifest.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run_smoke.ps1`: passed
  against a copied Survivalcraft API 1.9.3.1 runtime in `local/runtime`.
- The smoke run exercised mesh/texture loading, all three creative deployment
  entries, independent mech boarding, driving, firing cooldown, turret target
  selection, enemy advance and fire, friendly-fire exclusion, line-of-sight
  blocking, safe dismount, save/reload persistence, post-reload world
  execution, and lethal enemy removal.
- Visual capture: `local/runtime/ScreenCapture/mech-defense-smoke.png`.
- Machine-readable result: `local/runtime/smoke-result.json`.

## Animation and ammunition slice

- `python tools/extract_shade_motions.py` extracted and SHA-256 indexed all 26
  original `PSMd` motion containers into `local/dguns/motions`.
- The source sets are grouped as candidate enemy (`339-348`), turret/support
  (`370-372`, `453-456`) and mech (`411-418`) banks in
  `Mod/Assets/Animations/catalog.json`.
- The playable build exposes deploy, track, idle/walk, fire, reload, hit and
  death event states for all three units. Their event poses are retargeted to
  the recovered bind joints; raw source containers remain preserved and are not
  mislabelled as a completed byte-level PSMd curve decode.
- The mech has five creative-only ammunition profiles: rifle, missile, cannon,
  laser and grenade. While mounted, `B` cycles profiles, `R` plays reload, and
  the HUD shows the active profile. Their runtime shot visuals now use separate
  rifle, missile, cannon, laser and grenade timings/shapes; no inventory
  ammunition items are required.
- The Endfield turret package now carries the two recovered `firePos_1` /
  `firePos_2` sockets from the exported `battle_turret_1_03` metadata. Aiming,
  ray origin and the visible twin muzzle flash use those sockets instead of a
  guessed body offset.
- Endfield defense resources are now archived separately under
  `local/endfield/tower-library`: 19 named families (including 铳械塔,
  扩装铳械塔, “暴雨”铳械塔, 榴弹塔, 高爆榴弹塔, 液氮塔, 射线塔 and others),
  1,181 MD5-verified raw bundles (about 240 MB), per-family catalogs, combat
  configuration references, model/material/texture/effect categories and
  `audio-cues.json`. The archive is source material only; it is not yet all
  converted into Survivalcraft meshes or effects.
- The temporary Destroy Gunners `Fire.ogg` is no longer played by the Endfield
  turret. Its Endfield Wwise cue IDs are preserved in the audio catalog while
  native cue/media conversion is pending.
- Enemy death now rotates the recovered root body into a grounded side-fall
  pose, with a smaller head/leg follow-through rather than the generic turret
  death rotation.

## Acceptance

- [x] Deployed mech survives save/reload and stays after dismounting.
- [x] Player boards, drives and fires, then exits into a safe free position.
- [x] Ground turret tracks and damages a visible hostile enemy; it does not fire through walls or at neutral/friendly units.
- [x] Enemy advances toward a hostile target, fires, takes lethal damage and is removed.
- [x] All three use validated recovered models; asset provenance and adapted versus recovered motions are documented.

## Remaining boundary

The smoke test proves the sample's runtime path, not a full content-complete
conversion of either source game. Additional turret variants and bulk extraction
of shared Endfield dependencies remain follow-up work. The converted mesh files
keep source hashes and mark gait/aim behavior as `adaptedMotion`; the raw PSMd
motion files are retained, while their event curves still need a full decoder
before they can be claimed as original curve playback.
