<!-- Research notes for the NEMESIS / REGULUS mechs (2026-10-21): read from the original game's native code, tables and model
containers on private copies; nothing extracted from the game is in the repository. Companion of docs/MERCURY_ORIGINAL.md. -->

# Destroy Gunners Σ 1.10: NEMESIS and REGULUS (source data)

Scope: the player bodies 1 (NEMESIS, 复仇女神) and 2 (REGULUS, 轩辕十四) of `jp.shade.DGuns5` 1.10, compared with body 0
(MERCURY), from the arm64 `libapp_lib.so` (SHA-256 83e8af7d…0ae6, the same file as MERCURY_ORIGINAL.md), the decoded tables
in `local/dguns/unit-library/tables.json` and the PSCf model containers `raw/grp.bin/0208.bin` (rd04), `0216.bin` (rd12),
`0214.bin` (rd10, the control). Units (time 1920 = 1 s, 1 tick = 1/60 s, damage formula, world unit ≈ 2.67 m at our ×4
scale) are as in MERCURY_ORIGINAL.md §0.
Labels: **[code]** read from decompiled native code; **[data]** read from a table or container; **[inf]** inferred, not verified.
Work files (not in the repository): `E:/projects/dguns-mech-model/v9_heroes/` (`work/ida/` decompiles from a private copy of
the motion-research IDA database, `motion/roles_*.json`, `motion/feet_*.json`, `review/check_*.json`, `tools/`).

## 1. Body records [data: plbody, dat.bin/0001 @168, 56 bytes per body; tables.json `playerBodies`]

| Field | MERCURY (0) | NEMESIS (1) | REGULUS (2) |
|---|---|---|---|
| kind (+0) | 14 KN_RD10 | 2 KN_RD04 | 16 KN_RD12 |
| model (via kind) / section (+2) | 214 rd10 / 0 | 208 rd04 / 0 | 216 rd12 / 0 |
| HP (+12) / attack (+8) / EP (+16) | 6000 / 1000 / 100000 | **10000 / 1500 / 130000** | **9000 / 1300 / 160000** |
| slots (+3) = equipment-cost budget | 12 | 16 | 20 |
| missile locks (+4) | 3 | 3 | **2** |
| resist % impact / explosion / electric (+5..7) | 0 / 0 / 0 | **2 / 5 / 0** | **3 / 3 / 3** |
| +24 / +28 (unlock price, coin [inf]) | 0 / 0 | 200000 / 500 | −5 / 0 ([inf] a special unlock condition, not a price) |
| +32..38 | 50, 10, 2, 12 | 50, 11, 2, 12 | 50, 12, 2, 12 |
| texture (skin 0) | 148 | 139 | 156 |

Raw records: NEMESIS `0200001003020500dc05000010270000d0fb0100…400d0300f4010000 32000b0002000c00…`, REGULUS
`100000140203030314050000282300000071020000000000fbffffff 00000000 32000c0002000c00…`.

Descriptions [data, dat.bin text]:
- NEMESIS: "the prototype of the mass-production CANCER; not much better, but strong against explosions and a few more slots;
  the A in its code stands for Assault, and its rifle has a built-in 3-round burst mechanism".
- REGULUS: "a new-concept body built from salvaged data; its form differs greatly from earlier bodies; advanced piloting is
  needed to use it well".

Stats grow with the player level for every body alike (`cal_ply_stat` 0x81a04: +50 HP, +10 attack, +1000 EP per level) [code].

## 2. Body-specific code [code `set_player` 0xc8734]

`set_player` clears four flags and sets them by the body's kind:

| kind | flag | read by | effect |
|---|---|---|---|
| **2 (NEMESIS)** | `pls_renrifle_f` | `sub_CAC2C` (fire one rifle bullet / cannon shell) | rifle burst cadence, §2a |
| 14 with section ≠ 0 (GLADIUS) | `pls_nonock_f` | `prg_PLY` 0xc7c24 | no hit reaction (clip 30) |
| 27, 32 | `pls_fastcan_f` | `sub_CA400`, `sub_CA508` | cannon clips at 1.5× / 1.625× |
| 28 (AVALON) | `pls_longlaz_f` | `sub_C8E4C` | laser cooldown 2880 |
| **16 (REGULUS)** | none (`default: return`) | | |

IDA cross-references of the four flags list exactly `set_player` plus the readers above, nothing else [code: xrefs].

### 2a. NEMESIS burst rifle [code `sub_CAC2C` 0xcac2c, `sub_C8E4C` 0xc8e4c]

- Without the flag every rifle bullet sets the weapon cooldown to 480 (0.25 s, 4 shots/s).
- With it, a counter `n2_4` cycles 0 → 1 → 2: cooldown 256, 256, then 832 → **three shots in 0.133 s steps, then 0.433 s**
  (1344 units = 0.70 s per burst, 4.29 shots/s on average).
- The counter is reset to 0 when a touch starts (`sub_C8E4C`: `if (BYTE1(ply_tapdt_0)) n2_4 = 0` before the rifle call), so
  every new press starts a fresh burst.
- Damage per bullet is unchanged (attack × part power). NEMESIS Lv1 with Rifle NO-100: 1500 per bullet, ~6430 per second
  vs MERCURY's 1000 / 4000.
- The same function also fires the cannon (`sub_CAC2C(…, 1)` from `sub_CA508`), so cannon shots advance the shared counter.
  The cannon's own cycle (clip 55, 52 ticks) is longer than any burst gap, so this has no visible effect [inf].

### 2b. REGULUS: nothing in code

- No flag, and the movement code does not read the body: `ply_move` 0xc8bb8 and the move branch of `sub_C8E4C` add
  `0.1 u/frame × stick × (1.1 / 1.2 with Multi Booster) × (1.4 while boosting)` for every body [code].
- The boost drains 256 EP per frame for every body (`n999999_1 -= n56/4·n56`) [code]. REGULUS's 160000 EP therefore boosts
  about **10.4 s** from full, NEMESIS 8.5 s, MERCURY 6.5 s [inf from the code numbers]. That and the stats are the only
  original differences; "advanced piloting" is flavour text.
- Its boost clips hover instead of gliding on the feet (§4) [data].
- Our plan (user, 2026-10-21) makes REGULUS the most mobile body. That is a design choice, not the original's behaviour.

## 3. Default weapons [code `save_s_init` 0x894f4, `chk_can_equip` 0xbde18]

- `save_s_init` writes the same three parts into each of the 64 body save slots: `0x000C0002` (Rifles NO-100 = part 2,
  Missiles NO-100 = 12) and `22` (Cannons NO-200). The Fighter Knuckle (part 1) is always weapon 0 (`cal_ply_stat`).
- So **NEMESIS and REGULUS start with exactly MERCURY's weapons**: rifle, missiles, cannon, punch. Laser and grenade must
  be bought.
- `chk_can_equip`: one part per category, and the summed part cost must fit the body's slots (16 / 20 leave room for
  upgrades the mod does not model).

Lv1 damage per hit against 0 % resist (`set_dmg`, MERCURY_ORIGINAL.md §0) [code formula]:

| | rifle | missile | cannon direct / splash | punch |
|---|---|---|---|---|
| MERCURY (1000) | 1000 | 1000 | 2000 / 1500 | 10000 |
| NEMESIS (1500) | 1500 | 1500 | 3000 / 2250 | 15000 |
| REGULUS (1300) | 1300 | 1300 | 2600 / 1950 | 13000 |

Resists reduce incoming damage per channel by that percentage: NEMESIS takes 98 % from impact and 95 % from explosions;
REGULUS takes 97 % from everything [code `set_dmg`; the percentages are data].

## 4. Fire points and motions [data: PSCf containers; code: `shdPartsAtbitToNo` + `shdPartsCalPos`]

The original finds a weapon's fire point by a per-joint attribute word (`PSCf +80`, one u32 per joint) and fires from that
joint's posed pivot (`shdPartsCalPos(model, joint, zeropos)` in `sub_CAC2C`, `sub_CAFE4`, `sub_C8E4C`) [code]. Bits: 0x400
rifle, 0x800 missile, 0x1000 cannon, 0x2000 laser, 0x4000 grenade, 0x100000 / 0x200000 left / right jet (`set_player`
attaches KN_JET0 there). Read for all three containers; MERCURY's words reproduce MERCURY_ORIGINAL.md's table exactly (the
control) [data].

| Job | MERCURY (0214) | NEMESIS (0208) | REGULUS (0216) |
|---|---|---|---|
| rifle + laser | 14 ☆右腕兵装懸架システム05 | 15 右腕兵装懸架システム05 | **15 ライフル銃口** (a muzzle locator, no mesh) |
| missile | 18 ミサイルポッド | **11 右腕兵装懸架システム01** (the right shoulder block with a 4-tube box) | **22 ミサイル発射** (locator on top of 21 ☆右肩武装, the right shoulder launcher) |
| cannon | 17 キャノン03 (chain 15-17 on the back) | 18 キャノン3 (chain 16-18 on the back) | **20 左肩武装05** (chain 16-20 on the left shoulder) |
| grenade | 15 キャノン01 | 16 キャノン1 | **06 左腕兵装懸架システム02** (left upper arm) |
| jets L / R | 33 / 35 バックユニット02 | 35 / 37 ブースター02 | 24 / 26 スラスター (locators) |
| arms (left / right root) | 05 / 10 | 05 / **11** | 05 / 10 |
| turret + cockpit | 02 | 02 | 02 |

- NEMESIS's left arm also carries `10 左腕シールド内臓打杭機構` (a shield with a built-in pile driver). It is model only: melee
  is the shared Fighter Knuckle, with no body-specific code [code: no kind test outside `set_player`].
- Joint counts: MERCURY 37, NEMESIS 39, REGULUS 39 (with the `(No Group)` root). Joint numbers differ between the bodies, so
  joint prefixes must come from the per-body role table, not from MERCURY's numbers.

Motions [data, decoded with `motion-research/dguns_motion.py`, which reproduces MERCURY's V8 clips exactly]:

- All three containers have the same 24 player slots (0, 1, 5, 10-13, 15, 16, 20-23, 25-28, 30, 35, 45, 46, 50, 54, 55) with
  the same timing and root-motion fields.
- **NEMESIS's leg motion is numerically MERCURY's**: the same planted-foot speeds 2.89 / 4.06 / 3.09 / 3.08 m/s at ×4 for
  walk forward / back / left / right.
- **REGULUS's boost clips 25-28 hover**: the lower foot stays 0.15 m (forward), 0.34 m (backward) and 0.03 m (sideways)
  above the ground at ×4. MERCURY and NEMESIS glide with their feet on the ground.
- **REGULUS has 8 more slots, 62-69**. 62-65 are identical to 25-28 (max joint rotation difference < 0.05°). 66-69 are
  ground-glide boosts like MERCURY's 25-28.
- The player program never selects 62-69:
  - `sub_C8E4C` writes only 0/1, 10-13, 15/16, 25-28; other paths use 2/20/21/23, 30, 35, 45, 50, 54, 55 (`prg_PLY`,
    `sub_CA400`) [code].
  - No instruction in the library loads 62-69 as an immediate, apart from unrelated stack offsets [code: scan of every
    function].
  - [inf] They serve the enemy KN_RD12 (kind 16, enemy program `prg_RD26`, data-driven motion scripts) or are left over.
- Clip 54 (cannon unfold) moves the cannon chain:
  - NEMESIS: 16/17/18 by 77° / 61° / 180°;
  - REGULUS: 16/17/18 by 24° / 64° / 15°, with 19/20 following;
  - plus, in both, the arms, the head and the turret by up to 47°. The mod's cannon layer plays the cannon chain only, as
    for MERCURY.

## 5. The same models as enemies [data: tables.json `kinds`, `enemies`]

- Kind 2 KN_RD04 (model 208) is also an enemy type (enemy rows 54 and 55: HP 10000 / 20000, resist 90/90/90 or 0/30/50).
- Kinds 2, 14 and 16 all run `prg_RD26` as enemies.
- `set_player`'s flags apply only to the player body.

## 6. Evidence and gaps

- Verified from code: the four body flags and all their readers (IDA cross-references); the burst cadence and its reset;
  the identical movement and boost code; the default loadout for every body; the fire-point lookup by attribute bit; the
  player's clip selection.
- Verified from data: body records, attribute words (control: MERCURY), clip slots and timing, foot heights, identical clips
  (Blender evaluation of the baked clips, `review/check_*.json`).
- Inferred: the meaning of +24 = −5 for REGULUS; that 62-69 belong to the enemy KN_RD12; the boost durations (code numbers,
  not measured in play).
- Not checked: other kind tests elsewhere in the program (only `set_player`'s switch and the functions named above were
  read; the flags' cross-references are complete); the original running on a device.
