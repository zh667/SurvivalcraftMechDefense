# Endfield Gun Towers - 0.3.1

## 0.3.1 录屏反馈修复

本次针对 `QQ20261009-112831-HD.mp4` 与用户补充的五项问题：

- 转视角时的花纹变化：源方块模型存在共面重叠面，贴图文件本身未改变。
  导入时按后绘制的装饰面优先，裁掉下面重复的表面，UV 按原平面线性插值。
  三座塔逐三角形复查：同刚性骨骼的共面重叠为 0，退化三角形为 0。
  原 `geo.json`、PNG 和 Blender 文件不改动；这是运行网格清理。
- 炮管外甩：原始 `firePos` 编号在扩装/暴雨塔中左右反转，之前按列表顺序
  匹配形成约 0.5 米错误枢轴偏移。现按实际 X 位置与 VAT track 2/3 对应。
  完整开火过程含插值的炮口横向偏离分别小于 2.1/4.4/3.2 厘米，不再绕到外侧。
  保留原炮管自转和后坐数据，不用固定炮管掩盖绑定问题。
- 死亡循环：炮塔和敌兵统一使用不可逆 `Destroyed` 状态，拒绝后续 Hit/Fire。
  死亡与残骸计时存档，重进不从头播放或重置消失时间；8 秒模拟时间后移除。
- 固定占地：铳械塔 2×2×3、扩装 2×2×4、暴雨 3×3×4（宽×深×高）。
  右键选择的方块是占地西南角，按格对齐；必须有完整支撑和空气空间。
  固定 Body 不运行重力/碰撞位移积分，不接受推挤、冲量或直接移动；占地内
  阻止普通方块放置。仍为可受击结构实体，不是写入地形的假方块。
- Neorxna：对实际 1.4 模组的 `SetInfo_Entity` 做可选兼容，显示中文塔名、
  友好模组名和占地尺寸，保留其装甲值显示；不修改 Neorxna 文件。

新部署采用方格规则；旧存档炮塔保持原位置并固定，避免升级时搬动旧设施。
需要重新对格的旧塔请拆除后重新部署。固定支撑不等于完整多方块拆除/坍塌系统。
第三方特效着色器的移植差异仍见下文；本轮没有重做特效贴图。

验证：0.3.1 构建零警告零错误；1,783 项资源校验及完整共面/炮管轨迹检查通过；
加载真实 Neorxna 1.4 的隔离运行中 120 项通过，包括固定位置、空位/支撑拒绝、
中文信息显示、开火、不可逆死亡、死亡存档恢复及残骸最终移除。
记录：`local/runtime-endfield-fixes/endfield-result.json` 和
`local/custom-model-integration/feedback-verification.json`。

铳械塔、扩装铳械塔、“暴雨”铳械塔已同时换成 `E:/projects/endfield-turret-model/v1`
的自制方块模型和自绘贴图。该项目的反馈记录确认这三座最新版为 V1；
`batch2_v2` 是另外 16 种设施，不应拿来替换这三座。
安装时用 0.3.1 替换旧版，不要同时启用多个版本；在 Weapons 分类
查找三个中文部署物即可。无需弹药物品。原始声音、粒子和弹丸素材继续沿用
终末地；三座的炮头和两根炮管已绑定终末地原始 VAT 数据。旧机甲/敌兵保留。

本版仍有移植差异：Unity 部分着色器/粒子模块和 Wwise 实时混音未完整还原。
动画帧来自原始 VAT，绑定到自制几何时采用骨骼重定向；不能把本版称为原引擎
一比一复现。自制模型与贴图的归属、原始游戏素材的归属分开记录。

Current scope: the three gun towers only. Destroy Gunners content is retained
for old worlds but is not a source of any Endfield tower model, effect or audio.

## Playable entries

| Source identity | Game name | Deployment data | Attack behavior |
| --- | --- | --- | --- |
| battle_turret_1 | 铳械塔 | 0 (existing) | Recovered energy-bullet mesh, 60 units/s |
| battle_turret_2 | 扩装铳械塔 | 3 (new) | Distinct mesh/effects; every fourth round enhanced |
| battle_turret_3 | “暴雨”铳械塔 | 4 (new) | Source super-attack/hit effects; every sixth round area hit |

The names and enhancement counts come from FactoryBuildingTable,
FactoryBattleTable and I18nTextTable_CN. Ranges are 15/15/18; cooldown is 2s.
Damage, blast radius and target selection are Survivalcraft balance adaptations.
The authored fire_1/fire_2 locators follow the rendered aim and animated barrel
pose. Bedrock coordinates follow the authoring project's importer. Old data values 1/2 remain
the old mech/enemy deployment items, so existing inventory values are not reused.

Install `dist/MechDefense-0.3.1.scmod` in place of earlier versions, not alongside them.
Use a new Creative world and the three named deployment items in Weapons.
No ammunition inventory is required. All towers are friendly to the player and
each other; enemies/predators are the targets. Solids and friendly bodies block
shots. Explosions damage hostiles only and do not edit terrain.

## Converted Source Material

Root: `local/endfield/tower-library/three-gun-towers`.

- `selection.json`: exact source asset paths, VFS locations, MD5-checked bundles,
  SHA-256 hashes, per-family ownership, and counted strings in skill payloads.
- `converted`, `json`, `scene-json`: source meshes, textures, materials,
  ParticleSystems, renderers, hierarchy, projectile templates and VAT metadata.
  Original names and PathIDs are retained.
- `builtin-json`: the referenced Unity built-in Quad mesh from the installed
  game's resources (not an invented replacement projectile).
- `audio/banks`, `audio/wem`, `audio/wav`: source banks, 92 original WEMs and
  decoded WAVs. `audio/catalog.json` records 16 complete event graphs and media
  ownership; 42 deterministic mono event mixes use only those original stems.
- `audio/event-previews`: playable WAV versions of the runtime event mixes.
- Runtime assets: `Mod/Assets/Imported/Endfield/battle_turret_1`, `_2`, `_3`,
  `Effects`, `Audio`. After coplanar trimming: 14,778 / 19,263 / 17,778 triangles.
- Authored source snapshots: `Mod/Assets/Models/Endfield`. Source file and texture
  hashes, cube/face counts and animation hashes: `docs/custom-model-provenance.json`.
  No model/texture from the original game replaces these three authored assets.
- FX library: 14 original prefab families, 107 particle emitters, 51 material
  adaptations, source projectile mesh geometry and source textures.

## Conversion Boundary

This is not a byte-identical Unity/Wwise runtime recreation. The source files
remain intact beside the portable runtime data.

- Source bursts, transforms, lifetimes, speed, size/color curves, flipbook UVs,
  basic velocity/rotation and mesh particles are sampled for portable playback.
- Distortion, polar/custom shader streams, soft-depth blending, noise and some
  sub-emitter/trail behavior are not fully ported. Each emitter/material records
  unsupported modules in `Effects/library.json`; appearance can differ.
- Full-precision native RGBAHalf VAT is now decoded for these three tower heads:
  44 frames, three rigid tracks, 30 fps, original idle/attack/damage-start/
  damage-loop/damage-end clips. The shader samples row `43-frame`, quaternion
  columns `2*bone`, translation/scale columns `2*bone+1`; quaternion normalization
  and Z-axis reflection match the recovered single-bone vertex shader.
  `MDEndfieldAnimation` interpolates those samples. Authored cube labels assign
  each rigid barrel group only after ordered geometry matches the authoring tool.
  Sockets use the same matrices as their barrels. Damage holds the source damaged
  state and no longer restarts every 1.4 seconds. Aim yaw/pitch remains engine AI.
- Retargeting anchors use the authored head pivot and source-to-authored muzzle
  offsets. This adapts the original motion to new geometry; it does not claim
  identical vertex positions to the original game. No separate source deploy or
  death clip exists in these five-clip banks: deploy uses source idle, destruction
  uses damage-start then the held damage-loop, not an invented collapse animation.
- Raw WEM-to-WAV is a decode of original media. Runtime Ogg mixes select one
  child per random container and sum layer children with peak limiting. Wwise
  DSP, inherited RTPC, exact loudness and live random history are not recreated.
- The Rainstorm skill names direct super-attack/hit effects, not a distinct
  ProjectileTemplate in the selected source. It therefore uses a direct hit
  in this adapter; no invented projectile mesh is labelled as recovered.
- SkillData counted-string extraction is reference evidence, not a complete
  binary skill timeline decoder. Original full payloads are retained.

## Rebuild And Verify

After source exporters have produced the recorded `json` and `scene-json`:

```powershell
python tools/prepare_endfield_three.py
python tools/extract_endfield_tower_audio.py
python tools/convert_endfield_tower_audio.py
python tools/convert_endfield_three.py
python tools/import_custom_endfield_models.py
python tools/prepare_database.py
python tools/package.py
python tools/verify_endfield_three.py
pwsh -NoProfile -File tools/run_endfield_smoke.ps1
```

`convert_endfield_three.py` now preserves authored runtime models by default;
`--source-models` is an explicit reference-only opt-in to replace them.

The isolated test creates its own runtime and world under `local/runtime-endfield-fixes`.
The runner launches asynchronously: inspect a NEW `endfield-result.json`, not a
previous result. It checks all model/sound loads, original projectile geometry,
all three firing/impact paths, fourth/sixth enhanced FX, automatic acquisition,
wall/friendly exclusion and variant/counter save/reload. Screenshots are in
`ScreenCapture/endfield-shot-1.png`, `endfield-shot-4.png`, `endfield-shot-6.png`.
The 0.3.0 Blender cross-check matched all 69,728 original face corners and UVs.
The 0.3.1 topology is intentionally trimmed and no longer has identical face
corners; `verify_tower_feedback.py` checks overlap-free output and full barrel
paths, while the importer checks unchanged source geometry/atlas hashes and
projected area conservation. Do not use the historical exact-corner test to
assess this intentional triangulation change.
The test does not prove visual parity with the source game.
