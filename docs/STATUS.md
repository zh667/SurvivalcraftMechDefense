# Status — 2026-10-31

Housekeeping 2026-10-25 (user: "中间产物都删掉吧"): all `local/runtime-*` (197) and the earlier rounds' `local/claude-work` folders (r3-r5,
r13-r16, progression, mech-review, k11, diag) were deleted, 29.7 GB. Screenshot paths under `local/claude-work/` named in the sections
below no longer exist; the numbers recorded here stand. Kept: `local/claude-work/bug1023` (the player's log and video),
`local/claude-work/r-0.12.0` (0.12.0 phone comparison), `local/claude-work/engine-src`, and every other folder of `local/`.


## 0.16.2: parts follow a stack-size mod; mech keys no longer reach other readers (branch fix/stacking-0162, PR pending)

- User (10-31): "那些配件最高只能40个堆叠，不能和铃兰适配自己改变 ，希望配件能和玲兰辅助设置的堆叠一致". Stack-size mods set each
  block's MaxStacking when a world loads (the vanilla rule, inventories ask GetMaxStacking(value)); MDPartBlock overrode
  GetMaxStacking with fixed per-kind sizes, so the raised value never reached the parts. Now the kinds above 4 (scrap, alloy,
  chip, core 40; kits 8; charge cell 16) return max(own, MaxStacking); the blueprint and the beacon (4) and the handbook (1) keep
  theirs, as that mod's 普通堆叠 tier covers blocks above 4 only (Mod/MDParts.cs). Tower deploy items never overrode it.
- Lab: stacking (Tests/TowerLab/Stack16.cs): default sizes, MaxStacking raised to 99 / 999 / 10,000,000, restored.
- User (10-31): "切换机甲的弹药类型（X键）会触发原版游戏的操作" (it opened the recipe book). Vanilla binds nothing to X in 1.9.3.1 or
  1.9.4; RecipaediaEX registers the mapping "Recipaedia" = X and polls Keyboard.IsKeyDownOnce in its GuiUpdate hook, which the
  PlayerInput clearing (MDMechKeys.Suppress) cannot reach. Now a key the mech used is taken off the keyboard's pressed-once flags for
  the rest of the frame (MDMechKeys.Consume, after ComponentInput, before ComponentGui and the Default-order components), also the
  mount key when it boards or leaves. Key audit of the same change: the middle button no longer picks a block while driving
  (PickBlockType), a mech action on a number key no longer also selects a hotbar slot, and the settings page names other mods'
  mappings on a key (ModSettingsManager.ModKeyboardMapSettings) next to the game's. Of the user's other mods, BF2042 TOR's
  "模式" is also X (its vehicle only). Readers that run before ComponentInput in a frame would still see a press.
- Lab: mech, "keys after the mech" (Tests/TowerLab/Keys16.cs watches ComponentGui's update, with a mapping on X registered as a
  mod's): while driving X x2, B, V x2, F, 3 bound to melee, R leaving: nothing seen there; on foot the same X is seen once from
  the keyboard and once by the mapping (the control).
- Package dist/MechDefense-0.16.2.scmod, 35,576,506 bytes, SHA-256 7d67864ab91f21cff20044fcebd1d99d4b163ac2e990b9d6f0c25191d77c8fb0.
  Verified (PC, isolated runtime): stacking and mech on 1.9.3.1 and 1.9.4, all on 1.9.3.1 (and all on 1.9.4 with the stacking
  change alone). Not verified: the user's game with the helper mod and RecipaediaEX installed; a phone.

## Delivered: 0.16.1: damage numbers for this mod's damage only (PR #17, merged 2026-10-31 on the user's approval, tag v0.16.1)

- User (10-31): "那个伤害数字显示，只是我们的模组物品有这个就可以，现在用别的模组或者原版物品造成伤害，都有这个伤害显示".
  Numbers were shown for every hit by a tower, a mech or a player, so a player's vanilla sword or another mod's gun showed them
  too. Now only hits whose attacker is a tower or a mech of this mod (Mod/MDDamageNumbers.cs); the handbook text says so.
- Lab: combat15 numbers part adds a player's projectile hit (no number) and then a tower's (one number).
- Package dist/MechDefense-0.16.1.scmod, 35,575,704 bytes, SHA-256 774a2b2d8ed10b7c8548db38f730b5324eff8684e2bd801449a3f6f638caaf0d.
  Verified (PC, isolated runtime): combat15 on 1.9.3.1 and 1.9.4 (a player's hit 0 numbers, a tower's 1), all on 1.9.3.1.
  Not verified: a phone.

## Delivered: 0.16.0: defence core and threat raids, boss poise, the handbook, element balance (PR #16, merged 2026-10-31 on the user's approval, tag v0.16.0)

The user's answers of 10-30 evening and follow-ups; design, numbers and what of the earlier breakdown is still not done:
docs/tasks/2026-10-31-threat-core-0.16.md.
- Balance (bench-mix, same mixed tower set, reactions on / off): a 12-robot wave 18 / 19.8 s, the devil mech 62 / 68 % left after
  120 s, RD08 39 / 67 %, RD21 86 / 85 s: reactions were not what made robots easy, so ordinary robots and boss armour get +20 %
  (RD05 1200, RD06 2900, RD07 3400, RD14 2400, RD09 720, EN02 2640, RP09 960; boss armour 36000 base, 3600 per tier).
  Reactions hit bosses at 70 % (the max-health part halved).
- Boss poise (Mod/MDBoss.cs): reactions, control hits, heavy shots and the mech cannon fill a poise bar; full = 4 s stagger and
  8 s of x 1.5 damage, then locked 30 s. After the user's "失衡值掉的太快了": towers add at most 4 a second, the mech counts in
  full, each break raises the next threshold x 1.5, decay 8/s after 2 s. Bars over each boss within 72 m (plate, health, poise
  with 失衡 / 失衡冷却) and in the Neorxna panel.
- Overhead layout: element icons, damage numbers, reaction names and boss bars sit at fixed heights and no longer overlap.
- Defence core (Mod/MDThreat.cs, tower defense_core, data 59, recipe iai/cxc/iai; the radar III's model for now): one live per
  world, a new one puts the old out; robots destroyed within 120 m raise threat (elites 3); full (30 / difficulty) starts a raid:
  30 s countdown, 3-6 waves 40 s apart, each countdown names the next wave, every second raid ends with a boss; endless mode;
  mod settings 防线难度 (简单 / 普通 / 困难 / 噩梦) and 无尽模式. Night raids and the boss by kills stay (their own settings).
- The handbook 机甲防线手册 (Mod/MDManual.cs): crafted from planks and coal, one given per player on a world's first load; chapters
  towers (damage-type icons, range, DPS), damage and status, reactions, mechs, enemies, bosses, the core. Its own model: a
  hardcover book (tools/build_part_models.py item 10, 132 triangles; E:/projects/md-item-models V2).
- The user's first play (10-31): "boss被击败好像不会增加威胁度" -> a boss destroyed near the core adds 10; "威胁度满30了但是没效果"
  -> the raid was skipped in Creative (the wild-spawn rule) while threat still rose: raids now run in every mode but Harmless
  (Harmless: no threat, said on the core); the core test had switched the world to Survival and missed it, it now runs in the
  lab's Creative world. "可以取消同一个地方只能出现一个boss的限制" -> beacons, Creative summons, the boss by kills and raids no
  longer wait for a boss to be gone; several beacons count down at once. A raid's last wave now waits for its boss, and the boss
  named by the countdown is the one that comes (it was redrawn when it set off).
- The user's second play (10-31, 1.9.3.1, screenshot): 液氮塔, “毒沼” and “酸洗” showed only their base, from the first one placed.
  A tower whose aim angle turned NaN loses everything above the base (yaw and pitch bones NaN) and never recovered: AngleTowards
  keeps NaN, and it was saved with the world. Shell towers need no line of sight, so they can take a creature the others never
  see; one with a NaN / infinite position passed InArea (every comparison with NaN is false), and a NaN velocity poisoned the
  lead point. Now: such creatures are never targets, a NaN velocity leads nothing, a NaN aim returns to rest on the next update
  (also on load), and the first of each is logged ("[MechDefense] towers skip ..." / "<tower>: aim NaN ... target ..."), so the
  creature behind it in the user's world can be named from Game.log. The user's Game.log (read-only, with their OK) had no
  error of this mod; one tower is drawn with 4 bones there, far below the 64 per draw the GPU allows, so it is not the
  per-draw bone limit. Lab mode aimnan (Tests/TowerLab/AimNan16.cs): NaN aim with the repair off shows the base alone (as in
  the screenshot), on: the turret is back within a frame; a target with a NaN velocity keeps the lead point a number.
- Lab: a result that does not serialize still writes lab-result.json (Lab.Save).
- Package dist/MechDefense-0.16.0.scmod, 35,575,693 bytes, SHA-256 d694e3b8e697a8dcfa22b6419a9262cfb00f7ae79fbb6ded03f3456a3d98a369.
  Verified (PC, isolated runtime): before the play feedback, the full set on both APIs (r16: all, bosses14, enemies14, phone,
  robots, combat15 on 1.9.3.1 and 1.9.4; combat, fb13, icons, special, mech-view on 1.9.3.1; faction-mods, bench-rules on
  1.9.4: 19 passed); after it, combat15 (both), robots (both), bosses14 (both). The handbook model seen in game on both APIs
  (hand, ground, night, hotbar). After the NaN-aim fix: aimnan (both), all (both), combat (1.9.3.1), combat15 (1.9.4)
  passed, none of them logged a skipped creature or a reset aim. Not verified: the creature behind it in the user's world; a phone; bench-mix with the +20 % health not rerun.

## Delivered: 0.15.1: medic charge cell fix (PR #15, merged 2026-10-31 on the user's approval, tag v0.15.1)

User: "现在医疗塔好像不能用那个核心充能（提示只能用于机甲）". The weapon-kit handler took every part from Mk1 on as a kit, and the charge
cell comes after the kits, so it answered "武器改装件只能装在机甲上". It now takes only Mk1-Mk3. The right-click chain is MDModLoader.Interact;
combat15 (medic) now clicks through it: a cell in hand fills the medic, a kit in hand still goes on Mercury. The 0.15.0 test had called
MDMedic.Charge directly and missed it.
- Package dist/MechDefense-0.15.1.scmod, 35,115,675 bytes, SHA-256 52434049c9080abb854b10660deea61981b07cf43a2a0b8a25cc58d5383a1fd3.
  Verified (PC): combat15 (1.9.3.1, 1.9.4), robots (1.9.3.1), all (1.9.4). Not verified: a phone.

## Delivered: 0.15.0: combat rework — damage types and reactions, no stagger from ordinary towers, control stops fire, growth, rewards, damage numbers (PR #14, merged 2026-10-30 on the user's approval, tag v0.15.0)

The user's answers to the nine questions of docs/COMBAT_DESIGN.md section 10 and "二～五按你的建议"; design, numbers and evidence:
docs/tasks/2026-10-30-combat-0.15.md, docs/COMBAT_DESIGN.md (2.5: benchmark before / after).
- Damage types (Mod/MDElements.cs): each tower kinetic / explosive / heat / cold / electric / corrosive; heat, cold, electric and
  corrosive leave a status; 8 reactions (thermal shock, overload, corrode, brittle, frost etch, electrolysis, spread, shatter)
  with cooldowns (1.5 s per target, 6 s per reaction); resistances x 0.75-1.5 per robot kind; status icons over targets and the
  damage type over towers (atlas Towers/status.png, tools/build_status_icons.py); reaction names rise over the target; mod
  setting 元素反应. Damage numbers (Mod/MDDamageNumbers.cs): merged per target over 0.35 s, at most 14 / 8 / 4 at once by
  特效数量, within 28 m; mod setting 伤害数字.
- Towers no longer stun or push anything they hit, except the heavy single shots (attack.stagger); stun and freeze stop a robot's
  fire; frost towers no longer push; the water jet pushes up to 800 mass (RD05, drones).
- Enemies: RD05 steps aside when aimed at and backs off when hurt; RD14 armoured in front (x 0.75) and weak behind (x 1.4), slower
  turret; RD07 rings where its shells land; RD05 and the drones go for players at the defences / alone; the repair drone (RP09,
  RD09's model tinted green) from 14 days after the start day. RD21 armour x 1.1; RD08 stays 22 m from its target.
- Same day, user follow-ups: medic towers hold a healing store (mod setting 医疗塔储能, default 50000 points, 0 = unlimited) that every heal draws on; empty they stop; the new item 医疗充能核心 (every boss drops 1-2, first kills one more) refills one; a bar over the medic (green left, grey used) and in the Neorxna info panel. Neorxna showed towers under their English template names: the rename patch was tried once at load and missed Neorxna when it loaded later; it now patches when Neorxna's assembly loads (lab on 1.9.4 now uses the user's Neorxna-v1.4.3). Mechs load ammo elements (key X): Mercury standard / heat / cold, Nemesis + electric, Regulus + corrosive. Four three-element reactions (x 3-3.5).
- Growth by stage (every 7 days from the start day, mod setting 敌人成长 0 / 5 / 10 / 15 % per stage, at most 5 stages); rewards:
  loot by toughness, extra for kills mostly by players / mechs, scrap per broken boss layer, the first kill of each boss kind,
  a payout for a beaten raid.
- Package dist/MechDefense-0.15.0.scmod, 35,115,621 bytes, SHA-256 6ba1e9ae8711f67909c2250a1a1ecad2b26ed4ebf3108ff7e80cd06c891a6da6.
  Verified (PC): full run on 1.9.3.1 (all, robots, enemies14, bosses14, fb13, combat, mech, mech-view, phone, icons, special,
  combat15, bench-rules, bench-towers) and 1.9.4 (all, robots, enemies14, bosses14, faction-mods, mech, phone, combat15,
  bench-enemies, bench-bosses, bench-fortress). After the damage numbers and the knockback change: robots, combat15 (both),
  combat, fb13, all (both), faction-mods, bench-rules again, all passed. After the medic charge, Neorxna, mech ammo and three-element
  reactions: combat15, robots, mech, all (both), bosses14, enemies14, phone (both), combat, fb13, icons, special, mech-view, faction-mods,
  bench-rules passed (bosses14 failed twice on RD08 keeping out of the towers' reach, fixed: it now stays on the towers' side). Not verified: a phone. Open: the flame towers' damage
  against robots rose a lot (no more pushed out of the flame), RD19 held out longer in the fortress benchmark (cause not checked).

## Delivered: 0.14.0: new enemies (RD14 tank, RD09 / EN02 drones, four new bosses), armour phases, combat benchmark (PR #13, merged 2026-10-30 on the user's approval, tag v0.14.0)

The sixth 2026-10-29 report; decisions and evidence: docs/tasks/2026-10-29-new-enemies.md; combat numbers and open questions: docs/COMBAT_DESIGN.md.
- Our own models (LOD1 in the package; high poly and LOD0 kept in E:/projects/dguns-enemy-models/v3_new): RD14 twin-barrel armoured tank,
  RD09 rotor gun drone, EN02 twin-rotor drone (regulars / elites from start day + 3), and the bosses RD13 bird-shaped heavy mech, RD21
  twin-sabre flying mech, RD08 heavy hover gunboat, RD19 heavy armoured gun carriage. Each boss comes at random with the devil mech
  (kills and beacon), every attack telegraphed (crouch, red rings, red lines, aim beams), each with one answer only a player or mech has.
- Boss damage cap: the 2 % per-hit cap became armour phases (two layers of 10 %, 20 % through, a break = 3 s stagger and 10 s at x 1.5,
  8 % per hit at most). Armour 30000 + 3000 per tower tier + mechs, at most 300000.
- User 10-30 "很多的炮塔和医疗塔配合，敌方boss还是不堪一击": a tower a boss hit cannot be healed for 8 s, medics no longer stack, fire goes
  through the armour phases; RD19 drives to a new spot while sunk ("不能移动").
- Fixed: control towers (EMP, shockwave, frost, liquid nitrogen) never held any robot (the boss resilience threshold); a boss coming down
  dug a pit (block clearing ahead of a downward velocity); RD08 hovered under water at sea.
- Lab: `bosses14`, `enemies14`, combat benchmark `bench` (towers / enemies / bosses / fortress / sample), `run_isolated.py --no-build`.
- Package dist/MechDefense-0.14.0.scmod, 35,027,049 bytes, SHA-256 d53e8da31e6aca8e54f3a77a5adc8550eb0929879660652e96203c046984e7c0.
  Verified (PC), full run 10-30 on 1.9.3.1 and 1.9.4 side by side: 1.9.3.1 all, robots, enemies14, bosses14, fb13, combat, mech, mech-view,
  phone, icons, special; 1.9.4 all, robots, enemies14, bosses14, faction-mods, mech, phone. robots and bosses14 failed once and were
  fixed (the pit, RD19 spot choice, the bosses14 shell test spot) and passed on both after the fix; the other labs ran on the package
  before those two boss fixes. Not verified: a phone.

## Delivered: 0.13.0: player feedback (silent weak point, Black Scorpion terrorists, medics, placement, phone heat) (PR #12, merged 2026-10-30 on the user's approval, tag v0.13.0)

Five player reports forwarded by the user; decisions: docs/tasks/2026-10-29-player-feedback.md. The sixth (new enemies) follows as 0.14.0.
- Devil mech: no chest flash and no "weak point changed" message; the weak channel still changes every 20 s, shown by its ring colour.
- Towers attack creatures of the herd "Terrorist" (the Black Scorpion gun mod's terrorists and their gun turrets; its neutral and
  guerrilla NPCs are left alone) and any creature whose attack target object points at the player's side.
- Medic towers no longer heal medic towers.
- Placement: a click on grass / flowers / snow, on a block's side, or beside a wall now places the tower; the footprint is the free one
  nearest the click; grass and flowers in it are cleared, crops and saplings block.
- Fast towers fire at most once a second with the same damage per second (gatling x4, missile x2, anti-air x2 / x3 per shot); a mod
  setting 特效数量 (完整 / 精简 default / 极简) thins smoke and small particles.
- Package dist/MechDefense-0.13.0.scmod, 29,613,066 bytes, SHA-256 eaaad1c0b4527ed8e20d1cacd28be3f21e9093f8a3b065267d599e27af22308a.
  Verified (PC): 1.9.3.1 fb13, robots (all parts), robots-heroes, all, icons, special, combat, mech, mech-view, phone, old save 0.5.0 -> 0.13.0,
  new world; 1.9.4 fb13, faction-mods (with the Black Scorpion gun mod), robots-targets / -boss / -settings / -encyclopedia, mech, phone, icons
  — 21/21 passed. Not verified: a phone.

## Delivered: 0.12.0: player feedback (raids on players, aggro, leaves, the devil mech by kills, tower chains and repair, phone drawing) (PR #11, merged 2026-10-25 on the user's approval, tag v0.12.0)

Seven player reports forwarded by the user; decisions and evidence: docs/tasks/2026-10-25-player-feedback.md.
- Phones (Honor WIN, Snapdragon): "机甲会隐身…炮台在特定视角也会隐身". Root cause from the engine source: a Snapdragon GPU reports 256 vertex
  uniform vectors, so the engine draws at most 30 bones of a model at once; the mechs have 38-40 and the devil mech 33, the draw throws,
  the exception is swallowed and every model after the mech in that frame's list (towers too) is skipped. `Mod/MDDrawBatches.cs` draws
  such models in batches. Reproduced and checked in the lab with the GPU limit set to 256 (`--mode phone`), not on a phone.
- Robots: a player, pet or mech that hits a robot is locked on: fought first and without switching until it dies, leaves or is 96 m away (round 2); robots attack a player they see
  within 16 m (mod setting 主动攻击距离, 0 = only when hit); their blasts and bodies clear leaves; a night without towers near any
  player sends the raid at a player (3 + day / 8 robots, following that player for 5 minutes).
- The devil mech comes after every 50 robots destroyed in a world (mod setting, 0 = beacon only), counted in Project.xml.
- Towers: health x2; repaired only with robot scrap (400) or alloy plates (1500), mechs still with iron and copper; 哨戒塔 from
  扩装铳械塔, 重锤迫击炮 and 序章炮塔 from 高爆榴弹塔, 电涌塔 from 磁暴线圈 Ⅰ, 全向声波塔 from 电磁脉冲 Ⅰ; tier 1 unchanged.
- Round 2 (user, same day): a robot also fights a player who hit it in Creative; a mech (or its pilot) that hits a robot is
  fought first, and the devil mech aims every attack (the whole missile volley too) at whoever hit it; the devil mech clears any
  block in its way (not bedrock, no drops) and takes at most 2 % of its armour per hit (CalculateCreatureInjuryAmount, as the
  Subnautica and Wonderful Era bosses do); the start day has no 30-day limit (0-99999).
- Package (round 2): dist/MechDefense-0.12.0.scmod, 29,610,728 bytes, SHA-256 b8deec2f48e64ba3a68b1e7582fe8b01f61d710849f91a6e48ceecf270aeb733.
  Verified (PC): on 1.9.3.1 robots (every part), robots-targets / -boss / -bossdig / -settings / -encyclopedia / -heroes / -leaves (x2),
  mech, combat, special, all, phone, old save 0.5.0 -> 0.12.0, new world; on 1.9.4 robots-targets / -boss / -bossdig / -settings /
  -encyclopedia / -heroes / -raidplayer / -bosskills / -leaves (x2) / -repair, mech, phone. One full robots run failed once on the
  leaf test's missile shots (fired 18 m from a 9 m leaf crown they circled it; the test now fires from 34 m and waits for every shot).
- Round 1 package (superseded): dist/MechDefense-0.12.0.scmod, 29,610,071 bytes, SHA-256 4e5b577873fb26bd2d22cd8a9adcc382d8a51d866550a44df6b1c732a66102da.
  Verified (PC, isolated runtimes): on 1.9.3.1 phone, all, icons, robots (every part), robots-heroes, special, combat, mech, mech-view, old
  save 0.5.0 -> 0.12.0, new world; on 1.9.4 phone, mech, robots-settings / -encyclopedia / -heroes / -items / -targets / -raidplayer /
  -leaves / -bosskills / -repair, icons, mech-view — 24/24 passed. Not verified: a phone, a real survival world over a night.

## Delivered: 0.11.0: mech key bindings, enemies in the bestiary, towers and mechs in Help (PR #10, merged 2026-10-24 on the user's approval, tag v0.11.0)

User: "机甲绑定按键编辑（类似于CS枪械适配铃兰触控那样） 敌人图鉴（在帮助里可以看到敌人信息，包括数值什么的，炮塔这边也是）".
Design and choices: docs/tasks/2026-10-24-mech-keys-and-encyclopedia.md. Contains the 0.10.1 hotfix below.
- Mech keys (`Mod/MDMechKeys.cs`, page section 机甲按键 in `MDSettingsScreen`): hover, jump, melee, weapons, view, plus an extra fire
  key and an extra board key; per device in ModSettings.xml. Phones: 铃兰触控 puts screen buttons that press these keys.
  Found in the lab: a mech key that is also a game key (C clothing, K precipitation) opened the game's panel, which then blocked the
  mech's input; while driving, a pressed mech key now clears the game's actions on that key (`MDMechKeys.Suppress`). The page names
  the game action a chosen key also has and refuses the game's mount key.
- Bestiary: 双管 / 导弹 / 炮击步行机兵 and 恶魔机甲 (hidden egg types 3900-3903, no egg item or recipe); the list shows the
  short description, each page adds 【数值】 from live data and the current mod settings; big robot models are re-framed to fit.
- Help (`Mod/MDEncyclopedia.cs`): 机甲防线 · 防御塔总览 / 一阶 / 二阶 / 三阶防御塔 / 主角机甲 (index 900-904), text written when a
  topic opens from towers.json, the loaded recipes, MDMechType and the current keys.
- Package dist/MechDefense-0.11.0.scmod, 29,603,469 bytes, SHA-256 b4f6775e942b2418ca741b31a43bf03bc65d542f3502f11b1aae7ba5adfe17b1.
  Verified (PC): on 1.9.3.1 all, icons, robots (incl. settings + encyclopedia), robots-heroes, special, combat, mech, mech-view, old
  save 0.5.0 -> 0.11.0, new world; on 1.9.4 mech, robots-settings, robots-encyclopedia, robots-heroes, icons, mech-view — 16/16 passed.
  The lab presses the rebound keys for real (K boards and leaves, G strikes, N switches, C hovers without opening the clothing panel,
  the old F / B do nothing) and checks every tower's Help paragraph against towers.json. Not verified: a phone with 铃兰触控.

## Delivered: 0.10.1 hotfix (branch fix/android-api194, PR #9; merged into main with 0.11.0 on 2026-10-24)

Player report (Android phone, API 1.9.4, log + video): "玩家打开武器那一些闪退出世界，并且机甲放置出来不可见".
- Crash, root cause found: in the API 1.9.4 engine, `Engine.Vector4.Transform` (and the array forms of Vector2/3/4.Transform and
  TransformNormal) take a SIMD path that falls back to x86-only `Sse` calls when the runtime reports vector acceleration but no ARM
  AdvSimd; on that phone it throws PlatformNotSupportedException. The tower item pictures (`MDTowerLibrary.DrawPicture`) used it, so
  opening the inventory page with the towers dropped the player out of the world (stack trace in the player's log). Fixed: the
  projection is computed by hand. A PC cannot reproduce it (Sse works there), so every lab run now first scans the mod's IL for those
  calls (`SimdTraps`, proven on `SimdTrapProbe`; it flagged 0.10.0 and passes on 0.10.1).
- Invisible mech: not root-caused in 0.10.1 (the likely cause, the phone GPU's draw limit, is handled in 0.12.0 above). The player's log has no session with a placed mech; on the PC 1.9.4 build all three mechs draw.
  Each placed mech now writes one "[MechDefense] mech ..." line at its 2nd and 200th frame (model, registered with the renderer,
  IsSet, DisableDrawing, visible, opacity, draw orders, mode, bounding sphere, view range, mods that override
  OnModelCalculateIsVisible), so the next player log shows what the renderer sees.
- `tools/run_isolated.py --source 1.9.4` runs the lab on the user's API 1.9.4 Windows build.
- Package dist/MechDefense-0.10.1.scmod, 29,590,172 bytes, SHA-256 3c799e5e084e94f08b54c3898fb582633081a1fac883ad1b2053e749e688a52f.
  Verified (PC): on 1.9.4 mech-view, mech, robots-heroes, robots-gallery, icons, robots-items; on 1.9.3.1 all, icons, robots,
  robots-heroes, special, combat, mech, mech-view, old save 0.5.0 -> 0.10.1, new world — all passed. Not verified: the phone itself.

## Delivered: 0.10.0 (branch feat/more-towers-enemy-models -> main, 2026-10-23)

User 2026-10-23: "转轮机枪塔那个游戏的其他炮塔都可以开始建模了，并且恶魔机甲和地方机兵也可以我们自己重新建模 … 这些都可以直接放进游戏打包，
另外现在已有的pr你可以在合适的时机合并了"; later rounds: frost / flame colours lighter, missile tower thicker without rings and firing
real missiles, then "新加的这些炮塔AI味太重了，能按照终末地炮塔的风格建模吗" -> the gatling sample was approved ("挺不错的，其余的都按照这个做吧"),
the radar should boost every tower in a larger range without lines or pings, the cannon hits only the ground.
- The 2D tower-defence family is complete: 9 types x 3 levels (data 32-58) in our Endfield towers' style (poly_v2, one shared
  Endfield-style base, <= 7,420 triangles, outline overlap with the sprites 0.765-0.873), see docs/TOWERS.md "新防御塔 v2".
  New mechanics: turning sprayers (frost slow, flame burn), homing missiles (attack type "missile"), recoiling cannons (ground only),
  anti-air (air first, half damage on the ground, raised barrels at rest), radar range boost for every tower in range (silent),
  tesla chain lightning (1 / 2 / 3 targets), EMP stun pulse with a turning rotor. Original game sounds (mono).
- Our own polygon enemies: walkers RD05 / RD06 / RD07 (3 skins each) and the devil mech RD17 (crimson), see docs/PROGRESSION.md;
  node trees, clips, muzzles and boxes unchanged (tools/build_unit_assets.py POLY_UNITS / --poly-only).
- Package: dist/MechDefense-0.10.0.scmod, 29,588,923 bytes, 368 files, SHA-256 9995ff0489f2ba09997feb045ffb140eee1ce8993cf3de52c57fc86c1bea5f8f;
  shown texts name none of the source games ("Endfield" appears only in internal sound paths, as in 0.9.0).
- Verification (isolated runtime, PC, on this package): all (37 towers), icons, robots (every part), robots-heroes, special, combat,
  mech, old save 0.5.0 -> 0.10.0 and a new world all passed; per type view-<id> for all 27 new levels (firing, spin, recoil, radar
  boost of 4 towers in range and none outside), robots-boss / -fight / -raid with the new enemies. Not verified: Android, a real
  survival world, frame time with many towers on a phone.

## Previous delivery: 0.9.0 (PR #7 into feat/progression-robots, PR #5 into main; merged 2026-10-23 on the user's approval, tag v0.9.0)

User 2026-10-23: "现在已有的pr你可以在合适的时机合并了" — merged after 转轮机枪塔 Ⅰ was re-centred ("1级的转轮机枪塔，机枪位置并不是在正中位置")
and the package passed view-gatling_1 (spin 1.0, 17 shots), all, icons, robots-items, special and old save 0.5.0 -> 0.9.0.
Next (same message): the other 8 tower types and our own models of the walkers and the devil mech, on new branches from main.

### New towers (feat/new-towers)

User 2026-10-22: rebuild every tower (and level) of a 2D tower-defence game ourselves, numbers from our towers and the mods'
creatures; extract its special weapons; then "可以直接放到游戏里面，打一个包，和复仇女神，轩辕十四的我们自建模一起放到同一个包里".
Plan confirmed the same day (9 types x 3 levels, polygon style of the 19 towers, sprite-camera outline overlap >= 0.85, <= 6,000
triangles + one 1024 atlas, crafting upgrades, the original sounds, 转轮机枪塔 first); base changed to 2x2 blocks after the sprite
proportions ruled out a 1-block base. Details: docs/TOWERS.md "新防御塔".
- 转轮机枪塔 Ⅰ-Ⅲ (data 32-34, commit fb732d7): models from E:/projects/sog-tower-model/poly_v1 (3,748 / 4,232 / 5,704 triangles,
  2.37 / 2.57 / 2.79 m, overlap with the sprites >= 0.868), "polyOnly" tower build, spinning barrel clusters, original fire / hit
  sounds (mono), recipes in the gun towers' tier chain, 32 / 54 / 96 damage per second, range 28 / 32 / 36, health 800 / 1000 / 1300.
- Reference pack (sprites per level, stats, the three special weapons: space laser, EMP bomb, carpet bombing):
  E:/projects/sog-tower-model/reference/ (not shipped). The special weapons are only extracted, not in the mod.
- The polygon 复仇女神 (c098e73) and 轩辕十四 (08068a5) are on feat/progression-robots, merged in here.
- Package (built from this branch, everything above + 0.9.0; 转轮机枪塔 Ⅰ re-centred): dist/MechDefense-0.9.0.scmod, 18,749,134 bytes, 260 files,
  SHA-256 8195000e...7efb54a0; no 终末地 / 毁灭枪手 / 原作 or the source game's name in shipped text.
- Verification (isolated runtime, PC): view-gatling_1/2/3 (spin rate 1.0 while firing, the barrels turn ~2.7 rad between two
  close shots), all (22 towers load), robots-items (33 recipes), icons; mech-view-nemesis / -regulus before / after,
  robots-heroes; final regression on the package: mech, robots-heroes, robots-gallery, robots-summons, robots-items, all, icons,
  special, combat, view-gatling_3, old save 0.5.0 -> 0.9.0, all passed. Not verified: Android, a real survival world.

## Previous: 0.9.0 progression (branch feat/progression-robots, PR #5 open, not merged)

Round 5 (user 2026-10-21: enemies a bit too strong early, Mercury too fragile but "不要提升太多"; no damage to the pilot in the
mech; medic tower heals players and mechs; new hero mechs 复仇女神 / 轩辕十四 (水星 < 复仇女神 < 轩辕十四); fewer tower triangles;
inventory icons miss chunks). Plans confirmed the same day (balance numbers, hero mech plan, one reduced tower first):
- Done (commits 190c993, 843c636, f7d3d6a): pilot shield (MDMechPilotShield), robots skip a seated pilot and their shots at it hit
  the mech; the medic tower now also heals the mechs (players and towers already); Mercury armour 4000; devil mech armour 30000 + 1000 / 2000 / 3000 per tier-1/2/3
  tower + 3000 per Mercury. Lab part `robots-balance`: shield blocks even invulnerability-ignoring injuries; medic: Mercury
  50 -> 100 % and player 60 -> 100 % in 6 s; boss vs 3 tier-1 towers + Mercury 36000 armour, after 30 s boss 70 %, Mercury 68 %.
- Hero mechs (commits dcb55f9, d08abe7; plan confirmed: original NEMESIS model 208 / REGULUS model 216 like Mercury, x4,
  same controls): MDMechType (Mercury / 复仇女神 / 轩辕十四: armour 4000 / 6000 / 8000, damage x1 / 1.25 / 1.5, energy 100000 /
  130000 / 160000). 复仇女神: the original's 3-round rifle burst (0.133 / 0.133 / 0.433 s) and explosions -20 %; 轩辕十四: 15 %
  faster walking and hover, its own glide pose, 2 missile locks (original). Recipes: 水星 + blueprint + 2 cores + 2 chips + 3 alloy,
  level 12; 复仇女神 + 2 blueprints + 4 cores + 2 chips, level 16; both in the creative inventory; kits shared. Assets by
  tools/build_hero_assets.py (helper agent, E:/projects/dguns-mech-model/v9_heroes; 2,481 / 2,345 triangles, 2048 texture).
  Devil mech armour per hero +4000 / +5000 and raid weight 5 / 6 (my extension of the confirmed +3000 per Mercury).
  Lab part `robots-heroes` (55 shots): armour, no missing bones, rifle gaps 0.14 / 0.14 / 0.44 s, damage 1000 / 1250 / 1500,
  复仇女神 takes 48 of a 60 missile, 轩辕十四 walks 12.2 m vs 10.8 m and hovers in dash_forward, cannons fire, dismantle keeps
  type and kit, Neorxna names, 30 recipes resolve (levels 12 / 16). Shots: local/claude-work/r5/sheet_heroes.jpg.
- Inventory icons missing chunks (commits 57fc378, ea9a45a): the tower icon mesh kept only the 1,500 largest triangles, so
  7.5-26.7 % of each tower was missing in a slot (the medic tower's whole dome). tools/render_tower_icons.py (helper agent)
  renders each full model from the slot's view into Towers/<id>/icon.png; the slot draws it as one quad facing the slot camera
  (premultiplied, mipmapped). 0.4.0's "像张纸" picture was a DrawFlatBlock card lying in the block's plane, seen at a slant and
  also held in the hand; the hand keeps the full model. Mech and robot icon meshes keep every triangle: unchanged. Lab mode
  `icons`: 19 pictures load at 256x256, hotbar pages at 1280x720 and 2560x1440 (local/claude-work/r5/icons/hotbar_big.jpg).
- Tower reduction prototype (commit b754763, tools/reduce_tower.py, not in the package): battle_rocket_1 142,608 -> 23,702
  triangles, largest part 141,020 -> 23,765 vertices (16-bit), 12 views 0.77 % different on average, worst 1.37 %; gaps 124 -> 132
  (888 -> 848 px), same-part overlaps 14 -> 4; atlas 1024x1024 -> 2048x1024. Cost: thin 3 mm dark lines along the yellow band
  edges gone within 0.6 m. Waiting for the user before the other 18 (docs/TOWERS.md 0.9.0 第五轮). Mech models are already
  2,345-2,481 triangles: no reduction.
- Polygon towers (user 2026-10-21: the reduced rocket "看起来效果差了很多" -> remodel it as a polygon model, outline like the
  original, own details, no Endfield marks, <= 6,000 triangles; approved: "我觉得这次建模非常不错！可以放到正式包，并且其他的炮塔都可以
  按照这个来"): battle_rocket_1 now ships the polygon model `E:/projects/endfield-turret-model/poly_v1/battle_rocket_1` (5,716
  triangles, one 1024 atlas, outline within 2.6 % of the original, 0 triangles in common with it) through `"poly"` in
  tools/towers.json: build_tower_assets.py checks its node tree against the cube build and reuses the cached cube metadata
  (pivots, muzzles, clearance). In-game comparison before it shipped: tools/stage_tower_preview.py + lab `view-<id>` (now with
  fog and a torch-lit cave), local/claude-work/r5/towers/battle_rocket_1/poly_game/. Regression on the new package: lab all,
  icons, view-battle_rocket_1, smoke 0.5.0 -> 0.9.0 pass. The other 18 towers (record table round 13, six helper agents in
  parallel): 3,076-5,724 triangles (battle_cannon_1_dg002 10,946 with a 2048 atlas), outline within 2.9 % of the original,
  0 triangles in common with it, 0 interpenetration in the motion sweeps; each group compared in game against the cube V5
  (local/claude-work/r13/<group>/<id>/, overview r13/overview_19.jpg). User 2026-10-22: "换进正式包吧" -> all 19 towers ship
  polygon models ("poly" in tools/towers.json); package 29.9 -> 17.2 MB.
  Regression on that package (job pz2-official-poly18-check-20261022-a): every tower's model, atlas (pixel-identical), slot
  picture and towers.json entry equal those of the preview package checked in game; lab all / icons / mech / robots-gallery /
  robots-summons / robots-items / special / combat and smoke 0.5.0 -> 0.9.0 pass.
- Polygon Mercury (user 2026-10-22: "可以把现在三台机甲，我们也自己建模一下", Mercury first): v10 by a helper agent
  (E:/projects/dguns-mech-model/v10_poly/mercury, 5,270 triangles, node tree identical, per-clip interpenetration no deeper than
  the original, 0 triangles in common with it). In game first through tools/stage_mech_preview.py and the new lab mode mech-view
  (same cameras before / after, local/claude-work/r13/mercury/): lab mech passes on both, flicker equal; visibly lighter and bluer,
  the bayonet became a long barrel. User: "这一轮炮塔和水星都可以" -> shipped as it is: tools/build_mech_assets.py swaps it in
  (POLY; --poly-only). Package 17,247,141 bytes; its Mercury files equal the preview's; lab mech / robots-heroes /
  robots-gallery / robots-summons / robots-items / all / icons / special / combat and smoke 0.5.0 -> 0.9.0 pass (job
  mz1-mercury-official-20261022-a). 复仇女神 and 轩辕十四 are being remodelled the same way (dguns round 14, helper agents).
  PR #5: the user said not to merge yet (2026-10-22).
- Final regression on the package (jobs md-r5-final-reg-1, md-r5-smoke-2): full `lab --mode robots` passes (223 shots; all parts
  incl. heroes, balance, settings, hud, escorts, raid, boss, kits); special and mech pass. Smoke 0.5.0 -> 0.9.0 first failed one
  stale check (it expected towers + Mercury in the creative inventory; the hero mechs add two): check updated, passes. The failed
  result had still exited 0: tools/run_isolated.py now exits non-zero unless the result says passed. Perf (9 towers in the
  hotbar / 30 towers + hotbar): 0.8.0 1.18 / 1.27 and 1.22 / 1.19 ms, 0.9.0 0.65 / 1.09 and 0.65 / 1.08 ms (slot pictures instead
  of 1,500-triangle meshes).
- Package size (user 2026-10-21 asked how to shrink it; chose lossy WebP quality 90 for the mech and robot textures): package.py
  re-encodes the PNG textures embedded in Assets/Imported/*/*.glb as WebP (tools/webp_glb.py, glTF EXT_texture_webp, which the
  1.9.3.1 engine lists; sources stay PNG). 28.6 MB of textures -> about 3.8 MB, PSNR 36.8-42.8 dB, mean difference 1-1.7 levels.
  Lab mech, robots-gallery, robots-heroes pass on both packages with the textures drawn (local/claude-work/r5/webp/), smoke
  0.5.0 -> 0.9.0 passes. Not changed: sounds (already OGG, 2.3 MB), tower atlases and icons (to do with the polygon towers).
- Lossless size steps (2026-10-22): textures the mod loads by name without an extension ship as lossless WebP (tower atlases,
  Towers/fx, Parts/parts; tools/package_formats.py, exact=True keeps the colour under transparent pixels; every one checked
  pixel-identical), the mech/robot icon textures as WebP quality 90 (PSNR 34-40 dB), tower slot pictures stay PNG (opened by file
  name). icon.bin stores shared corners once (rocket 480 -> 317 KB, same 5,716 triangles). Lab all / icons / mech /
  robots-gallery / robots-summons / robots-items / special and smoke 0.5.0 -> 0.9.0 pass.
- Test games no longer trap the user's cursor: with no menu open the engine set raw mouse mode in Mouse.BeforeFrame and
  AfterFrame whenever its window counted as active, even behind other windows, which clipped the system cursor to the game
  window. TowerLab, RuntimeSmoke and PerfCompare patch both to a visible mouse (clipped 7 of 133 samples before, 0 of 149 after).
- Package dist/MechDefense-0.9.0.scmod 29,900,478 bytes, 242 entries, SHA-256
  10913e8d495ff6109b7abbf25f6b1e8676ac0fe4119515a343fa7442bcd6cb56 (before the lossless steps 32,700,124 bytes, 693db794…3cdd15) (polygon rocket with PNG textures: 58,615,878 bytes;
  before the polygon rocket: 61,061,299 bytes) (no 终末地 / 毁灭枪手 / 原作 in data files or the dll).
- Not tested: Android (picture loading and mipmaps on phones), real touch, a survival night with raids, long play; the medic
  rate for mechs (50 -> 100 % in 6 s) is untuned and may be strong in real fights.

Round 4 (user 2026-10-21: adjust the panel on a game page like the CS weapons mod, fingers and mouse/keys to scale and turn; the
devil mech's escorts never attack mech, towers or player; all settings on one page like the CS weapons mod, start day 0 = no
spawns; what if a tower is blocked by its own side):
- Settings: the game's 模组设置 has one entry (MDSettingsEntryWidget) that opens the mod's page MDSettingsScreen (Mod/MDSettingsScreen.cs):
  mech panel button + summary, start day 0-30 (0: no wild spawns and no raids; beacon still works), spawn time, interval, density
  (with its group size and limit spelled out), raid chance and size; 恢复默认 / 取消 / 保存 on a copy. Saved by
  MDModLoader.SaveSettings / LoadSettings into ModSettings.xml (round 3's data-driven items and Assets/Lang removed).
- HUD editor MDHudLayoutScreen over a frozen game picture (MDWorldBackground): the real panel where it will be; drag, two fingers
  move / pinch / twist (MDHudGesture), wheel scales, Shift + wheel or Q / E turns, buttons 放大 / 缩小 / 左转 / 右转 / 恢复默认,
  show and key-hint switches, controls collapse, swap sides and move away by themselves when they cover the panel. In play
  MDHudHost places the panel from MDHudPlacement (centre, scale x screen fit, turn) while measuring.
- Escorts (and raids) vanished: SubsystemSpawn packs away every AutoDespawn creature over 60 m from all players every 2 s and
  recreates it later from its template without its orders; escorts appear 40-50 m beyond the beacon, raids 48-64 m beyond the
  towers. Diagnostic lab part `robots-escorts`: 2 of 3 escorts gone within 3 s at full health, back later as plain robots.
  Raiders now keep AutoDespawn off while busy (target, tower to go to, goal) and go for the nearest tower or Mercury first.
- Tower blocked by its own side: already handled in round 3 (MDCombat.Trace passes the player's side); answered, no change.
- Lab parts (first runs after the changes): settings, hud, escorts (A 3/3 and B 2/2 escorts fired, none vanished), spawns
  (start day 0 blocks), raid (0 vanished) pass. Shots: local/claude-work/r4 (sheet_r4.jpg).
- Mercury in Neorxna's info panel read 已移除的旧单位 (user): the mod's own hook named every non-tower unit so since 0.6.0, and
  Mercury uses the old mech template. Now 水星机甲 with armour, kit and pilot state (lab: real Neorxna panel, after a tower).
- Full `lab --mode robots` passes (job md-r4-reg1, 172 shots); special, mech, smoke 0.5.0 -> 0.9.0 pass; kits with the Neorxna
  check (md-r4-fin1). Perf on the final package: 0.8.0 1.08/1.14 and 1.08/1.12 ms vs 1.06/1.14 and 1.17/1.22 ms (an earlier
  pair in this round spread 1.04-1.55 in both builds: machine noise).
- Package dist/MechDefense-0.9.0.scmod 50,788,769 bytes, 214 entries, SHA-256 87612cdd5b55195d9cc1aae9a564f227aa06bb81a426cd3e91ea9b013ed78917
  (no 终末地 / 毁灭枪手 / 原作; MechDefense.xdb unchanged).
- Not tested: touch gestures on a real phone (the editor's pinch / twist are only exercised through the same code path the
  buttons use; the drag and wheel paths were not driven by real input in the lab), Android, the nightly raid trigger, long play.

Round 3 (user 2026-10-21: mod settings for the mech panel (place, size, off: a phone screen is full) and the robot spawns (time,
density); the rifle flips between 装填中 and 就绪; robots and towers sometimes miss each other; robots should go for the nearest
tower at once, leave players alone without towers, priority mech > tower > player):
- Mod settings in the game's 模组设置 (modinfo.json `Settings`, Mod/MDSettings.cs, option names in Mod/Assets/Lang): page 机甲面板
  (show, size 50-150 %, position 0-100 % across / down, key hints) and page 敌方机兵 (wild spawn time off / night / all day,
  interval 15-600 s, density 0.25-4x, first day 1-30, raid chance 0-100 %, raid size 0.5-3x). Changes apply at once.
- Panel (MDStatusWidget) placed while the layout is measured: placed from the previous frame's size it was cut off when the
  screen size changed (seen in the lab's 1280x720 capture). Rifle status 射击中 while firing (its 0.25 s between shots showed as
  装填中: 21 flips in 3.5 s in the lab, now 就绪 -> 射击中 -> 就绪).
- Towers: sight lines and shots pass through the player's own side (MDCombat.Trace): a tower behind another tower used to see only
  the front tower and never fired (lab: the old rule's first body was the front tower; now the back tower fires).
- Robots (ComponentMDRobot): order Mercury in sight > tower in sight > either out of sight > players / the player's herd; walk to
  the nearest tower or Mercury within 96 m at once (Hunt); players only for 30 s after they hit the robot, or in weapon range when
  the robot is at the defences. Wild spawns: own spawner MDWildSpawns (the vanilla night spawner shared a limit of 6 with wolves
  and bears and had no knobs): every interval, 32-56 m from each player, 1-2.5 x density per group, at most 4 x density within
  80 m; raids take the chance, size and first day from the settings; spawn points never on tree tops.
- Lab parts `robots-settings|hud|spawns|targets` (Tests/TowerLab/Robots.cs), first runs: settings, spawns, targets passed;
  hud failed on the panel place (the bug above), passed after the fix. Results and shots: local/claude-work/r3.
  - settings: 2 pages / 11 items registered, page widgets built with the Chinese names, 关闭 / 仅夜晚 / 全天, Set -> OnModSettingChanged.
  - spawns: rules (creative: game mode; by day: daytime; all day: spawns; day 30: blocked; off), limit 4 at 1x and 8 at 2x,
    33-59 m away; 40 s of survival every 15 s: robots after 15 s, none went for the player; a hit robot fired back 8 times.
  - targets: a robot 50 m from the only tower targeted it after 8 s and fired after 11 s; Mercury before a nearer tower; the back
    tower of two in a line fired 6 times.
- Full `lab --mode robots` passes (job md-r3-full2, 165 shots; boss 46000 armour, hovers 3.0 m, glide clips only, three attacks,
  escorts at 69 %, blueprint; raid 14 + 7; raid size x2 plans 12 for 6). Regression (job md-r3-reg1): special and mech pass, smoke
  0.5.0 -> 0.9.0 passes, perf 0.8.0 1.02/1.08 and 1.01/1.09 ms vs 1.02/1.09 and 1.04/1.09 ms.
- Package dist/MechDefense-0.9.0.scmod 50,780,004 bytes, 216 entries, SHA-256 956129bfa6e6179f4939bf13e8e976c92af7247cbe4e6721eebf8bc8f9a1c4ce
  (no 终末地 / 毁灭枪手 / 原作; MechDefense.xdb unchanged this round).
- Not tested: the nightly raid trigger itself, a whole survival night of wild spawns, Android (the settings page and the panel on
  a phone), long play in a real save, multiplayer.

Round 2 (user 2026-10-21: the devil mech "一直蹬脚"; enemy robots placeable -> chose creative summons; difficulty up because players
place many towers; the item models were only pictures -> 3D model plan confirmed the same day):
- Devil mech flight: holds the end pose of the one-shot dash clips (10-13) by movement direction, idle hover when slow; the
  pedalling fly_forward / strafe loops are never played (lab: base clips idle, dash_forward/backward/right only). Its fly order is
  re-set right before locomotion (Harmony prefix MDBossFlyOrder): the vanilla pilot adds (0,-0.5,0) to idle flyers and, depending
  on component update order, pulled it onto the ground in the full lab run. It also keeps 1 m above the towers under it.
- Difficulty: robots 1000 / 2400 / 2800 armour, per shot vs structures 14 / 60 / 140; raids sized by the cluster's defence
  (tier 1/2/3 = 1/2/3, Mercury 4): 3 + defence/2 + day/8 (max 20), elites up to half, a second wave at defence >= 20; 75 % of
  nights. Boss armour 40000 + 2000 per tower (+4000 per Mercury) within 48 m (max 160000), escorts at 70 % and 35 %, 10-missile
  volleys, stronger beam and slashes. Lab: one robot beats one tier-1 tower, two towers kill it; 9 towers (defence 21) drew 14 +
  7 robots and lost 3 towers in 28 s; the boss (46000 vs 3 tier-3 towers) left them at 24 / 33 / 64 % after 30 s.
- Creative summons (Mod/MDSummon.cs): one per robot template + the devil mech, the unit's icon mesh in slots, right-click the ground.
- Item 3D models: helper agent, `E:/projects/md-item-models` V1 (README, 反馈记录表.md), tools/build_part_models.py replaces
  build_part_icons.py (deleted); 52-214 triangles each, drawn x8/3 so the alloy plate matches the vanilla iron ingot; emissive
  core band, beacon lamp, Mk3 connector. In game: ground by day and night (glow shows), hotbar, first person (same crop and size
  as vanilla ingot / battery / diamond in the same view), third person. Sheets: local/claude-work/progression/items_a|b|c.jpg.
- Lab now runs parts: `lab --mode robots-<items|gallery|fight|raid|boss|summons|kits>`; full `robots` passes (job md-prog2-robots-5).
  Regression on the final package: special and mech pass (Mercury damage unchanged), smoke 0.5.0 -> 0.9.0 3/3 + 20/20, perf
  0.8.0 1.05/1.08 and 1.03/1.09 ms vs 0.9.0 1.02/1.08 and 1.03/1.09 ms.
- Package dist/MechDefense-0.9.0.scmod 50,776,034 bytes, 214 entries, SHA-256 475b550f635a9967f86b3d118b122f6a679042ac7f1047f332cd347d3f33db82
  (no 终末地 / 毁灭枪手 / 原作 in data or the dll; the drifted turret_1 radius restored again).

Round 1 (2026-10-20):

- User (2026-10-20): asked how towers and mechs should be crafted and grow from low to high tier, with three player
  suggestions (Industrial Era's T-100, the devil mech dropping blueprints, hostile robots dropping materials). Accepted plan with
  decisions: 4 materials + 1 blueprint, fallback recipes without fighting cost 5x, the blueprint is used up; robots both spawn
  in the wild and raid the towers at night; only the mech needs a player level; modelling by the modeling-workflow skill.
  Brief: docs/tasks/2026-10-20-progression-robots.md; design and numbers: docs/PROGRESSION.md.
- Enemy assets: `E:/projects/dguns-enemy-models` (V1, helper agent; README, 反馈记录表.md, HANDOFF.md with job ids) and
  `tools/build_unit_assets.py` -> `Mod/Assets/Imported/RD05|RD06|RD07|RD17` (ignored). Walkers x4 like Mercury (3.1-3.4 m),
  three skins each; the devil mech x3 as recommended (9.6 m tall, 15.4 m wings; x4 would be 12.8 m / 20.5 m). Re-pose
  p99 <= 0.005 mm, glb readback 0.0005 mm, 16-bit indices (<= 794 vertices per part).
- In game (isolated runtime of the user's build 1.9.3.1 with Neorxna, creative flat world, daytime; `lab --mode robots`, job
  md-prog-t_robots-8): 28 recipes resolve from their own grids (19 towers, Mercury, 8 items; tier rules; Mercury blocked at
  level 7); 9 items with names, stacks, flat icons and hand-held extrusions; 9 robot templates load (16 meshes, 19 bones, no
  missing joint, loot lists); every walker clip rendered; one normal robot against a tier-1 gun tower: walks to 21 m,
  fires 8 bolts, the tower loses 9 %, the robot dies and drops scrap x3-4 + experience; a raid (day 12) sends 4 incl. an
  elite, natural spawn rules spawn 1-2 normal + 1 elite when forced and nothing in Creative / by day; the beacon counts down
  30 s, the devil mech flies in and hovers at 3.00 m, weak channel 900 vs 360 of 900, in 30 s against three tier-3 towers:
  3 volleys, 2 beam sweeps, 2 sabre charges, 2 weak-channel changes, then destroyed it drops the blueprint (+ cores, chips,
  scrap, experience); kits: Mk2 refused before Mk1, Mk1 then Mk2, a 1000 hit deals 1300, kept through take-down and redeploy.
  Screenshots: `local/claude-work/progression/lab-screenshots` (ri_* items, rb_* robots and every walker clip, rf_* fight,
  rr_* raid, rx_* boss), contact sheets one folder up (sheet3.jpg is the final run).
- Found and fixed in the lab: a stunned creature's locomotion turns gravity back on, so every tower hit (0.2 s stun) made the
  boss sink to the ground; the boss now takes no stun or knockback. The lab's first fight placed the tower on the player
  (crushed, orange death screen in the screenshots): test bug, fixed.
- Regression on the final package: `lab --mode special` (recipes, hand, effects, artillery) and `lab --mode mech` pass, Mercury's
  weapon damage as in 0.8.0 (rifle 571, missiles 3 x 100, laser 855, grenade 167, cannon 200, melee 1000); old saves
  (`smoke`, a 0.5.0 world -> 0.9.0) 3/3 and 20/20. Frame time (`perf`, alternating, no robots in that scene): 0.8.0 1.08 / 1.15
  and 1.15 / 1.25 ms, 0.9.0 1.35 / 1.22 and 1.17 / 1.15 ms: within the spread. Tower models unchanged (no gap/overlap rerun).
- Package `dist/MechDefense-0.9.0.scmod`: 49,104,914 bytes, 206 entries, SHA-256 682281de8f2fea96f467603f16fc5397ba1615e1212a41e6f7d20f5ca5a450cb;
  no 终末地 / 毁灭枪手 / 原作 in names or data. Against 0.8.0: new Assets/Imported/RD05, RD06, RD07, RD17 (models, unit.json,
  clips.bin) and Assets/Parts; changed MechDefense.dll, MechDefense.xdb (10 robot templates added; regenerating drifted
  turret_1's radius 2.98 -> 2.97 again, restored), MechDefense.cr, modinfo.json. 0.8.0 stays the rollback.
- Not verified: the nightly raid trigger itself (Raid() is tested), natural spawn rates in a survival world at night,
  Android phones, a long session in a real world, robots against other mods' creatures, multiplayer.

## Previous delivery: 0.8.0 (PR #4, merged 2026-10-17 on the user's approval, tag v0.8.0)

- User (2026-10-17): after accepting the Mercury asset (`E:/projects/dguns-mech-model/v8_assets`, side-step clipping kept as in
  the original): “进模组现在就可以了”; chose mouse-turn + WASD (Shift dash, Space jump), Mercury's own weapons (rifle, missiles,
  cannon that unfolds and folds, F melee), creative inventory only (round 5: a survival recipe as well). Task brief: docs/tasks/2026-10-17-mercury-mech.md; design
  and numbers: docs/MECH.md.
- Assets (`tools/build_mech_assets.py`, job claude-pb-assets-1): 37 joints, 34 rigid parts, 25 clips (752 samples), texture
  embedded (JPEG, engine mipmaps, linear). Re-posed from its own output with the engine's bone composition against Blender:
  p99 0.001-0.002 mm on 7 poses; max 16-48 mm at the 3 head-sensor seam triangles the skinned original stretches.
- In game (`run_isolated.py lab --mode mech`, isolated runtime of the user's build 1.9.3.1 with Neorxna; two runs on the
  final package, same numbers): the item “水星机甲 · 部署” is in the creative list and deploys Mercury (34 meshes, 38 bones,
  no missing bone, no texture override, two-sided); real key presses through the game's input (Keyboard.ProcessKeyDown inside
  ComponentInput.Update): R boards from 2.4 m, W walks 5.4 m in 1.2 s, Shift+W dashes, B cycles rifle > missiles > cannon
  (unfolded) > rifle, F strikes, V switches third/first person, E opens and closes the inventory while the mech stays, R
  leaves; boarding, third- and first-person cameras, HUD; walk forward 10.1 m in
  2 s (walk clip at 1.56× so the feet keep pace), strafe left 5.4 m in 1.5 s to the mech's left (walk_left), dash 10.1 m in
  1 s, jump 2.80 m (jump → fall → landed); standing, the body turns until the aim is within ~51° and the turret takes the
  rest; on 2e4-resilience wolves: rifle 8 impacts 206 damage, missiles 4/4 = 280, cannon (stowed → unfolding → ready →
  firing → ready, then folding → stowed on switching) 360, melee 300; leaving: rider off, first person, HUD gone; destroyed:
  explosion, death clip, removed after the wreck time. No draw or model errors. Screenshots:
  `local/claude-work/mech-review/lab-screenshots` (mc_* clips, mt_*
  turntable, mp_* piloting, md_death; contact sheets one folder up).
- Old saves (`smoke`, a 0.5.0 world → 0.8.0, 3/3 and 20/20): the sample enemy is gone; the 0.5.0 sample mech is Mercury with
  its model and the player still seated, who gets out unhurt into first person; the old mech item reads “水星机甲 · 部署”
  and deploys a second Mercury, the enemy item stays “已移除的旧单位” and does not deploy; towers still deploy and shoot;
  after saving and reloading: towers and both mechs.
- Regression: lab fire 0.8.0 vs 0.7.0 — every tower fires the same number of times; which dummies register a hit in the
  2 s window varies between runs of the same version (0.7.0 run: prologue cannon false, 0.8.0 run: gun tower 1 false).
  Frame time (`perf`, three alternating pairs): hotbar only 0.7.0 2.91 / 2.88 / 2.92 ms, 0.8.0 2.05 / 3.05 / 2.00 ms;
  30 towers 0.7.0 1.86 / 3.34 / 3.30 ms, 0.8.0 3.47 / 3.50 / 1.87 ms — no difference beyond today's spread (1.8-3.5 ms).
  Tower models unchanged (no gap/overlap rerun).
- Package `dist/MechDefense-0.8.0.scmod`: 31,257,637 bytes, 178 entries (+19 original sound effects), SHA-256 2220167d…d450db50c (job
  claude-pb-yuanzuo-fix-1; shown texts without 原作, smoke old/new pass). Against 0.7.0: added Assets/Imported/Mercury/{model.glb, mech.json, clips.bin, icon.bin, icon.png};
  changed MechDefense.dll, MechDefense.xdb (the MDMech template gains its model component, armour 3000), MechDefense.cr
  (Mercury's recipe), modinfo.json, the manifest. No 终末地 / 毁灭枪手 / 机兵 in any entry.
- Fix after the user's first try (2026-10-17, “上机甲是哪个键？E不行”): E is the game's inventory key and the GUI opened the
  inventory before the mech saw the key. Boarding and leaving now use the mount key (R); input is read after ComponentInput.
- User test 2 (2026-10-17): big steps but little ground, no steering in a jump; the cannon unfolded on its own; first person
  shaky, own weapon hard to see, flickering; HUD speed flickering. Now: the original speeds with the clips at their own rate (lab: 17.9 m
  walked in 2 s, 11.5 m strafed in 1.5 s, jump 3.1 m high and 11 m forward in the air); the cannon unfolds on the trigger
  (Stowed > Deploying > Firing > Ready > Retracting > Stowed after 3 s idle); first person fixed to the body (eye height constant
  while walking) drawing only the rifle arm and the cannon, the arms aim the rifle at the crosshair; HUD speed smoothed.
  Weapons and sounds of the original: researched from its native code and tables (docs/MERCURY_ORIGINAL.md).
- User test 3 (2026-10-17): the cannon folded and snapped open again after every shot; the dash looked crouched; the original
  has an upward boost and a hover with back flames - make Shift hover. Also from test 2: laser, grenade, lock-on missiles,
  original sounds. Now (lab mech on the final package, numbers in docs/MECH.md): one world unit of the original = 2.67 m, so
  walk 12.2 m in 2 s (5.7 m/s); Shift hovers 0.79 m up at 8.0 m/s on the gliding boost clips with back-thruster flames,
  energy -15,450 in 1 s of hover, +9,000 in 1 s of rest; jump 3.1 m with a burst; the cannon: one press Stowed > Deploying >
  Ready > Retracting > Stowed (1 shot, no clip 55), held 3 shots at 0.7 / 2.4 / 4.0 s and folds on release; on 2e4-resilience
  wolves: rifle 151 (5 hits), missiles 3 locks in one sweep and 3 homing hits of 30, laser 251 over its 1 s beam, grenade 50
  (blast), cannon 60, jump attack (clip 45) lunges 13.8 m and hits 300; real keys: B cycles rifle > missiles > cannon >
  laser > grenade > rifle. Smoke 0.5.0 > 0.8.0 and lab fire pass again.
- User test 4 (2026-10-17): hover should look like the jump (legs kicked back), not crouched; jump / hover sound the same as the
  missile; locked missiles sometimes missed; the laser smeared in steps; hover did not feel faster. Now: hover holds the jump
  clip at 1.0 s (steady airborne pose) at 13 m/s (lab 12.96 m/s, 0.79 m up); hover hums with boss_hover, the jump keeps
  shot_missile (the only take-off sound in the original's code; SPα has the same 32 effects); locked missiles pass through
  terrain and burst on their target (lab: 3/3 on standing wolves, 3/3 on wolves running sideways at 6 m/s); the laser is one
  held beam with a fading ribbon. Smoke and lab fire pass.
- User test 5 (2026-10-17): “shift按住应该只有一段音效”; “机甲表面不用加那么多噪点，就用原版材质清晰化那个版本（…如果能再清晰就跟好了）”;
  “数值设置和生存模式材料设置参考模组 D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods”. Now: one boss_hover per Shift press
  (lab, real keys: 1 in a 2.5 s hold, 2 after a second press; counted on SubsystemAudio.PlaySound); the texture is the
  clean Real-ESRGAN x4plus 2048 upscale of the original (V7) with a light unsharp mask, embedded as PNG (model.glb 4.2 MB),
  no grain or wear; numbers from the installed mods (decompiled code and data, docs/MECH.md): damage the original's ÷ 10
  (lab on 2e4 wolves: missiles 100 each 3/3 also on runners, cannon 200, melee 1000, laser 855 per beam, rifle 571 in 6
  impacts), armour 3000; a crafting-table recipe from 暴雨 / 高能射线 / 序章 / 2 高爆榴弹 towers + 2 iron blocks + diamond
  block + semiconductor block (lab recipes: 20 recipes, Mercury's grid resolves to it); repair and pickaxe take-down as for
  towers, which before crashed on the mech (lab: iron ingot 50 % → 56.7 %, 3 hits → item “耐久 57%”, redeployed at 57 %).
  Smoke 0.5.0 > 0.8.0 (old, new) and lab fire pass (job claude-pb-round5-bal-1).
- Not verified: the user's real game with all its mods; mouse look and the left button with a real mouse (keys were pressed
  for real in the lab, the mouse was not); touch or gamepad play; the mech among the ghoul / Wonderful Era mods; the
  recipe with RecipaediaEX (the user's crafting table; the lab runs the vanilla one); the mod numbers in play.
  The faint green-circle overlay in some lab screenshots is screen-space (also in first person with no mech in view) and was
  not identified.

## Previous delivery: 0.7.0 (PR #3, merged 2026-10-13 on the user's approval, tag v0.7.0)

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

## Previous delivery: 0.6.0 (Destroy Gunners units removed, no 终末地 in shown names)

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
