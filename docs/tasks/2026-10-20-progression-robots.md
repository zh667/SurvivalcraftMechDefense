# Task 2026-10-20: progression — robot enemies, materials, tiered recipes, boss blueprint, mech upgrades (0.9.0)

Status: review (PR open, not merged)
Branch: feat/progression-robots    Current writer: Claude Code (VPS, edits applied on Windows); enemy assets by a helper agent
User-authorized scope (2026-10-20): the user asked how towers and mechs should be crafted and grow from low to high tier, with
three player suggestions (Industrial Era's T-100; the original's devil mech dropping blueprints; hostile robots dropping
special materials). The proposal (robots drop materials, a boss drops the mech blueprint, towers in three tiers, mech
upgrades) was accepted with these decisions:
- 4 new materials + 1 blueprint. Without fighting (fallback recipes) they cost **5×**; the blueprint is **consumed** (one use).
- Robots appear **both ways**: natural spawns in the wild and night raids on the player's towers.
- Fallback recipes as proposed: the materials have one, the blueprint only drops from the boss.
- Player level as proposed: only the mech needs one (level 8); towers are gated by materials.
- Modelling follows the modeling-workflow skill (enemy models: the original game's models into the game, steps 6–9).
Out of scope: installing the package, the user's Mods folder and worlds, merging, power/industry networks (the user deferred
them in 0.5.0), new tower models.

## Design (numbers are starting values, not play-tested)

Materials (`MDPartBlock`, crafting id `mdpart`, data):

| data | item | source | fallback (5 × normal price, vanilla only) |
|---|---|---|---|
| 0 | 机兵残骸 scrap | normal robots 2–4 | none (raw drop) |
| 1 | 合金板 alloy plate | furnace: scrap ×2 + iron ingot (normal price 3 iron) | iron block + 3 iron ingots + 3 copper ingots (15) |
| 2 | 火控芯片 fire-control chip | elites 60 % (normal 5 %) | normal price 2 germanium + 1 copper → semiconductor block + germanium + 5 copper |
| 3 | 能量核心 energy core | elites 15 %, boss 3–5 | normal price 1 diamond + 2 copper → 5 diamonds + copper block + copper ingot |
| 4 | 机甲设计图 mech blueprint | boss 100 % | none; consumed by the mech recipe |
| 5 | 诱敌信标 decoy beacon | crafted: chip + core + 4 alloy + 2 bulbs + wire | (through the fallbacks) |
| 6–8 | 武器改装件 Mk1–Mk3 | crafted from alloy / chips / cores | (through the fallbacks) |

Towers: tier 1 (gun, grenade, torrent, acid, poison, frost, flame) unchanged; tier 2 (extended gun, HE grenade, ray, surge,
sonic, medic) take 3 alloy plates instead of 3 iron ingots; tier 3 (Downpour, sentry, mortar, high-energy ray, Charge,
prologue cannon) take alloy plates + 2 chips (+1 core for the energy towers). Mercury: the three tier-3 towers + two HE
grenade towers + 2 alloy plates + blueprint (consumed) + energy core, player level 8.

Enemies (Destroy Gunners Σ models via `tools/build_unit_assets.py`, same ×4 scale as Mercury): 双管步行机兵 RD05 (normal),
导弹步行机兵 RD06 and 炮击步行机兵 RD07 (elites), 恶魔机甲 RD17 (boss, summoned with the beacon, warning countdown, weak point,
three damage channels). Creatures of category LandPredator, so towers, other mods' weapons and per-hit caps apply. Natural
spawn: night, surface, from day 3 (elites from day 10). Raids: at night robots come for the player's tower clusters.
Optional Industrial Era exchange (`Assets/MechDefense.ier`, read only by Industrial Era): T-100/T-200 drops → our materials.

## Evidence

- Vanilla prices (Content.zip CraftingRecipes.xml): iron block = 9 iron ingots, semiconductor block = 9 germanium, diamond
  block = 9 diamonds, battery 4 per 2 copper + 4 coal, wire 16 per copper.
- Industrial Era 2 1.0.0.2: T-100 (day > 600, > 40 m from the ocean, supernatural creatures on) / T-200 (day > 1000) drop
  steel alloy 3–4, condenser (60 %), bullets, solar panel (40 %), basic / advanced circuits (50 %); resilience 2500.
- Wonderful Era 1.9.3a: the snow-mountain boss drops car / plane / boat design drawings (20 / 12 / 30 %); key recipes need
  the drawing (consumed) and player level 7 / 10 / 4.
- Engine 1.9.3.1: blocks of mod assemblies get dynamic indices; `ComponentLoot` decodes "Name:data"; furnace recipes are one
  row of up to three cells; `CraftingRecipe.RequiredPlayerLevel`.

## Results

- Status of record: docs/STATUS.md (0.9.0). Changed files: new Mod/MDParts.cs (items, icons, kits), Mod/MDRobots.cs (robot
  kinds, behaviour, fire, model, natural spawns, raids), Mod/MDBoss.cs (devil mech, beacon, weak channel); MDMech.cs (unit
  definitions from any folder, kit level), MDTowerBrain.cs (damage channels, kit factor), MDTowerFx.cs (robot projectiles),
  MDFaction.cs (channel field), MDPlayer.cs (beacon use, kit install hook, kit bits on the mech item), MDTowerMaintenance.cs;
  Assets/MechDefense.cr (tiers, fallbacks, Mercury), Assets/MechDefense.xdb (robot templates), Assets/Parts (icons);
  tools/build_part_icons.py, tools/build_unit_assets.py (helper agent), prepare_database.py, package.py; Tests/TowerLab/Robots.cs;
  docs/PROGRESSION.md, README, AGENTS, MECH, TOWERS.
- Windows jobs: lab robots md-prog-t_robots-8 (final), regressions md-prog-final-fA-1 (special + mech) and md-prog-final-t_smoke-1,
  perf md-prog-perf-20261020a, package md-prog-fixxdb-20261020a.
- Package dist/MechDefense-0.9.0.scmod 49,104,914 bytes, SHA-256 682281de…a450cb.
- Decisions made here without asking: the devil mech at x3 (the helper's recommendation: x4 is taller than trees); no
  Industrial Era exchange recipe (its steel, circuits and condensers can be manufactured, which would bypass the 5x rule).

## Round 2 (2026-10-21)

User: "恶魔机甲的移动方式有问题（一直蹬脚），敌方机兵最好做成那种可放置的。难度……需要提高，因为玩家一般会放置很多炮塔。而且现在物品材料模型没建好，
都是贴图". Asked and answered: placeable = creative-inventory summons (not craftable); item models = the confirmed plan (vanilla
item style, low-poly + 32 px textures, sizes per item, <= 300 triangles). Done: glide poses + fly-order fix, difficulty scaled to
the defence, summons, 3D item models (helper agent, E:/projects/md-item-models), lab parts; numbers and checks in docs/STATUS.md and
docs/PROGRESSION.md. Package SHA-256 475b550f…3f33db82.


## Round 3 (2026-10-21)

User: "应该加模组设置给玩家一些自定义选项：1.希望机甲下面的提示可以自定义位置，或者关掉……（可调整显示面板UI大小或者直接关闭），而且现在有个
问题，机甲的步枪会一直在装填和就绪之间来回切换 2.敌对机甲刷新可自定义刷新时间和刷新密度 3.一些bug，敌兵和我们的炮塔有时候互相识别不到，
敌兵刷新之后应该直接找离自己最近的炮塔攻击（没有炮塔就正常呆着，不要找玩家……优先级，机甲>炮塔>玩家）". Not asked back; decisions:
- Settings go into the game's "模组设置" (API 1.9.3+, modinfo.json `Settings`; the user asked for plugin-version settings there on
  2026-10-20). Panel: show, size 50-150 %, position as two 0-100 % sliders (any place, not a few presets), key hints. Robots:
  wild spawn time (off / night / all day), interval, density, first day, raid chance, raid size.
- Wild spawns use an own spawner (MDWildSpawns): the vanilla night spawner shared its small limit with wolves and bears and had no
  interval to set. "刷新时间" read as both the time of day and the interval.
- Player as a target: never sought out; fought back for 30 s after the player (or the player's herd) hits a robot, or when the
  robot is at the defences (a tower or Mercury within 96 m) and the player is in its weapon range with no mech or tower to hit.
- Root causes found: the rifle's 0.25 s between shots was shown as 装填中 (Cooldown > 0.15 s); a tower's sight line and its shots
  stopped at the first body, so a tower behind another tower (or the player, or Mercury) never fired; robots only looked 32-64 m
  around them, so a robot spawned farther from the towers wandered.
- Results: docs/STATUS.md (round 3). Lab parts settings / hud / spawns / targets and the full robots mode pass (job md-r3-full2);
  regression md-r3-reg1 (special, mech, smoke 0.5.0 -> 0.9.0, perf). Package SHA-256 956129bf…f9a1c4ce. The panel test found a
  second bug (placed from the previous frame's screen size, cut off when the size changes), fixed by placing it during layout.
