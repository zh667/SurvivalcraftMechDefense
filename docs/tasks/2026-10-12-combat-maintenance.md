# Task 2026-10-12: tower combat fixes, dismantle and repair (0.7.0)

Status: accepted — merged into main on the user's approval (2026-10-13), tag v0.7.0
Branch: feat/tower-combat-maintenance    Current writer: Claude Code (VPS, edits applied on Windows)
User-authorized scope (2026-10-12): “1. 炮弹的飞行速度得加快一点，要不然会出现炮弹跟不上别人跑的速度的尴尬。2. 攻击范围还可以加大一点
太近了，另外美好时代的船长跟海盗炮塔不会攻击他们，索敌需要优化一下，就是分辨敌我。然后感觉数值可以再给高一点，因为本来频率就不高，
只有大量使用才有效果 3. 这个炮台打尸鬼很吃亏……有的减伤很严重，基本不减血 4. 现在炮塔无法拆卸，做成原版镐子可拆卸的吧（注意好拆卸前后的
血量，以及能不能做一个维修机制？右键拿着材料对准炮塔可维修，具体的材料消耗可以参考其他 mod 对于原版材料获取难度的影响）”
Out of scope: installing the package, the user's Mods folder and worlds, merging (needs the user's approval), tower models.

## Evidence

- Mods read (copies only, decompiled into ignored `local/`): `D:/下载/[API1.9.3]美好时代V1.9.3a.scmod` (Wonderful Era 1.9.3, SHA-256
  e9dc7624…01a20; it is not in the 1.9.3.1 Mods folder, the user's copy is in D:/下载), `[API1.9.2.1]Ghoul尸鬼-v2.0.scmod` (decompiled
  earlier by the balance research into `local/balance-research/ghoul-code`), the user's `Survivalcraft.dll` 1.9.3.1 (SHA-256
  25816c2f…72c) into `local/engine-src`.
- Pirates (WonderfulEraCreature.xdb): 海盗水手 / 精英海盗 / 海盗船长 chase with Wonderful Era's own `ComponentNewChaseBehavior`
  (a clone of the vanilla chase behaviour with an extra `m_chasePlayerProbability`), category not a predator. `MDCombat.IsEnemy`
  only knew vanilla `ComponentChaseBehavior` and predators, so it never picked them. Hired pirates (`*_Tame`) are `HerdName
  "player"`, the deputy captain (`Pirate_Captain_NPC`) trades and has `ChasePlayerProbability 0`: neither may be shot.
- Ghouls: `SAGModLoader.ProcessAttackment` multiplies every `ProjectileAttackment` by `ComponentBulletDefender`: head hits (within
  0.15 m of the eye height) ×`HeadHitDamageMultiplier`, body hits ×`NonHeadHitDamageMultiplier` = 0.1-0.5 (Strong_08 0.1, Monster /
  Fatt / Boar / Bear 0.2, Wolf 0.3). Every tower attack was a ProjectileAttackment aimed at the body centre. Their per-injury caps
  (Dark, Bear: 50 per window) apply to all damage and are left alone. Many ghouls are category `Bird`.
- Vanilla `ComponentChaseBehavior.ScoreTarget`: any creature with a chase range above 0 hunts players (the mask only selects other
  creatures). Vanilla non-predators with a range: Bull/Bison 1.5 m, Wildboar 1.5/4 m, Rhino 16 m.
- ScCsgoTactical: enemies aim through `ComponentTacticalEnemy.TargetBody`; companions have `lastVoiceTarget` (the player they talk
  to), so target fields are matched by exact name only.
- Repair reference: Wonderful Era's wrench restores 10/25/50/75% of a vehicle per use by AttackResilience class. In the user's pack
  the Industrial Era 2 sieve turns gravel into ore and a small mod lets its iron/copper plates replace ingots for rods and powder:
  iron and copper are easier to get than with vanilla smelting alone (iron ingots need heat level 2; 9 ingots = 1 block).
- Engine hooks used: `ComponentMiner.Hit` (Harmony prefix: pickaxe on a tower), ModLoader `OnPlayerInputInteract` (called before the
  engine picks use/interact/place, so non-usable items like ingots reach it), ModLoader `ProcessAttackment` (aggressor memory).

## Results

- Changed files and why:
  - `tools/towers.json`: ranges, damage, bolt/shell speeds (intervals unchanged); four summaries quote the new ranges.
  - `Mod/MDFaction.cs` (new): friend or foe from each creature's own behaviour; `MDTowerAttackment` (non-bullet damage).
  - `Mod/MDTowerBrain.cs`: lead from the target's observed movement, head aim for gunfire on weak-headed creatures, bullet vs
    non-bullet hits, candidates ranked before line-of-sight traces.
  - `Mod/ComponentMDUnit.cs`: `IsEnemy` → MDFaction; target tracking, dismantle progress, `SetHitpoints`.
  - `Mod/MDTowerMaintenance.cs` (new): pickaxe dismantle (Harmony prefix on `ComponentMiner.Hit`), repair with iron/copper.
  - `Mod/MDPlayer.cs`: deploy item keeps wear in data bits 8-14 (name, slot health bar), redeploy restores health; mod hooks
    `ProcessAttackment` and `OnPlayerInputInteract`.
  - `Mod/MDTowerPlacement.cs`: Neorxna panel shows 耐久. `Mod/MDTowerFx.cs`: bolt tracer length follows speed.
  - `Mod/modinfo.json`: 0.7.0. `Tests/TowerLab/Combat.cs` + `Lab.cs` + `tools/run_isolated.py`: lab mode `combat`.
  - Not committed (tool output unrelated to this task, restored): `Mod/Assets/MechDefense.xdb` (regenerating it changes one
    bounding-sphere radius, 2.98 → 2.97, left over from the V5 models) and `docs/tower-provenance.json`.
- Windows jobs: research-branch commit ea153f90…; build d88489b2…; package 1685be07…, 692fc9f9…; combat lab 0fe81709…
  (ghoul `SubsystemWorld.Load` needs `GameLoadingScreen.m_worldInfo`, which the lab now sets), d892a480…, d11684d4…;
  regression suite 8f2e3bb8…, follow-up 279b6c5e… (migration with the 0.5.0 package, as documented; the first attempt used
  0.6.0, which cannot build the old mech world), 29987337… (0.6.0 fire baseline after making the lab depend on the tested
  package's version), 0f1f4991… (combat with the every-tower check).
- Package: dist/MechDefense-0.7.0.scmod, 26,263,051 bytes (SHA-256 in the commit message / PR).
- Checks passed: lab combat (all checks, docs/STATUS.md 0.7.0), lab special, lab fire, smoke migration 0.5.0 → 0.7.0, perf
  (no difference). Not run: check_tower_gaps / check_tower_overlaps (models unchanged: tower model files are byte-identical
  to 0.6.0 in the package).
- Remaining: not played in the user's real game with all its mods; ScCsgoTactical soldiers/companions not loaded in the lab;
  Industrial Era plates are not repair materials; numbers are a first pass for the user to judge in play.
- Rollback: dist/MechDefense-0.6.0.scmod (keep until the user accepts 0.7.0 in play).
