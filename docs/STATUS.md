# Status — 2026-10-12

## In review: 0.7.0 (branch feat/tower-combat-maintenance, not merged)

- User (2026-10-12): shells cannot keep up with running targets; ranges too short; Wonderful Era's captain and pirates are not
  attacked, tell friend from foe; damage can be higher since towers fire rarely; towers barely hurt the ghouls; towers cannot be
  taken down — make the vanilla pickaxe dismantle them, keep their health, and add repair by right-clicking with materials
  priced after how other mods change the vanilla materials. Task brief: docs/tasks/2026-10-12-combat-maintenance.md.
- Numbers (docs/TOWERS.md, 0.7.0): ranges about ×1.6-2.7 (gun towers 28/32/36, sentry 48, mortar 48, prologue cannon 56),
  damage ×1.5-3.2, bolt 110-130 and shell 24-36 m/s; attack intervals unchanged.
- Lead: bolts and shells aim where the target will be (its observed movement × flight time).
- Friend or foe (`Mod/MDFaction.cs`) from each creature's own behaviour, no list of mod names: never players, towers, the
  player's herd or a player's mount; targets: recent attackers of the player's side, creatures chasing the player's side,
  predators, creatures whose chase behaviour hunts players on sight (range > 4 m).
- Ghouls: gunfire aims at the head where the head is the weak spot; explosions, beams, lightning, flames, jets and poison are
  no longer ProjectileAttackment, so the ghouls' bullet defence (body ×0.1-0.5) does not apply to them. Their injury caps do.
- Dismantle: 3 pickaxe hits; the deploy item keeps the health (name and slot bar), redeploying restores it. Repair: right-click
  with iron/copper ingot (200/120 hit points) or block (×9).
- Verification (isolated runtime of the user's build, API 1.9.3.1, with the user's ghoul 2.0 and Wonderful Era 1.9.3a mods
  plus its two dependencies; `run_isolated.py lab --mode combat`, Harmless world):
  - Friend or foe: targets — wolf, rhino, pirate sailor/elite/captain, ghoul weak/fast/strong_08/monster/bear; not targets —
    hired pirate sailor/elite, deputy captain, bull, bison, boar; the deputy captain becomes a target after hitting the player,
    and when its chase behaviour targets the player; a bull staring at the player (ComponentStareBehavior) does not.
  - Ghoul damage per unit of attack power (health lost × resilience / power): weak_01 body 0.5 / head 1 / explosion 1;
    strong_08 0.1 / 0.5 / 1; monster 0.2 / 0.4 / 1. Real rounds on a pinned monster at 15 m: gun tower 7/7 and sentry 3/3
    hit within the head band (hit height 71.02, eye 70.97).
  - Running target (pirate, 5 m/s across the line of fire at 20 m, beyond the old 15 m range), 12 s: gun tower 9/12 hits
    with lead, 0/12 without; grenade tower 5/8 with lead, 2/8 without.
  - Dismantle through real input (Hit injected into ComponentInput): 3 iron-pickaxe hits take a 62% tower down, pickaxe
    wear 3, the player gathers “铳械塔 · 部署（耐久 62%）” with a 0.62 health bar; the worn item does not fit the upgrade
    recipe, a full one does; redeployed through Interact at 62%; repair clicks with 3 iron ingots: 62% → 93% → 100%, then
    no change and no ingot used (1 left).
  - Every damaging tower against a pinned wolf at 12 m (trap 5 m), 6 s: all 15 hit it — e.g. gun towers 384 / 748 / 1,235,
    grenade 440, high-explosive 1,320, sentry 1,020, prologue cannon 1,400, acid 576, poison 241 (shell + poison ticks),
    mortar 480, ray 720 / 1,040, lightning 600 / 1,084, trap 1,040.
  - Regression: lab special passes (19 recipes resolve; effects on real wolves as in 0.5.0/0.6.0: frost 25% and 4 s,
    poison continues after leaving the cloud, acid ×1.30, jet pushes 2.92 m and 1500 kg not at all; prologue cannon hits at
    14 / 26 / 38 m, shell along the barrel 0.00°; Neorxna shows the 19 names). Lab fire passes; against 0.6.0 in the same
    2 s window the gun tower and the prologue cannon now also damage the dummy; the ray tower fires 0 times in that window
    in both versions (its charge), the poison tower's cloud is too late for it in 0.7.0 (it does damage in the 6 s check).
  - Old-save migration (`smoke`, 0.5.0 world → 0.7.0): 3/3 and 16/16, as for 0.6.0.
  - Frame time (`perf`, the same scene, four alternating pairs): hotbar only 0.6.0 1.07 / 1.43 / 1.40 / 1.05 ms, 0.7.0
    1.31 / 1.45 / 1.19 / 1.05 ms; 30 towers 0.6.0 1.22 / 1.72 / 1.15 / 1.16 ms, 0.7.0 1.60 / 1.45 / 1.20 / 1.14 ms — no
    difference beyond the run-to-run spread (about 0.3 ms on this machine today).
- Package `dist/MechDefense-0.7.0.scmod`: 26,263,051 bytes, 154 entries. Against 0.6.0 only MechDefense.dll, modinfo.json,
  Assets/Towers/towers.json and the manifest differ; no 终末地 in any entry.
- Not verified: the user's real game with all its mods; ScCsgoTactical soldiers/companions (the CS package was not loaded in
  the lab; the rule reads their TargetBody / no chase behaviour); Industrial Era plates as repair material (not supported).

## Current delivery: 0.6.0 (Destroy Gunners units removed, no 终末地 in shown names)

- User (2026-10-11): “先把毁灭枪手里的机甲剔除掉，然后名字显示也不要出现终末地，再打包一版scmod”.
- Removed the Destroy Gunners Σ sample mech and enemy: driving, boarding, cameras, HUD, ammo keys (B/R), enemy AI and
  firing; their models, textures, icons, motion catalogue and fire sound are out of the code and the package
  (`tools/package.py` refuses them). The DG extraction/conversion tools left `tools/` (in history at v0.5.0).
- Old worlds: the MDMech/MDEnemy templates stay unchanged, so worlds load; such entities remove themselves on load, and a
  player riding a mech is set down where it stood, in first-person view. Deployment items with data 1/2 read
  “已移除的旧单位”, cannot deploy and are not used up; they are no longer in the creative list.
- Nothing shown in game says 终末地: mod name “机甲防线 · 防御塔”, deploy item “防御塔 · 部署” and its description, the
  Neorxna info panel. Tower names never had it. Package scan: no 终末地 / 毁灭枪手 / 机兵 in any entry (DLL strings included).
- Towers attack hostile creatures only (predators, or anything chasing a player). Tower assets are byte-identical to 0.5.0.
- Verification (isolated runtime of the user's build, API 1.9.3.1):
  - Old-save migration (`run_isolated.py smoke`): 0.5.0 builds a world with a gun tower, a mech the player rides, an
    enemy and old deployment items (3/3 checks); 0.6.0 loads it: 16/16 checks — mech and enemy gone, player off the mech,
    unhurt, first-person view; old items read “已移除的旧单位”, do not deploy and are not used up; creative list = the 19
    towers; no 终末地 / 毁灭枪手 / 机兵 in names; a frost tower still deploys; the old gun tower fires at and damages a
    wolf; still only towers after another save and reload. Game log without errors.
  - Lab all + special: 19 towers load and draw, no model or draw errors; the Neorxna panel shows the 19 tower names;
    19 recipes resolve; effects on real wolves as in 0.5.0 (frost freezes 1.5 s, boss only slowed to 25%; poison and
    slow go on after leaving the cloud; acid ×1.30; shockwave 2 s stun; jet pushes 2.92 m, 1500 kg not at all);
    artillery hits at 14 / 26 / 38 m, shell along the barrel (0.00°); held towers unchanged.
  - Frame time (`perf`, two alternating runs): hotbar only 1.05 / 1.21 ms (0.5.0: 1.05 / 1.22), 30 towers 1.14 /
    1.27 ms (0.5.0: 1.18 / 1.22) — no change.
  - Package `dist/MechDefense-0.6.0.scmod`: 26,256,959 bytes, 154 entries (0.5.0: 26,866,219 bytes, 162 entries).
- Tooling: `tools/run_isolated.py smoke --mode old|new` (migration test); the lab's target dummy is now an immobile, very
  tough wolf; the runner skips Neorxna builds for API 1.9.4 (v1.4.3 in the user's Mods stops this 1.9.3.1 runtime at start)
  and uses the user's v1.4 copy in `D:/下载/Mods`.
- Not verified: the user's real game with its other mods; a game on API 1.9.4.

## Previous delivery: 0.5.0 (crafting, special effects, held towers, round 11)

- Crafting: 19 crafting-table recipes from vanilla materials (`Mod/Assets/MechDefense.cr`); upgrades take the lower
  tower as an ingredient. No industry, no power (user's call).
- Special effects matched to Endfield's skill/buff data (extracted read-only to local/endfield/ability-research):
  frost slows to 25% with a 3 s ice field (plus a 1.5 s freeze the user asked for; the original has none), poison
  cloud is purple and keeps poisoning/slowing 5 s after leaving, acid -30% defence 15 s, charge orb gold 5 s,
  shockwave 2 s stun without damage, bosses immune, water jet pushes only up to 500 kg.
- Held towers: first/third-person pose set (the item used to sit on the camera / centred in the hand) and drawn from the
  full glTF model (the 1,500-triangle icon fell apart in the hand).
- Prologue cannon elevates 0-30 degrees with articulated pistons and centre strut; shells leave along the barrel.
- Verified in an isolated runtime of the user's build (numbers in docs/TOWERS.md). Not played in the user's real game.

## Previous delivery: 0.4.3 (V5 models, rounds 8-10)

- All 19 towers use the V5 authored models (endfield-turret-model/v5): every bevel strip, cone, polygon and
  stripe cube is clipped to its part's shape, so no cube corner sticks out (V3 had 59-721 per tower, up to
  1.19 px); drum bases rebuilt from the original as one solid cut by the logo plane; trap nozzle is one cone.
- Builder: per-tower metadata in local/tower-meta/<id>.json (geo and atlas hashes), so subsets build in
  parallel and `--runtime-only` assembles towers.json (one process for all 19 takes about 3 h).
- Verified in an isolated runtime of the user's build: see-through slits (rest, two-sided) 1,995 -> 2,026,
  triangles 715k -> 1,162k, frame time not slower (hotbar 1.24 -> 1.15 ms, 30 towers 1.18 -> 1.16 ms),
  all 19 load and draw. Not played in the user's real game.
- Numbers: docs/TOWERS.md.

## Previous delivery: 0.4.2 (chamfers, round 6-7)

- All 19 towers use the V3 authored models (endfield-turret-model/v3): 45-degree chamfers on exposed long edges,
  slopes instead of steps, no needle tips; shapes, sizes, colours and bones unchanged.
- Authored see-through slits from the 0.4.1 to-do fixed where they were modelling gaps (trap side armour, sniper
  magazine neck, medic corner pads); the remaining ones (lightning fins, medic leg cavity) are open in the original too.
- Builder: cross-part separation repeats until no coincident pair is left (max 3 passes).
- Numbers and verification: docs/TOWERS.md.

## Previous delivery: 0.4.1 (user feedback on 0.4.0)

- See-through barrels and seams: the authored geo.json leaves out faces it considered hidden (67% of
  33,705 cubes miss at least one), and the 0.4.0 hidden-surface pass treated every cube as closed.
  The builder now restores all six faces before culling, grows whole cubes for the cross-part
  separation (moving one face left a slit), and grows same-part pieces floating ≤ 12.5 mm onto the
  surface below. Towers render two-sided as a safety net for 1-px rasterisation cracks.
- New check tools/check_tower_gaps.py (GPU, 86 views per pose, culled and two-sided). Results and the
  see-through spots that remain in the authored models themselves: docs/TOWERS.md.
- Inventory icons: 1,500-triangle 3D meshes per tower, fitted to the slot in UI mode.

## Previous delivery: all 19 Endfield towers 0.4.0

- Every Endfield tower family is deployable (data values 0, 3, 4 keep the old gun towers; 5–20 the rest).
  Models: authored cube models from `endfield-turret-model` (v1 / batch2_v2) exported as rigid-part glTF
  and drawn by the game's own model renderer; original Endfield audio for 18 families; own pixel effects.
- 0.3.1 issues and root causes: see docs/TOWERS.md (stacked faces 0.1–0.5 mm apart z-fighting; original
  VAT barrel spin retargeted onto non-round authored barrels; full-model CPU redraw every frame for every
  tower and every hotbar icon; 8–11 MB JSON per model).
- Towers no longer auto-despawn (SubsystemSpawn used to drop them 60 m from the camera and rebuild them
  from the template only).
- Build/test target is the user's own API 1.9.3.1 build (D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1).
  The vendor dev build used for 0.3.x tests differs (e.g. its instanced-buffer index path throws for
  models ≤ 65535 vertices and blanks the whole solid-model pass).
- Verification: tools/run_isolated.py lab (gallery, joints, fx, perf) and perf (same scene for 0.3.1 and
  0.4.0); tools/check_tower_overlaps.py. Results are summarised in docs/TOWERS.md.


## Previous delivery: video feedback fixes 0.3.1

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
