# Mercury, the mech (0.8.0)

User (2026-10-17): bring Mercury (水星, Destroy Gunners Σ) into the mod after its V8 asset was accepted in
`E:/projects/dguns-mech-model`. Choices: mouse turns the body + WASD moves; Mercury's own weapons + F melee; creative inventory
only. After the user's tests in game: R boards (E is the inventory), the original speeds, steering in the air, Shift hovers
(the original's boost), the cannon unfolds only when fired, a steady first person, and the original's weapons, lock-on and
sounds. Round 5: one hover sound per Shift press, the clean texture without grain, and numbers and a survival recipe set by
the user's installed mods (below). Facts about the original game, with their sources in its code and tables: docs/MERCURY_ORIGINAL.md.

## Assets (`tools/build_mech_assets.py` → `Mod/Assets/Imported/Mercury/`, ignored, packaged)

- Source: `E:/projects/dguns-mech-model/v8_assets/mercury_V8.blend` (the original model ×4, 37 joints, 2,466 triangles; its
  25 actions are the 24 decoded game clips plus a generated cannon retract), the clean texture
  `v8_assets/textures/mercury_clean_2048_sharp.png` (the original 512 upscaled ×4 with Real-ESRGAN x4plus as in V7, then a
  light unsharp mask, radius 1.4 px, 70 %, threshold 2; user: no grain or wear, as clear as possible) and the game's sound effects (`local/dguns/assets/se.bin`, 18 of 32 by the game's own ids). Extracted game data:
  never committed.
- `model.glb`: rigid parts, one node per joint at its rest pivot, the part's mesh in node space (34 parts carry geometry).
  Skinning in the original is rigid (one joint per vertex), so this is lossless except 3 triangles at the head-sensor seam
  (joints 03/04) that the skinned original stretches by up to 5 cm. The texture is embedded as lossless PNG: the engine builds
  its mipmaps and samples it linearly. A `TextureOverride` would be point-sampled (SubsystemModelsRenderer), so there is none.
- Since 0.9.0 (round 13 of dguns-mech-model; user 2026-10-22: "这一轮炮塔和水星都可以", then chose to ship it as it is) the
  shipped `model.glb`, `icon.bin` and `icon.png` are our own polygon Mercury from `E:/projects/dguns-mech-model/v10_poly/mercury/`
  (5,270 triangles, one 2048 texture painted from colours measured on the original, outline within 2.3 % of it, 0 triangles in
  common). The V8 export above still makes mech.json, clips.bin and the sounds; `use_poly` checks that the polygon model's node
  tree is the V8 export's and copies the three files over (`--poly-only` does just that, `--no-poly` ships V8).
- `clips.bin` + `mech.json`: per clip, per sample (every 2 ticks = 1/30 s, the last one at the clip's end), per joint the
  local deformation relative to the parent's (quaternion, translation, uniform scale). Game frame: Y up, faces −Z, right +X.
  `mech.json` also has the muzzles the tool finds on the model: the rifle tip (14), the missile box front next to the
  missile-pod joint (18; its mesh is skinned to 02), the cannon tip unfolded (17), the fist (09), the eye (04) and the two
  thrusters under the lower back units (33, 35; the original puts its jet objects there).
- `Audio/seNN.ogg`: lock_on 07, shot_gun 08, shot_missile 09 (also jump / boost take-off), shot_laser 10, shot_cannon 11,
  hit_gun 13, hit_missile 14, punch_swing 16, hit_punch 17, player_damage 19, player_destruction 20, throw_bomb 23,
  bomb_explosion 24, change_to_gun/missile/cannon/bomb/laser 25/26/27/29/30, boss_hover 31 (the hover hum).
- `icon.bin` / `icon.png`: inventory icon (idle pose, every triangle) in the towers' icon format.
- Self-check of the tool: it re-poses the model from its own output with the engine's bone composition
  (`ComponentModel.ProcessBoneHierarchy`: the set transform, plus the node's bind translation, times the parent) and compares
  with Blender's evaluation of the same frames: p99 0.001-0.002 mm on 7 poses; the maximum (16-48 mm) is the head-sensor
  seam above. `package.py` refuses assets that fail it.

## Runtime (`Mod/MDMech.cs`, `Mod/MDMechPilot.cs`)

- Entity: the `MDMech` template of the 0.5.0 sample mech (body 2.4 × 4.8 × 2.4, mass 3000, armour (AttackResilience) 3000, since
  0.9.0 round 5 4000 (user: too fragile against the devil mech, "不要提升太多"), mount), with
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
- Hover (Shift held; the original's boost mode): floats 0.8 m above the ground at 13 m/s on flames from the two back
  thrusters, in the jump's airborne pose with both legs kicked back (the jump clip held at 1.0 s). User: the original's
  gliding "dash" clips looked crouched, and its ×1.4 boost (8 m/s) did not feel faster. Sounds: the jump take-off is
  shot_missile as in the original's code (neither Σ nor SPα has another take-off sound among its 32 effects); hover hums
  with boss_hover once per Shift press (user: one sound per hold; the lab counts it with real keys). Energy (EP, the original's): 100000, +9000/s, boost −15360/s (about 6.5 s
  from full), a cannon shot −640; empty: no boost for 3 s.
- Clips: idle, combat stance (1.5 s after firing), walk in four directions (left/right by the mech's own sides), the
  jump's airborne pose while hovering, jump, fall, hit (when damaged on the ground), punch combo, jump attack (lock-on special), death
  (held), cannon unfold / fold. Not used: hangar pose, quick-steps, the two unnamed extras, the cannon's follow-up clip 55, the four "dash" clips (the
  original plays clip 55 for every cannon shot; it swings the cannon up and back down, which the user saw as folding and snapping
  open again).
- Weapons (B cycles rifle → missiles → cannon → laser → grenade, with the original's change_to sounds). Damage is the
  original's (pilot attack 1000 × part power %) × 0.1 (see Numbers below): Mercury's default rifle NO-100, missile NO-100 and cannon NO-200, and
  the shop's first laser NO-150 and grenade NO-200. Shots go from the model's muzzles to the crosshair point (the camera ray).
  No ammunition (the original has ammo with pickups; Survivalcraft has none).

  | Weapon | Damage | Rhythm | Flight / reach | Behaviour |
  |---|---|---|---|---|
  | Rifle | 100 | 0.25 s while held | 160 m/s | from the rifle tip |
  | Missiles | 100 each | one per lock, 0.3 s apart | 8 → 32 m/s (38 m/s²), 8-12 s | hold: a creature the crosshair sweeps over locks at once (within 3.5° or 1.3 m of the ray, 0.8-133 m, in sight beyond 13 m), up to 3 (MERCURY's lock count), red rings and the lock_on beep; release: launch. Drift 0.33-0.67 s, then home at 60°/s and faster the longer it homes (600°/s within 13 m, straight at it within 5 m) towards where the target will be. A missile with a live target always hits (user): it passes through terrain and bodies in its way and bursts on reaching the target. Release without a lock: one straight missile |
  | Cannon | 200, blast 120 within 13.4 m | 1.63 s | 320 m/s, straight | folded until fired: a press unfolds it (0.73 s) and fires once open; held, it stays open and keeps firing; released, it folds after the shot's recoil, as on another weapon |
  | Laser | 150 every 1/6 s | 1 s beam, then 1 s | grows at 160 m/s to 59 m | from the rifle tip, follows the crosshair; drawn as one beam with a soft ribbon through its last 0.12 s of positions (beams added every frame showed as stepped copies) |
  | Grenade | 200, blast within 13.4 m | 1.33 s, on press | arc 4 m above the higher end | thrown from the cannon's base; lands at 75 % of the crosshair's distance, 40 m at most |
  | Melee (F) | 1000 | 1.2 s | — | with a locked creature or one near the crosshair within 20 m: the jump attack carries the mech at it (up to 22 m/s); else the punch combo; 120° wedge up to 5.5 m at the hit frame |

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
  Since 0.9.0 round 3 (user 2026-10-21: a phone screen is already full) it can be hidden and lose its key-hint line (with the
  key hints of the boarding and weapon messages); since round 4 it is placed on a game page like the CS weapons mod's HUD
  editor (MDHudLayoutScreen, from the mod's settings page): drag, two fingers to move / pinch / twist, mouse wheel to scale,
  Shift + wheel or Q / E to turn, buttons for both; in play MDHudHost places it from MDSettings.Hud while the layout is measured. The rifle shows 射击中 while it fires: its
  0.25 s between shots used to read as 装填中 and flip with 就绪 four times a second (user report).
- Neorxna's info panel (optional mod) names it 水星机甲 with armour, kit level and pilot state (MDNeorxnaCompatibility; until
  0.9.0 round 4 it read 已移除的旧单位, the name meant for the removed 0.5.0 enemy, because Mercury uses the old mech template).
- The pilot takes no damage while seated in a working mech (0.9.0 round 5; Harmony prefix on ComponentHealth.Injure(Injury),
  MDMechPilotShield): robots never target a seated pilot and a robot shot that hits the pilot's body lands on the mech. The medic
  tower heals Mercury like a tower.
- Damage taken plays player_damage; destroyed (armour 0): the rider is set down, explosion and player_destruction, death clip
  held, smoke, removed after 10 s.
- Deploy item data 1 (“水星机甲 · 部署”, crafting table or creative inventory): stands on the clicked ground facing like the player;
  needs about 3 × 3 cells and 5 cells of headroom, no creature in the way. Old 0.5.0 mech items and mechs come back as Mercury;
  the 0.5.0 enemy (data 2, `MDEnemy`) stays removed. Shown texts do not name the source games or mention an original (原作; user: “帮助里不要提到什么原作什么的”).
- Upkeep as for the towers (`MDTowerMaintenance`): right-click it with iron or copper to repair (an iron ingot restores 200
  of its 3000, 6.7 %; an iron block 60 %); three pickaxe hits take it down into its item, which keeps its health (wear bits
  8-14 of the data, shown in the name and the slot's bar) and deploys it again with that health. A rider is set down first.
  Before this, both clicks reached tower-only code (`u.Tower` is null for the mech).

## Numbers and recipe (user, round 5: “数值设置和生存模式材料设置参考模组”)

Read from the active Mods folder of `D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1` (decompiled code and data, not played; the
research notes are outside the repository). Damage is power against AttackResilience, as the API's `Attackment`.

| Reference | Power, rhythm | Per second | Hull (AttackResilience) |
|---|---|---|---|
| Best hand-held guns (SiyahAkrep Gatling / prototype sniper, BF2042 MP9 / M5A3, Industrial Era AutoRifle) | | 240-350 | player 10 |
| BF2042 tank / RAM / MD540 helicopter | 600-2700 per 3.2 s / 85 per 0.3 s / rockets | 190-865 / 283 / 563 | 2000 / 1200 / 900 |
| SiyahAkrep armoured car / Leopard 2 | Leopard 1200-2500 per 6 s | | 3200 / 12000 |
| Industrial Era T-100 / T-200 (natural spawns after day 600 / 1000) | 250 every 0.2 / 0.1 s | 1250 / 2500 | 2500 |
| Ghoul bosses Dark / Corpsifice; SirenHead, Fenrir, Smolder | capped per hit or per second (Dark ≥ 40 s, Corpsifice ≥ 225 s on Normal) | | 2000 / 20000-50000; 1024-3200 |
| MechDefense towers | | | 650-2600 |

- Weapons: the original's damage ÷ 10 (DamageScale 0.03 → 0.1), the original's ratios kept: the rifle's 400 per second is
  above every hand-held gun there and below the vehicles' heavy weapons; the laser's 900 during its 1 s beam (450 on
  average), the cannon's 200 + 120 blast and the 1000 punch are the heavy hits. The capped bosses take as long as before.
- Armour 1500 → 3000: above BF2042's tank and the strongest tower (2600), below SiyahAkrep's armoured car and Leopard 2. A
  T-100 bullet takes 8.3 %, a BF2042 tank shell 20 %, a ghoul bear's punch 0.2 %.
- Recipe (crafting table, `Mod/Assets/MechDefense.cr`): the towers that are its weapons, “暴雨” (rifle), 高能射线塔 (laser),
  序章炮塔 (cannon) / 2 iron blocks around a diamond block / two 高爆榴弹塔 (missiles, grenades) around a semiconductor
  block: about 130 iron, 12 diamonds and 11 germanium in all. That is about twice the prologue cannon and above the
  installed packs' other top items (CS Negev ~120 iron at player level 18, Wonderful Era car key ~22 iron + 9 diamonds +
  a boss drawing, 玲兰 MarkII ~30 iron + 72 germanium), far below SiyahAkrep's armoured car (~520 iron) and Leopard 2
  (~5400). Worn towers do not count as ingredients until redeployed and repaired, as in the upgrade recipes.
- Vanilla ingredients and this mod's own items only: the user's pack crafts through RecipaediaEX, whose recipe loader wraps
  the whole merged `.cr` list in one try/catch, so one ingredient of a missing mod would drop every later recipe of every
  mod. Industrial Era items cannot be named in a `.cr` at all; an optional Industrial Era recipe would go in its own
  `.ier` file (read only when Industrial Era is installed; not done, not tested).

## Since 0.9.0: blueprint, player level, weapon kits

- Recipe: the same towers, two alloy plates around the devil mech's blueprint (used up), an energy core between the two HE
  grenade towers; player level 8 (user, 2026-10-20: only the mech has a level requirement). The blueprint only drops from the
  devil mech (decoy beacon). Details and numbers: `docs/PROGRESSION.md`.
- Weapon kits Mk1-Mk3 (right-click Mercury, in order): every weapon's damage x1.15 / x1.3 / x1.5, applied in
  `MDTowerBrain.Hit`; kept in the deploy item when it is taken down (data bits 15-16, name “（改装 MkN）”).
- Mercury's hits carry a damage channel for the devil mech's weak channel: rifle bolts and melee kinetic, missiles,
  cannon and grenades explosion, the laser energy.

## Since 0.9.0 round 5: hero mechs 复仇女神 (NEMESIS) and 轩辕十四 (REGULUS)

User 2026-10-21: "主角机甲可以再加个复仇女神和轩辕十四（实力方面：水星<复仇女神<轩辕十四）"; plan confirmed the same day. `MDMechType`
(Mod/MDMech.cs) holds each mech's name, asset folder, template (MDMech / MDMechNemesis / MDMechRegulus), item data (1 / 30 / 31),
damage factor (1 / 1.25 / 1.5), energy (100000 / 130000 / 160000), speed (轩辕十四 1.15, its walk clip played faster to match),
missile locks (3 / 3 / 2, the original's), the burst rifle (复仇女神), blast protection (复仇女神 0.8, MDMechArmour on robot missiles /
shells / blasts and explosive tower hits), the hover pose (轩辕十四 holds its gliding dash_forward), raid weight and boss armour.
Armour 4000 / 6000 / 8000 is in the templates. Joint roles come from each mech.json's `roles` (defaults = Mercury's):
turret, armL / armR (pitch to the aim), cannon joints (the cannon layer), grenade, firstPerson parts; the first-person eye from
`cockpit.firstPersonEye`; the third-person camera scales with the mech's height. Assets: tools/build_hero_assets.py (helper agent,
E:/projects/dguns-mech-model/v9_heroes); original data: docs/HEROES_ORIGINAL.md.

## Not done

- The original's projectile and effect models (KN_BULET, KN_MISSILE, KN_BOMB, KN_JET0 in `local/dguns/unit-library`):
  shots, missiles, grenades and jets are the towers' particle effects.
- Ammunition and pickups, part upgrades (power levels, High Scope Lock, CN-EXP/SPD), the DevilMech damage channels.

## Verification

See docs/STATUS.md (0.8.0) and `tools/run_isolated.py lab --mode mech` (Tests/TowerLab/Mech.cs).
