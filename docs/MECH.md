# Mercury, the mech (0.8.0)

User (2026-10-17): bring Mercury (水星, Destroy Gunners Σ) into the mod after its V8 asset was accepted in
`E:/projects/dguns-mech-model`. Choices: mouse turns the body + WASD moves; Mercury's own weapons + F melee; creative inventory
only. After the user's tests in game: R boards (E is the inventory), the original speeds, steering in the air, Shift hovers
(the original's boost), the cannon unfolds only when fired, a steady first person, and the original's weapons, lock-on and
sounds. Facts about the original game, with their sources in its code and tables: docs/MERCURY_ORIGINAL.md.

## Assets (`tools/build_mech_assets.py` → `Mod/Assets/Imported/Mercury/`, ignored, packaged)

- Source: `E:/projects/dguns-mech-model/v8_assets/mercury_V8.blend` (the original model ×4, 37 joints, 2,466 triangles; its
  25 actions are the 24 decoded game clips plus a generated cannon retract), its texture (Real-ESRGAN 2048 + grain and edge
  wear) and the game's sound effects (`local/dguns/assets/se.bin`, 18 of 32 by the game's own ids). Extracted game data:
  never committed.
- `model.glb`: rigid parts, one node per joint at its rest pivot, the part's mesh in node space (34 parts carry geometry).
  Skinning in the original is rigid (one joint per vertex), so this is lossless except 3 triangles at the head-sensor seam
  (joints 03/04) that the skinned original stretches by up to 5 cm. The texture is embedded as JPEG: the engine builds its
  mipmaps and samples it linearly. A `TextureOverride` would be point-sampled (SubsystemModelsRenderer), so there is none.
- `clips.bin` + `mech.json`: per clip, per sample (every 2 ticks = 1/30 s, the last one at the clip's end), per joint the
  local deformation relative to the parent's (quaternion, translation, uniform scale). Game frame: Y up, faces −Z, right +X.
  `mech.json` also has the muzzles the tool finds on the model: the rifle tip (14), the missile box front next to the
  missile-pod joint (18; its mesh is skinned to 02), the cannon tip unfolded (17), the fist (09), the eye (04) and the two
  thrusters under the lower back units (33, 35; the original puts its jet objects there).
- `Audio/seNN.ogg`: lock_on 07, shot_gun 08, shot_missile 09 (also jump / boost take-off), shot_laser 10, shot_cannon 11,
  hit_gun 13, hit_missile 14, punch_swing 16, hit_punch 17, player_damage 19, player_destruction 20, throw_bomb 23,
  bomb_explosion 24, change_to_gun/missile/cannon/bomb/laser 25/26/27/29/30.
- `icon.bin` / `icon.png`: inventory icon (idle pose, every triangle) in the towers' icon format.
- Self-check of the tool: it re-poses the model from its own output with the engine's bone composition
  (`ComponentModel.ProcessBoneHierarchy`: the set transform, plus the node's bind translation, times the parent) and compares
  with Blender's evaluation of the same frames: p99 0.001-0.002 mm on 7 poses; the maximum (16-48 mm) is the head-sensor
  seam above. `package.py` refuses assets that fail it.

## Runtime (`Mod/MDMech.cs`, `Mod/MDMechPilot.cs`)

- Entity: the `MDMech` template of the 0.5.0 sample mech (body 2.4 × 4.8 × 2.4, mass 3000, resilience 1500, mount), with
  `ComponentMDMechModel` (the engine's model renderer like the towers: world light, fog, shadow; two-sided because the
  original shell is open). `ComponentMDUnit` with kind `Mech` holds an `MDMechState`.
- Bone transforms: the engine's for joint j is T(p) L T(−p) (row form), p its rest pivot, L its local deformation from the
  clips. Layers per joint: the base clip with a cross-fade, a full-body action (hit, melee, jump attack) and the cannon
  (joints 15-17 only). On top, in posed space about the posed pivot: the turret/cockpit (02) turns about the vertical towards
  the aim, at most 60° from the body; the weapon arms (05, 10) pitch about the turret's lateral axis so the rifle points at the
  aim pitch (measured on the clip's pose every frame; faded out in hit / melee and when destroyed); the cannon barrel kicks
  back 0.45 m per shot.
- Scale: one world unit of the original is 2.67 m here (MERCURY is 1.68 units tall there, 4.48 m here). Speeds and ranges
  below are the original's converted with it.
- Moving (the original's root motion, clips at their own rate, the feet glide a little as in the game): walk 5.7 m/s,
  backwards 5.2, sideways 5.7. The mech keeps its own ground velocity eased at 24 m/s² (the body's velocity is cut by
  friction after every step: easing that never got past ~4.8 m/s). In the air it steers at 12 m/s² and keeps its momentum.
  The body turns towards the aim at 3.5 rad/s while moving or when the aim leaves the turret's range.
- Jump (Space): leaves at once at 8.5 m/s (3.1 m high) with a thruster burst and shot_missile's sound (the original's
  take-off sound).
- Hover (Shift held; the original's boost mode, whose clips are the four gliding "dash" clips): floats 0.8 m above the ground
  at 8 m/s on flames from the two back thrusters. Energy (EP, the original's): 100000, +9000/s, boost −15360/s (about 6.5 s
  from full), a cannon shot −640; empty: no boost for 3 s.
- Clips: idle, combat stance (1.5 s after firing), walk in four directions (left/right by the mech's own sides), the dash
  clips while hovering, jump, fall, hit (when damaged on the ground), punch combo, jump attack (lock-on special), death
  (held), cannon unfold / fold. Not used: hangar pose, quick-steps, the two unnamed extras, the cannon's follow-up clip 55 (the
  original plays it for every cannon shot; it swings the cannon up and back down, which the user saw as folding and snapping
  open again).
- Weapons (B cycles rifle → missiles → cannon → laser → grenade, with the original's change_to sounds). Damage is the
  original's (pilot attack 1000 × part power %) × 0.03: Mercury's default rifle NO-100, missile NO-100 and cannon NO-200, and
  the shop's first laser NO-150 and grenade NO-200. Shots go from the model's muzzles to the crosshair point (the camera ray).
  No ammunition (the original has ammo with pickups; Survivalcraft has none).

  | Weapon | Damage | Rhythm | Flight / reach | Behaviour |
  |---|---|---|---|---|
  | Rifle | 30 | 0.25 s while held | 160 m/s | from the rifle tip |
  | Missiles | 30 each | one per lock, 0.3 s apart | 8 → 32 m/s (38 m/s²), 8-12 s | hold: a creature the crosshair sweeps over locks at once (within 3.5° or 1.3 m of the ray, 0.8-133 m, in sight beyond 13 m), up to 3 (MERCURY's lock count), red rings and the lock_on beep; release: launch. Drift 0.33-0.67 s, then home at 60°/s (600°/s within 13 m) towards where the target will be. Release without a lock: one straight missile |
  | Cannon | 60, blast 36 within 13.4 m | 1.63 s | 320 m/s, straight | folded until fired: a press unfolds it (0.73 s) and fires once open; held, it stays open and keeps firing; released, it folds after the shot's recoil, as on another weapon |
  | Laser | 45 every 1/6 s | 1 s beam, then 1 s | grows at 160 m/s to 59 m | from the rifle tip, follows the crosshair |
  | Grenade | 60, blast within 13.4 m | 1.33 s, on press | arc 4 m above the higher end | thrown from the cannon's base; lands at 75 % of the crosshair's distance, 40 m at most |
  | Melee (F) | 300 | 1.2 s | — | with a locked creature or one near the crosshair within 20 m: the jump attack carries the mech at it (up to 22 m/s); else the punch combo; 120° wedge up to 5.5 m at the hit frame |

  The pilot picks the targets: hits hurt any creature except the player's side (players, towers, the player's herd, mounts).
  Creatures that attack the mech count as aggressors for the towers (it is on the player's side).
- Pilot: the game's mount key (ToggleMount, R by default, follows the player's key binding) boards within 3 m of the body
  and leaves to free ground 3-6 m away; with no mech near it mounts horses as usual. E stays the inventory. Mouse aims, WASD,
  Shift (held) hovers, Space jumps, left button fires, B weapons, F melee, V switches between the third-person camera (over
  the right shoulder, 10 m back) and first person. First person is fixed to the body at the cockpit (3.6 m up, 0.5 m
  forward, smoothed), not to the bobbing head joint, and the model draws only the rifle on its forearm and the cannon
  (MeshDrawOrders): the head, shoulder units, arms, legs and back units swept through the view and clipped at the near
  plane. Input is read right after ComponentInput (the earliest update); what the mech uses (move, look, jump, fire, mount,
  crouch, fly, camera mode) is taken out of the player's input before the GUI or the locomotion see it. HUD bottom right:
  armour, speed (smoothed over ~0.3 s, whole km/h), weapon and its state (ready / reloading / locks n/3 / firing / beam /
  unfolding / folded / out of energy), armour and energy bars. The rider is hidden.
- Damage taken plays player_damage; destroyed (armour 0): the rider is set down, explosion and player_destruction, death clip
  held, smoke, removed after 10 s.
- Deploy item data 1 (“水星机甲 · 部署”, creative inventory only): stands on the clicked ground facing like the player;
  needs about 3 × 3 cells and 5 cells of headroom, no creature in the way. Old 0.5.0 mech items and mechs come back as Mercury;
  the 0.5.0 enemy (data 2, `MDEnemy`) stays removed. Shown texts do not name the source games.

## Not done

- The original's projectile and effect models (KN_BULET, KN_MISSILE, KN_BOMB, KN_JET0 in `local/dguns/unit-library`):
  shots, missiles, grenades and jets are the towers' particle effects.
- Ammunition and pickups, part upgrades (power levels, High Scope Lock, CN-EXP/SPD), the DevilMech damage channels.

## Verification

See docs/STATUS.md (0.8.0) and `tools/run_isolated.py lab --mode mech` (Tests/TowerLab/Mech.cs).
