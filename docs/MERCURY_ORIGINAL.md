<!-- Research notes for 0.8.0 (2026-10-17): read from the original game's native code and tables by a research agent on a
private copy of the analysis database; nothing extracted from the game is in the repository. Used by docs/MECH.md. -->

# Destroy Gunners Σ 1.10: weapons, MERCURY, lock-on and sounds (source data)

Scope: original data and native code of `jp.shade.DGuns5` 1.10, the arm64 `libapp_lib.so`
(SHA-256 83e8af7d…0ae6, the same hash as in `docs/dguns-research-receipt.json`). Nothing in the repo or on
Windows was modified. Scratch output is in `E:/projects/SurvivalcraftMechDefense/local/claude-work/research/`
(IDA copy `ida/dg.i64`, decompiles `ida/dec1..4`, `se_analysis.json`).
Labels: **[code]** = read from decompiled or disassembled native code; **[data]** = read from a table;
**[inf]** = inferred, not verified.

## 0. Units (needed to read every number below)

- **Time** [code]: the global step `n56` is 32 per 1/60-s frame (renderer `0x3cb20`: 3-frame sum of
  0.1-ms deltas / 500 × 32; `shdInit1` sets 32; `appLoop1` adds n56/1920 to a seconds counter).
  So **1920 units = 1 s**. `vsync_rate` = n56/32 = 1.0 per 60-fps frame. "Per frame" below means per 1/60 s.
  Animation ticks are 1/60 s. The SE retrigger interval is counted in frames.
- **Space** [data+code]: MERCURY's mesh is 1.12 model units tall. `pw_aloc` scales every robot by
  KINDDT int16 +4 = 150 % (bullets 100 %), so **MERCURY is about 1.68 world units tall**.
  [inf] If SC MERCURY is 4.48 m tall (the V8 asset is ×4 model), then 1 world unit ≈ 2.67 m.
- **Damage** [code `set_dmg` 0x8e828]: `dmg = ceil((pilotAttack + bonus) × weaponPower / 100)`.
  For channels 0/1/2 the result is then reduced by the target's resist % for that channel.
  Channels: 0 = impact (IMP), 1 = explosion (EXP), 2 = electric (ELC/"LSR").
  Channels 11/12 are "DEV": they ignore resist and multiply by ×70 (11, cannon) or ×5 (12, laser)
  against targets that have byte +28 bit 7 set. Earlier research identified that bit as the DevilMech flag; it was not re-verified here. The UI text says ×10 and ×5.
  Weapon power comes from the equipped part (`partsdt +14`) and is written into the attack record at fire time.

## 1. Weapons

### 1a. Six categories: behaviour is per category, the part only sets power [code]

| Cat (part +2) | Fired by (state fn) | Projectile / hit test | Rate / timing | Start ammo / pickup | Other |
|---|---|---|---|---|---|
| 1 Rifle | `sub_CAC2C(…,0)` → `make_hou(type 10)` while touch held | KN_BULET sec0, scale 6; speed **1.0 u/frame** (60 u/s), gravity 1e-5 (straight), capsule r **0.2**; flies until ground (life 230400 = 120 s) | **480 = 0.25 s** (4/s). NEMESIS (kind 2): 3-round burst 256, 256, 832 → 3 shots / 0.70 s | 100 / +50 | atk #0: IMP, knock 0.045, stun 25 f |
| 2 Missile | hold = lock, release = salvo (`prg_PLY` + `sub_C8E4C`/`sub_CAFE4` → `make_missil`) | KN_MISSILE, scale 1.5; homing (§3); capsule r **0.25**, direct hit only | one missile per lock, **576 = 0.30 s** apart | 50 / +25 | atk #1: EXP, knock 0.18, stun 12 f |
| 3 Cannon | hold → clip 54 unfold (state 6, `sub_CA400`), then clip 55 fire (state 7, `sub_CA508`) → `make_cannon(type 11)` at key event 0x8000 | KN_BULET sec1, scale 6; speed **2.0 u/frame** (120 u/s), capsule r **0.8**, screen quake. Direct hit, then **splash r 5.0** (6.0 with CN-EXP Lv2), also on ground impact | clip 54 = 46 ticks + clip 55 = 52 ticks at 1× ≈ **1.63 s per shot** [inf from clip lengths]. Rate byte 32 = 1×; ANTARES/SCORPIO & VEROSSA/AURIS 1.5×/1.625×; CN-SPD 2×; Devil Killer 3× | 10 / +5 | **640 EP per shot**. Direct atk #2 (EXP, knock 0.3). Splash atk #3 (EXP, radial knock 0.3−0.25·d/r). Splash power = part +16 ("B"), or full power with CN-EXP |
| 4 Laser | touch → `beam_make` from the rifle muzzle; `beam_set` re-aims it every frame ("mow") | Beam tip grows 1 u/frame up to **22 u** (AVALON: 70 u, DevilMech Killer: 100 u); lasts **60 f = 1.0 s**. While held it stays anchored at the muzzle; after release the segment (≤ 22 u) keeps flying forward at 1 u/frame until its life ends; hit capsule r **0.5** (visual width 0.03, DK 0.1; ARGB 0x60F02020 / 0x70308010) | cooldown **1920 = 1 s** (AVALON 2880, DK 3840). Damage tick at most every 320 = 1/6 s → ≤6 ticks per beam [inf] | 30 / +15 (1 per beam) | atk #4: ELC, knock 0.1. DK uses atk #7 (DEV) |
| 5 Grenade | released touch → `make_bomb` | KN_BOMB, scale 1.5; arc with apex **1.5 u**, gravity ×0.2; lands at **75 % of the aim distance, max 15 u**; its body flag 0x10 is cleared after 641 units (0.33 s) [inf: arming delay]; explodes on any contact: **splash r 5.0**, knock-down within 3.4 u | cooldown **2560 = 1.33 s** | 5 / +3 | atk #5: EXP, radial knock 0.3−0.05·d |
| 6 Melee | always weapon slot 0 (Fighter Knuckle, power **1000 %**); clip 50 ground combo (`sub_CA15C`) or clip 45 lock-on dash jump (`sub_CA834`) | animation attack boxes (`shdAtkGetBox`); each target is hit once per swing (8-entry list) | clip 50 = 52 ticks (0.87 s) loop | ∞ (−1) | atk #6: IMP, knock 0.3, stun 20 f. EP: 640/frame during lunge keys (ev 0x100); dash-jump 960/frame airborne |

Sources: `wep_bulet_ini` @0x133084 = [0,100,50,10,30,5,−1] and `wep_bulet_add` @0x1330a0 = [0,50,25,5,15,3,0] per category [data].
MEGA MAGAZINE multiplies start ammo by 150–500 %.
An ammo pickup refills the equipped weapon whose ammo is lowest relative to its start value (80 % bias to the current weapon) [code `prg_ITM`].
An enemy-owned `make_hou` bullet gets speed and gravity ×0.3.
Other bullet types (enemy use, [code `make_hou`]): type 0 v1.0 r0.07, type 1 v2.0 r0.1, type 2 v0.8 r0.01, type 4 v1.5 r0.07 (KN_BULET2), type 6 v2.0 r0.07 scale 10 (KN_BULET3).

### 1b. Individual weapon parts [data `partsdt`, dat.bin/0001 64-bit table @104, 48-byte records]

Field meanings: +14 power % (P), +16 cannon blast % (B), +7 slot cost (S), +18 int16 / +20 int32 = two shop prices.
For the prices, +20 is checked against the in-game money (`buy_ack_open`); +18 is probably premium coin [inf].

| Category | id: name P (S) |
|---|---|
| Rifle | 2 NO-100 P100 (1) · 3 NO-150 150 (2) · 4 SU-200 200 (3) · 5 HY-300 300 (3) · 6 PR-500 500 (4) · 7 XR-1000 1000 (4) · 8 XR-1200 1200 (4) · 9 XR-1500 1500 (4) · 10/11 "Lv9/Lv10" are empty placeholders (P0) |
| Missile | 12 NO-100 100 (1) · 13 NO-150 150 · 14 SU-200 200 · 15 HY-300 300 · 16 PR-500 500 · 17 XM-1000 1000 · 18 XM-1200 1200 · 19 XM-1500 1500 (same S pattern) |
| Cannon (P/B) | 22 NO-200 200/150 (1) · 23 NO-300 300/200 · 24 SU-500 500/400 · 25 HY-900 900/600 · 26 PR-1500 1500/1000 · 27 XC-2800 2800/1000 · 28 XC-3800 3800/1000 · 29 XC-5000 5000/1000 · 31 Devil Killer Cannons 250 (3, DEV, 3× rate) |
| Laser | 32 NO-150 150 (1) · 33 NO-220 220 · 34 SU-300 300 · 35 HY-450 450 · 36 PR-800 800 · 37 XL-1500 1500 · 38 XL-2200 2200 · 39 XL-3000 3000 · 41 DevilMech Killer 250 (3, DEV) |
| Grenade | 42 NO-200 200 (1) · 43 NO-400 400 · 44 SU-600 600 · 45 HY-1000 1000 · 46 PR-2000 2000 · 47 XG-3000 3000 · 48 XG-4000 4000 · 49 XG-5000 5000 |
| Melee | 1 Fighter Knuckle 1000 (built in) |

Prices: tier 2/3/4 cost 4000/8000/15000 (+20); the coin field is 50…250. Laser NO-150 and Grenade NO-200 cost 2000.
Part help texts (dat.bin/0001 @120) confirm the categories:
Rifle "IMP, large clip, rapid-fire"; Missile "EXP, follows target, takes time"; Cannon "EXP, splash near target";
Laser "ELC, straight line, can mow"; Grenade "EXP, huge explosion on landing, short range"; Knuckle "IMP, unlimited ammo".
**WIDE LASERS** does not exist in Σ. It is part 16 of the separate SPα 2.75 game ("ELE-ATK500"), which was not analysed here.

Weapon-modifying parts [code `cal_ply_stat` 0x81a04 + help texts]:
- HIGH SCOPE LOCK +1..+3 adds to the lock count (cap 12).
- CN-EXP Lv1/Lv2: the splash uses full power; Lv2 also sets r 6.
- CN-SPD: cannon clip rate 2×.
- Knuckle Fighter: auto-melee when the sighted target is within 4.5 u.
- CRIT Lv1–4 (20/30/40/50): a value/1000 chance to add the attack once more (double base).
- MEGA MAGAZINE: ammo %.
- Barriers 8/9/10/11: + resist % (cap 80).
- ECM 1/3/5/20: an enemy missile drops its lock with chance 8·v/800 per 0.5 s.
- Multi Booster: move ×1.1/×1.2.
- EP-RCVRY: regen ×(600+300·v)/600.

## 2. MERCURY (水星, player body 0, kind 14 KN_RD10, model 214 "rd10", section 0)

| Field (plbody record, dat.bin/0001 @168, 56 bytes, file offset 24888) | Value |
|---|---|
| HP (+12) / attack (+8) / EP (+16) | **6000 / 1000 / 100000** |
| Slots (+3) = equipment-cost budget | **12** |
| missileLockCount (+4) | **3** |
| Resist % IMP/EXP/ELC (+5..7) | **0 / 0 / 0** ("no armor against any form of attack") |
| Unlock price +24 / coin +28 / paint +32 | 0 / 0 / 50; +34..38 = 10, 2, 12 (unknown) |
| Body-specific flags (`set_player`) | none. GLADIUS (same kind, section 5) gets "no knock-back"; NEMESIS burst rifle; kinds 27/32 fast cannon; AVALON long laser |
| Level growth [code] | per level of the global `n101` [inf: player rank], above 1, cap 101: +50 HP, +10 attack, +1000 EP, resist +(min(L−1,10)+L−1)/3. Separate purchased upgrades exist (savs 1204–1208) |
| EP economy [code] | regen 600/128 per n56 = **9000 EP/s**. Any spend pauses regen ≥ 320 (1/6 s). EP 0 → 5760 (3 s) lock-out. Boost/hover toggle ×1.4 speed costs 256 EP/frame (15360/s ≈ 6.5 s for MERCURY) |

**Default loadout** [code `save_s_init` 0x894f4]: each of the 64 body save slots gets equip[0..2] = **2 Rifles NO-100,
12 Missile NO-100, 22 Cannons NO-200**. Those three parts and part 1 are marked owned (−1 = permanent).
The total cost is 3 of MERCURY's 12. Fighter Knuckle is always weapon #0 (`cal_ply_stat`). Equipping rules
(`chk_can_equip`): one part per category, Σ slot cost ≤ slots. Laser and grenade must be bought.

Mercury damage per hit at Lv1 against 0 % resist [code formula]:
- rifle 1000 (4000/s);
- missile 1000 each (3-lock salvo 3000);
- cannon 2000 direct, or 1500 splash for others in r 5. [inf] The direct-hit target is excluded from the splash (pending-damage check);
- laser NO-150 1500 per tick;
- grenade NO-200 2000 splash;
- punch 10000.

**Which joint fires what** [data: per-joint attribute words at PSCf +80 of grp.bin/0214, read by `shdPartsAtbitToNo`]:

| Attr bit | Joint (index, name) | Used by |
|---|---|---|
| 0x0400 + 0x2000 | 14 ☆右腕兵装懸架システム05 (right-arm weapon mount, tip) | rifle muzzle **and** laser emitter |
| 0x0800 | 18 ミサイルポッド (missile pod) | missile launch point |
| 0x1000 | 17 キャノン03 (back cannon, 3rd segment) | cannon muzzle (cannon chain 15-16-17 unfolds in clip 54) |
| 0x4000 | 15 キャノン01 (cannon base) | grenade release point |
| 0x100000 / 0x200000 | 33 左バックユニット02 / 35 右バックユニット02 | two KN_JET0 objects (scale 0.5, pitch −90°). Shown while rising or spending EP [inf: booster flames] |

The left arm chain (5–9) has no weapon attribute. Clips: 45 lock-on dash punch, 50 melee combo
(swing SE at key 0x4000, EP drain at keys 0x100), 54 cannon unfold (46 ticks), 55 cannon fire
(0x8000 fire event on key 0 at tick 2, then recoil root motion, 52 ticks). Parsed from the 0214 clip directory.

## 3. Lock-on and homing [code `get_sight_trg` 0xd23b0, `prg_PLY` 0xc7c24, `make_missil` 0xdb954, `prg_MISSILE` 0xdbb30]

**Aiming.** The game aims with a screen ray from the camera through the touch point. Targets are hit capsules near the ray.
- Acceptance: (distance to ray − radius)/camera distance < `t1`, **or** < `t2` and the target is projected inside a box of **±32.3 × ±24 px** around the finger (`xmmword_10EB20`).
- Modes (t1/t2): aim 0.03/0.53, **lock 0.001/0.501**, missile sight 0.05/0.55, cannon 0.1/0.6, melee 0.2/0.7.
- The candidate nearest the camera wins.

**Locking.**
- **Range:** the target must be **0.3–50 world units** from the player, and the aim point is clamped to 50 u.
- **Count:** MERCURY 3, + HIGH SCOPE LOCK, max 12.
- **No lock time:** while the finger is held with missiles selected, every new target swept under the finger is added at once and plays `lock_on` (SE 7).
- **Line of sight:** targets ≥ 5 u away need a terrain check (≤ 10 samples between chest heights; the ground must stay ≤ line + 2). Nearer targets lock with no check.
- Dead targets are pruned from the list while the finger is held.

**Firing.** Releasing the finger with ≥ 1 lock fires one missile per lock, 0.30 s apart. Each missile flies to its own target.
[code] If the list is empty when the salvo starts, one missile goes to the current sight target / 200 u ahead.

**Missile flight (player):**
- **Speed:** starts at 0.05 u/frame and accelerates by 0.004 per frame up to **0.20 u/frame (12 u/s)**. A heading error over 15° slows it, down to a floor of 20 % of max.
- **Launch spread:** for a random 640–1280 units (0.33–0.67 s) the missile does not home. It drifts by salvo position. Missile 1 goes straight. Missiles 2/3/4 get yaw 0.785/−0.785/0.349 and pitch 0.087/0.175/0.524 (×0.01 rad per frame; `0x10ea68/74`). Missile 5 and later go straight. Initial pitch is randomised −27°…−2°, yaw ±8°.
- **Homing turn rate:** **1°/frame (60°/s)** in yaw and pitch while the horizontal distance to the target is ≥ 5 u. Inside 5 u: pitch 4°/frame, yaw 10°/frame.
- **Lead:** aims at target + target velocity × time-to-go (≤ 120 frames).
- **Wobble:** ±8° every 192–384 units.
- **Lifetime:** 15360–23040 units (8–12 s), refilled by 3 s while the target lives. With no target it explodes when the time runs out.
- **Hit:** capsule r 0.25 → EXP damage. Smoke trail effect.

**Enemy missiles** (owner flag 0x80):
- speed 0.01 → 0.13 u/frame;
- life = distance/max-speed·32 + 2240;
- they drop the target when it is < 10 u away with > 40° yaw error;
- they are subject to the player's ECM.

## 4. Sound effects (se.bin, 32 Ogg Vorbis, mono)

**ID → file → name mapping is code-evidenced, not guessed:**
- `se2str` @0x1333a8 in the .so holds the names for ids 0–32 (0 = "-").
- `se1_tbl` (dat.bin/0001 @1656, 33×4 bytes: idx, volume, min retrigger frames) has idx = 0..32 in order.
- Java `SeMng_SeArc` loads archive entry *i* (from 1) as SeNo *i*.
- So **file 00NN.bin = SE id NN**.

Columns: duration (s), sample rate, peak/RMS dBFS, t(peak)/t(−20 dB) in s, the two strongest bands, then the code callers.
Volume is 128 except lock_on 90. The retrigger value (frames) is in brackets.

| ID | Name [retrig] | Dur | SR | Pk/RMS | t pk/−20 | Bands | Played by [code] |
|---|---|---|---|---|---|---|---|
| 01 | select [10] | 0.04 | 32k | −0.4/−15.6 | 0.00/0.02 | >6k 51 % | menus |
| 02 | ok [10] | 0.41 | 32k | −3.4/−20.7 | 0.08/0.25 | 1–6k 86 % | menus (**not a weapon**: MechDefense 0.5.0 used this as its fire sound) |
| 03 | cancel [10] | 0.12 | 32k | −2.5/−12.3 | 0.02/0.11 | 2.5–6k 50 % | menus |
| 04 | error [10] | 0.21 | 32k | −12/−26 | 0.12/0.14 | >6k 56 % | menus |
| 05 | window_open [10] | 0.22 | 32k | −8.5/−20 | 0.15/0.21 | >6k 93 % | menus |
| 06 | window_close [10] | 0.24 | 32k | −9.5/−24 | 0.22/0.23 | >6k 91 % | menus |
| 07 | lock_on [8] | 0.13 | 44.1k | −3.7/−17 | 0.10/0.12 | >6k 71 % | each new missile lock |
| 08 | shot_gun [10] | 0.97 | 32k | −0.6/−15.5 | 0.01/0.64 | <150 Hz 45 % | rifle and bullet types without section 1 (`make_hou`, at muzzle) |
| 09 | shot_missile [20] | 2.91 | 32k | +0.6/−14.9 | 0.55/1.79 | 1–6k 54 % | every missile's first frame; **also jump and dash-punch take-off** |
| 10 | shot_laser [20] | 0.69 | 32k | −2.7/−15.5 | 0.04/0.49 | >6k 86 % | `beam_make` |
| 11 | shot_cannon [20] | 1.88 | 32k | +0.1/−12.9 | 0.22/1.33 | <150 Hz 74 % | cannon (bullet section 1) |
| 12 | empty [20] | 0.34 | 32k | −1.7/−14.3 | 0.14/0.24 | 1–2.5k 40 % | fire with 0 ammo |
| 13 | hit_gun [10] | 0.90 | 32k | −3.3/−17.8 | 0.02/0.59 | 150 Hz–1k 74 % | bullet/cannon direct hit, **each laser tick** |
| 14 | hit_missile [10] | 2.18 | 32k | −3.0/−13.8 | 0.02/1.60 | <150 Hz 60 % | missile hit |
| 15 | hit_rock [10] | 0.43 | 32k | 0.0/−19.2 | 0.01/0.18 | 2.5k+ 57 % | bullet/missile hits ground |
| 16 | punch_swing [30] | 0.77 | 32k | −3.4/−17.4 | 0.18/0.45 | broad | melee swing key, gacha |
| 17 | hit_punch [30] | 0.88 | 32k | −2.8/−14.5 | 0.20/0.64 | <150 Hz 40 % | melee hit (player and bosses) |
| 18 | enemy_destruction [20] | 1.32 | 32k | +0.3/−15.0 | 0.15/0.71 | <400 Hz 56 % | enemy/object death |
| 19 | player_damage [10] | 1.22 | 32k | −3.5/−16.1 | 0.04/0.77 | >6k 66 % | player hit |
| 20 | player_destruction [30] | 2.56 | 32k | −0.1/−13.9 | 0.21/1.97 | <150 Hz 56 % | player death |
| 21 | get_item [4] | 0.81 | 32k | −4.5/−21.5 | 0.13/0.46 | 400–1k 73 % | item pickup |
| 22 | boss_walk [20] | 1.36 | 32k | −1.0/−15.9 | 0.33/1.04 | <150 Hz 74 % | no constant call found (event scripts only) |
| 23 | throw_bomb [20] | 0.88 | 32k | −2.2/−18.2 | 0.23/0.52 | 1–6k 73 % | grenade throw |
| 24 | bomb_explosion [20] | 1.48 | 32k | −1.6/−16.6 | 0.10/1.18 | <150 Hz 60 % | **not called by `prg_BOMB`** (only reachable from event scripts) |
| 25–30 | change_to_gun/missile/cannon/punch/bomb/laser [10] | 0.11/0.33/0.40/0.26/0.35/0.55 | 32k | −4…−8 | 0.00/≈dur | 2.5k+ ≈100 % | weapon switch (`wep_chg_se` = [0,25,26,27,30,29,28] by category) |
| 31 | boss_hover [50] | 2.30 | 32k | −0.5/−14.6 | 0.69/1.68 | <150 Hz 57 % | RD08 / RD21 bosses |
| 32 | buy_item [20] | 1.52 | 32k | −4.3/−23.1 | 0.06/0.74 | 1–2.5k 67 % | shop |

[inf] For the MERCURY port:
- rifle 08 (+13 on hit);
- missile 07 → 09 → 14;
- cannon 11 (+13 on a direct hit; 24 is the closest splash-boom asset);
- laser 10 (+13 per tick);
- grenade 23 → 24 (Σ itself plays nothing on the grenade blast);
- melee 16/17;
- jump/boost 09;
- out of ammo 12;
- switches 25–30.

The sounds were characterised by numbers only, not by listening. The change_to_* clips are short, broadband and above 2.5 kHz; whether they are voice call-outs or beeps is unverified.

## 5. Projectile and effect models [data `units.json`, KINDDT]

All of these are in `local/dguns/unit-library/models/<id>/` (model.json) and `views/<key>/model.obj` (+GLB).

| Kind | Program | Model (sections, tris, half-extent) | Used for |
|---|---|---|---|
| 40 KN_BULET | prg_BULET | 0099: s0 40 tris (0.03,0.12,0.03); s1 48 (0.10,0.35,0.10); s2 28; texture 104 | **rifle = default section (presumably s0), cannon = s1** (×6 scale); enemy types 0/2 = s2 |
| 41 KN_BULET2 / 42 KN_BULET3 | prg_BULET | 0100 / 0101 | enemy bullet types 4 / 6 |
| 43 KN_MISSILE | prg_MISSILE | 0102: 64 tris (0.01,0.085,0.01) | all missiles, ×1.5 |
| 44 KN_BOMB | prg_BOMB | 0103: 320 tris | grenades, ×1.5 (enemy ×2.0) |
| 45 KN_JET0 | none | 0038: s0 98 tris, s1 8; texture 37 | MERCURY back-unit jets |

There is no laser model: the beam is drawn procedurally (`beam_disp`/`sub_DD150`).
Particle effects are named in dat.bin/0002 `pefc2str`:
- 0 EF_HIT_HOU (bullet hit)
- 1/2 EF_HIT_MSSIL(2)
- 3 EF_HIT_PUNCH
- 5 EF_HIT_GND
- 6 EF_PL_DAMAGE
- 8 **EF_FIRE_HOU** (muzzle flash)
- 9/10 EF_HIT_CANNON(2)
- 11 EF_HIT_BOMB
- 12 EF_HIT_LASER
- 13–18 event effects

They point to PTC containers in grp.bin, which are kept raw and not converted.

## 6. Evidence and gaps

- Verified from code: the time base, every interval and speed above, damage/resist, lock acquisition, homing, the default loadout, the joint attribute bits, the SE names/table and their callers.
- The Java mapping SE id = archive index was read from smali at `E:/projects/apk-game-workbench/projects/destroy-gunners-s/decompiled/smali_classes2/jp/shade/DGuns5/SeMng_SeArc.smali`.
- **Not in the data:** a per-weapon rate, ammo or speed. These are category constants in code; the parts only change power (and cannon blast).
- **Inferred:**
  - the cannon cycle time and its "hold to unfold, fire after unfold and release" flow (from state code + clip lengths; the meaning of the flag `qword_1A0810` = touch held/tap is inferred);
  - laser ticks per beam (≤ 6);
  - which currency each price field is;
  - KN_JET0 as booster flame;
  - the direct-hit target being excluded from the cannon splash;
  - the attack-record field +8 being a reaction time in frames.
- The movement speed (`ply_move`: +0.1 u per frame of stick input added to velocity, damping not traced) and the world-unit → metre conversion are **not** verified.
- Not decoded: KINDDT/PLYBODY bytes +34..+38, part bytes 8–13 (shop/gacha gating), effect (PTC) contents, positional-SE attenuation, enemy AI fire intervals.
- SPα 2.75 (WIDE LASERS etc.) was not analysed. Its field layout differs, so Σ offsets must not be reused.
