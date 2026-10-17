# Mercury, the mech (0.8.0)

User (2026-10-17): bring Mercury (水星, Destroy Gunners Σ) into the mod after its V8 asset was accepted in
`E:/projects/dguns-mech-model`. Choices: mouse turns the body + WASD moves (Shift dash, Space jump); Mercury's own weapons
(rifle, missiles, cannon that unfolds and folds) + F melee; creative inventory only.

## Assets (`tools/build_mech_assets.py` → `Mod/Assets/Imported/Mercury/`, ignored, packaged)

- Source: `E:/projects/dguns-mech-model/v8_assets/mercury_V8.blend` (the original model ×4, 37 joints, 2,466 triangles; its
  25 actions are the 24 decoded game clips plus a generated cannon retract) and its texture (Real-ESRGAN 2048 + grain and
  edge wear). Extracted game data: never committed.
- `model.glb`: rigid parts, one node per joint at its rest pivot, the part's mesh in node space (34 parts carry geometry).
  Skinning in the original is rigid (one joint per vertex), so this is lossless except 3 triangles at the head-sensor seam
  (joints 03/04) that the skinned original stretches by up to 5 cm. The texture is embedded as JPEG: the engine builds its
  mipmaps and samples it linearly. A `TextureOverride` would be point-sampled (SubsystemModelsRenderer), so there is none.
- `clips.bin` + `mech.json`: per clip, per sample (every 2 ticks = 1/30 s, the last one at the clip's end), per joint the
  local deformation relative to the parent's (quaternion, translation, uniform scale). Game frame: Y up, faces −Z, right +X.
- `icon.bin` / `icon.png`: inventory icon (idle pose, every triangle) in the towers' icon format.
- Self-check of the tool: it re-poses the model from its own output with the engine's bone composition
  (`ComponentModel.ProcessBoneHierarchy`: the set transform, plus the node's bind translation, times the parent) and compares
  with Blender's evaluation of the same frames: p99 0.001-0.002 mm on idle (frames 0, 192), walk, strafe, cannon unfolded,
  death and melee; the maximum (16-48 mm) is the head-sensor seam above. `package.py` refuses assets that fail it.

## Runtime (`Mod/MDMech.cs`, `Mod/MDMechPilot.cs`)

- Entity: the `MDMech` template of the 0.5.0 sample mech (body 2.4 × 4.8 × 2.4, mass 3000, resilience 1500, mount), now with
  `ComponentMDMechModel` (drawn by the engine's model renderer like the towers: world light, fog, shadow; two-sided because
  the original shell is open). `ComponentMDUnit` with kind `Mech` holds an `MDMechState`.
- Bone transforms: the engine's for joint j is T(p) L T(−p) (row form), p its rest pivot, L its local deformation from the
  clips; the engine adds the node offset and the parent. Layers per joint: the base clip with a cross-fade, a full-body
  action (hit, melee) and the cannon (joints 15-17 only: unfold, held unfolded, firing, fold). On top, in posed space about the
  posed pivot: the turret/cockpit (02) turns about the vertical towards the aim, at most 60° from the body; the weapon arms
  (05, 10) pitch about the turret's lateral axis.
- Moving: the body turns towards the aim at 2.4 rad/s while moving or when the aim leaves the turret's range. Walk 4.5 m/s,
  backwards 3.2, sideways 3.6, dash 10 (feet glide in the dash clips), jump 8 m/s up after a 0.22 s crouch. Walk clips play
  at speed ÷ the clip's planted-foot speed (V8 contract: 2.9-4.1 m/s), so the feet do not slide.
- Clips: idle, combat stance (1.5 s after firing), walk / dash in four directions (left/right by the mech's own sides), jump,
  fall, hit (when damaged on the ground), melee, death (held), cannon unfold / fire / fold. Not used: hangar pose,
  quick-steps, the lock-on special and the two unnamed extras.
- Weapons (tower-style definitions so shells, trails, impacts and blasts use the towers' effect world; sounds of the gun
  turret, rocket and cannon towers). Shots go from the model's muzzles (found by the asset tool: the rifle tip, the missile
  box front next to the missile-pod joint, the cannon tip unfolded) to the crosshair point (the camera ray):

  | Weapon | Damage | Interval | Blast | Notes |
  |---|---|---|---|---|
  | Rifle | 30 | 0.2 s | — | bolt 160 m/s; overheats after ~18 shots, usable again at 35% |
  | Missiles | 4 × 70 | 2.5 s | 2.5 m | 34 m/s, slight drop and spread |
  | Cannon | 360 | 2.8 s | 4 m | must be unfolded (0.73 s); low ballistic arc at 58 m/s |
  | Melee (F) | 300 | 1.2 s | — | 120° wedge up to 5.5 m, knocks back up to 800 kg |

  The pilot picks the targets: hits hurt any creature except the player's side (players, towers, the player's herd, mounts).
  Creatures that attack the mech count as aggressors for the towers (it is on the player's side).
- Pilot (E boards within 1.5 m of the body, E again leaves to free ground 3-6 m away): mouse aims, WASD, Shift, Space, left
  button fires, B cycles rifle → missiles → cannon, F melee, V switches between the third-person camera (over the right
  shoulder, 10 m back) and the first-person camera at the head sensors. HUD bottom right: armour, speed, weapon, status
  (ready / reloading / overheated / unfolding), heat bar. The rider is hidden; the player's own input is consumed.
- Destroyed (armour 0): the rider is set down, explosion, death clip held, smoke, removed after 10 s.
- Deploy item data 1 (“水星机甲 · 部署”, creative inventory only): stands on the clicked ground facing like the player;
  needs about 3 × 3 cells and 5 cells of headroom, no creature in the way. Old 0.5.0 mech items and mechs come back as Mercury;
  the 0.5.0 enemy (data 2, `MDEnemy`) stays removed. Shown texts do not name the source games.

## Verification

See docs/STATUS.md (0.8.0) and `tools/run_isolated.py lab --mode mech` (Tests/TowerLab/Mech.cs).
