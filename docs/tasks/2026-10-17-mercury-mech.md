# Task 2026-10-17: Mercury, a mech the player boards (0.8.0)

Status: review — pushed with a pull request; merging needs the user's approval
Branch: feat/mercury-mech    Current writer: Claude Code (VPS, edits applied on Windows)
User-authorized scope (2026-10-17): phase B of the Mercury plan (phase A = the asset in `E:/projects/dguns-mech-model/v8_assets`,
accepted the same day with the side-step clipping kept as in the original): “现在就可以了” — put Mercury into the mod. Choices
asked and answered: “鼠标转向 + WASD 平移” (Shift dash, Space jump); “按水星自带武器” (rifle, missiles, the back cannon unfolding
and folding, F melee); “只在创造模式物品栏” (no recipe).
Out of scope: installing the package, the user's Mods folder and worlds, merging, other mechs/skins, enemies, tower changes
beyond what the mech's shells need.

## Evidence

- Asset: `E:/projects/dguns-mech-model/v8_assets/mercury_V8.blend` + `textures/mercury_V8_2048.png` + `mercury_V8_contract.json`
  (V8, round 10 of that project; 25 actions, readback 3 µm).
- 0.5.0 (tag v0.5.0) had a sample mech on the same template (`MDMech`): procedural leg swing, CPU-drawn, tank controls, five
  ammo types. Its boarding, cameras, HUD and guards were the starting point for `Mod/MDMechPilot.cs`.
- Engine (decompiled 1.9.3.1, `local/engine-src` + Engine.dll types): `ComponentModel.ProcessBoneHierarchy` composes a set bone
  transform with the node's bind translation (rigid models); `SubsystemModelsRenderer` samples a `TextureOverride` with
  PointClamp, a model's own texture with its glTF sampler; `Model.GetTexture` builds a full mipmap chain.

## Results

- Changed files and why: new `Mod/MDMech.cs` (assets, layered animator, pose, model component, behaviour, weapons) and
  `Mod/MDMechPilot.cs` (boarding, controls, cameras, HUD, guards); `ComponentMDUnit` (kind Mech lives again; the enemy stays
  legacy; the mech's friend-or-foe rule), `MDPlayer` (item data 1 = Mercury), `MDTowerFx` / `MDTowerBrain` (a shell carries
  its weapon definition); `tools/build_mech_assets.py` (new), `prepare_database.py` (MDMech gets its model component),
  `package.py` (Mercury assets, checked), lab mode `mech` (`Tests/TowerLab/Mech.cs`), smoke expectations for 0.8.0, docs.
- Windows jobs: asset tool claude-pb-assets-1 (d2be6575…); lab claude-pb-lab-mech-1 (9944847d…), claude-pb-lab-mech-3 (two runs);
  regression claude-pb-tests-1 (86c410c8…) and claude-pb-tests-2 (6d7226d2…): smoke old/new, lab fire 0.8.0 / 0.7.0, perf pairs.
- Package: dist/MechDefense-0.8.0.scmod, 28,160,481 bytes, SHA-256 9fbbf465…c48cade6 (details in docs/STATUS.md).
- Checks passed: asset re-pose (p99 ≤ 0.002 mm), lab mech (twice), smoke migration 3/3 + 20/20, lab fire (same as 0.7.0), perf
  (no difference beyond the spread), package scan. Not run: tower gap/overlap checks (tower models unchanged).
- User test 1 (2026-10-17): “上机甲是哪个键？E不行” — E is ToggleInventory in this game version; the GUI opened the inventory
  first and the mech's E handler saw the panel and ignored the key. Now the mount key (R) boards and leaves, input is read in
  a postfix of ComponentInput.Update, reach 3 m; the lab presses real keys (job claude-pb-keys-2).
- User test 2 (2026-10-17): big steps and little ground, no steering in a jump, the cannon unfolded by itself, first person
  shaky / weapon unclear / flickering, HUD speed flickering, weapons not the original's (laser, grenade, lock-on missiles,
  sounds). User test 3: the cannon folded and snapped open again after every shot; the dash looked crouched; make Shift hover
  (the original's boost with back flames). Research of the original (code + tables): docs/MERCURY_ORIGINAL.md. Jobs
  claude-pb-round2-*, claude-pb-round3-*, claude-pb-smoke-3, claude-pb-final-3.
- User test 4 (2026-10-17): hover like the jump pose, distinct sounds, locked missiles always hit, smooth laser, faster hover
  (jobs claude-pb-round4-*, claude-pb-round4-final).
- Remaining: not verified in the user's real game, with real keyboard input, touch/gamepad, or next to the ghoul / Wonderful
  Era mods. Rollback package: dist/MechDefense-0.7.0.scmod.
