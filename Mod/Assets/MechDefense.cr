<?xml version="1.0" encoding="utf-8"?>
<!-- MechDefense recipes. Result data = tower data value in tools/towers.json; upgrades take the lower tower as an ingredient
     ("mddeploy:<data>"). Since 0.9.0 the towers come in three tiers (docs/tasks/2026-10-20-progression-robots.md):
     tier 1 vanilla only; tier 2 three alloy plates instead of three iron ingots; tier 3 alloy plates and two fire-control
     chips (+ an energy core for the energy towers). The progression items are MDPartBlock ("mdpart:<data>"): 0 robot scrap,
     1 alloy plate, 2 fire-control chip, 3 energy core, 4 mech blueprint, 5 decoy beacon, 6-8 weapon kits Mk1-Mk3.
     Robots drop scrap, chips and cores. Without fighting, plates, chips and cores are made from vanilla materials at five
     times their normal price (user, 2026-10-20: 不打怪要贵 5 倍): plate 3 iron (two scrap counted as one iron ingot each,
     plus the ingot) -> 15; chip 2 germanium + 1 copper -> 10 + 5; core 1 diamond + 2 copper -> 5 + 10. The blueprint
     only drops from the boss and the mech recipe uses it up.
     Vanilla ingredients and this mod's own items only: with RecipaediaEX (the user's pack) one unresolved ingredient drops
     that recipe and every later one of the merged list from the crafting table. -->
<Recipes>
  <!-- tier 1 -->
  <Recipe Result="MDDeploymentBlock:0" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" c="copperingot" i="ironingot" Description="铳械塔：一阶，基础防御塔">
    "rgr"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:5" ResultCount="1" RequiredHeatLevel="0" b="bomb" c="copperingot" i="ironingot" Description="榴弹塔：一阶，基础群攻塔">
    " b "
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:12" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" r="rod" w="waterbucket" c="copperingot" i="ironingot" Description="激流塔：一阶，近防推退，返还空桶">
    "rwr"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:9" ResultCount="1" RequiredHeatLevel="0" s="sulphurchunk" l="glass" c="copperingot" i="ironingot" Description="“酸洗”MK-Ⅰ：一阶，降低敌人防御">
    "sls"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:10" ResultCount="1" RequiredHeatLevel="0" p="purpleflower" l="glass" s="sulphurchunk" c="copperingot" i="ironingot" Description="“毒沼”MK-Ⅰ：一阶，毒雾减速并持续伤害">
    "plp"
    "scs"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:11" ResultCount="1" RequiredHeatLevel="0" e="ice" l="glass" c="copperingot" i="ironingot" Description="液氮塔：一阶，减速并冻结敌人">
    "ele"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:20" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" r="rod" m="magmabucket" c="copperingot" i="ironingot" Description="AF1“熔甲者”：一阶，近防喷火，返还空桶">
    "rmr"
    "ici"
    "iii"
  </Recipe>
  <!-- tier 2: three alloy plates -->
  <Recipe Result="MDDeploymentBlock:3" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" t="mddeploy:0" a="mdpart:1" Description="扩装铳械塔：二阶，由铳械塔升级">
    "rgr"
    "gtg"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:6" ResultCount="1" RequiredHeatLevel="0" b="bomb" t="mddeploy:5" a="mdpart:1" Description="高爆榴弹塔：二阶，由榴弹塔升级">
    "bbb"
    "btb"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:14" ResultCount="1" RequiredHeatLevel="0" l="lightbulb" g="germaniumchunk" b="battery" a="mdpart:1" Description="射线塔：二阶，充能射线">
    " l "
    "gbg"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:16" ResultCount="1" RequiredHeatLevel="0" w="wire" b="battery" c="copperingot" a="mdpart:1" Description="电涌塔：二阶，弹射电击">
    "wbw"
    "cbc"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:18" ResultCount="1" RequiredHeatLevel="0" s="soundgenerator" b="battery" c="copperingot" i="ironingot" a="mdpart:1" Description="全向声波塔：二阶，范围晕眩">
    "sbs"
    "ici"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:19" ResultCount="1" RequiredHeatLevel="0" w="cottonwad" l="glass" b="battery" i="ironingot" a="mdpart:1" Description="医疗塔：二阶，治疗玩家、修复友方炮塔">
    "wlw"
    "ibi"
    "aaa"
  </Recipe>
  <!-- tier 3: alloy plates and two fire-control chips; the energy towers also an energy core -->
  <Recipe Result="MDDeploymentBlock:4" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" d="diamond" g="gunpowder" t="mddeploy:3" a="mdpart:1" Description="“暴雨”铳械塔：三阶，由扩装铳械塔升级">
    "xdx"
    "gtg"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:7" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" l="glass" d="diamond" m="ironblock" a="mdpart:1" Description="哨戒塔：三阶，远程点杀">
    "xlx"
    "ada"
    "ama"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:13" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" d="diamond" k="largegunpowderkeg" m="ironblock" a="mdpart:1" Description="重锤迫击炮：三阶，远程重炮">
    "xdx"
    "aka"
    "ama"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:15" ResultCount="1" RequiredHeatLevel="0" l="lightbulb" d="diamond" x="mdpart:2" t="mddeploy:14" e="mdpart:3" a="mdpart:1" Description="高能射线塔：三阶，由射线塔升级">
    "ldl"
    "xtx"
    "aea"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:17" ResultCount="1" RequiredHeatLevel="0" n="magnet" d="diamond" x="mdpart:2" t="mddeploy:16" e="mdpart:3" a="mdpart:1" Description="甲二型“电荷”：三阶，由电涌塔升级">
    "ndn"
    "xtx"
    "aea"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:8" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" d="diamond" m="ironblock" k="largegunpowderkeg" a="mdpart:1" Description="序章炮塔：三阶，大型攻城重炮">
    "xdx"
    "mkm"
    "aaa"
  </Recipe>
  <!-- 转轮机枪塔 Ⅰ-Ⅲ (data 32-34; 0.9.0 round 6, the towers rebuilt from a 2D tower-defence game, E:/projects/sog-tower-model):
       the same tier chain as the gun towers above (user, 2026-10-22: 合成升级) - tier 1 vanilla, tier 2 = tier 1 + three alloy
       plates, tier 3 = tier 2 + alloy plates and two fire-control chips. -->
  <Recipe Result="MDDeploymentBlock:32" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" c="copperingot" i="ironingot" Description="转轮机枪塔 Ⅰ：一阶，高速连射，对地对空">
    "rrr"
    "gcg"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:33" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" t="mddeploy:32" a="mdpart:1" Description="转轮机枪塔 Ⅱ：二阶，由转轮机枪塔 Ⅰ 升级">
    "rrr"
    "gtg"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:34" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" d="diamond" g="gunpowder" t="mddeploy:33" a="mdpart:1" Description="转轮机枪塔 Ⅲ：三阶，由转轮机枪塔 Ⅱ 升级">
    "xdx"
    "gtg"
    "aaa"
  </Recipe>
  <!-- 冰冻塔 Ⅰ-Ⅲ (data 35-37) and 火焰塔 Ⅰ-Ⅲ (data 38-40), 0.10.0: the same tier chain. -->
  <Recipe Result="MDDeploymentBlock:35" ResultCount="1" RequiredHeatLevel="0" e="ice" r="rod" c="copperingot" i="ironingot" Description="冰冻塔 Ⅰ：一阶，低温喷射，减速">
    "eee"
    "rcr"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:36" ResultCount="1" RequiredHeatLevel="0" e="ice" r="rod" t="mddeploy:35" a="mdpart:1" Description="冰冻塔 Ⅱ：二阶，由冰冻塔 Ⅰ 升级">
    "eee"
    "rtr"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:37" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" d="diamond" e="ice" t="mddeploy:36" a="mdpart:1" Description="冰冻塔 Ⅲ：三阶，由冰冻塔 Ⅱ 升级">
    "xdx"
    "ete"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:38" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" i="ironingot" m="magmabucket" r="rod" c="copperingot" Description="火焰塔 Ⅰ：一阶，转向喷火，返还空桶">
    "imi"
    "rcr"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:39" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" r="rod" m="magmabucket" t="mddeploy:38" a="mdpart:1" Description="火焰塔 Ⅱ：二阶，由火焰塔 Ⅰ 升级，返还空桶">
    "rmr"
    "rtr"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:40" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" x="mdpart:2" m="magmabucket" d="diamond" t="mddeploy:39" a="mdpart:1" Description="火焰塔 Ⅲ：三阶，由火焰塔 Ⅱ 升级，返还空桶">
    "xmx"
    "dtd"
    "aaa"
  </Recipe>
  <!-- 导弹塔 Ⅰ-Ⅲ (data 41-43), 0.10.0. -->
  <Recipe Result="MDDeploymentBlock:41" ResultCount="1" RequiredHeatLevel="0" g="gunpowder" i="ironingot" r="rod" c="copperingot" Description="导弹塔 Ⅰ：一阶，导弹，对地对空">
    "gig"
    "rcr"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:42" ResultCount="1" RequiredHeatLevel="0" g="gunpowder" r="rod" t="mddeploy:41" a="mdpart:1" Description="导弹塔 Ⅱ：二阶，由导弹塔 Ⅰ 升级">
    "ggg"
    "rtr"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:43" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" g="gunpowder" d="diamond" t="mddeploy:42" a="mdpart:1" Description="导弹塔 Ⅲ：三阶，由导弹塔 Ⅱ 升级">
    "xgx"
    "dtd"
    "aaa"
  </Recipe>
  <!-- 加农炮 Ⅰ-Ⅲ (data 47-49) and 防空炮 Ⅰ-Ⅲ (data 50-52), 0.10.0. -->
  <Recipe Result="MDDeploymentBlock:47" ResultCount="1" RequiredHeatLevel="0" g="gunpowder" i="ironingot" c="copperingot" Description="加农炮 Ⅰ：一阶，炮弹溅射，只打地面">
    "ggg"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:48" ResultCount="1" RequiredHeatLevel="0" g="gunpowder" i="ironingot" t="mddeploy:47" a="mdpart:1" Description="加农炮 Ⅱ：二阶，由加农炮 Ⅰ 升级">
    "ggg"
    "iti"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:49" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" k="largegunpowderkeg" d="diamond" t="mddeploy:48" a="mdpart:1" Description="加农炮 Ⅲ：三阶，由加农炮 Ⅱ 升级">
    "xkx"
    "dtd"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:50" ResultCount="1" RequiredHeatLevel="0" r="rod" c="copperingot" i="ironingot" g="gunpowder" Description="防空炮 Ⅰ：一阶，优先打空中">
    "rcr"
    "igi"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:51" ResultCount="1" RequiredHeatLevel="0" r="rod" c="copperingot" t="mddeploy:50" a="mdpart:1" Description="防空炮 Ⅱ：二阶，由防空炮 Ⅰ 升级">
    "rcr"
    "rtr"
    "aaa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:52" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" c="copperingot" d="diamond" t="mddeploy:51" a="mdpart:1" Description="防空炮 Ⅲ：三阶，由防空炮 Ⅱ 升级">
    "xcx"
    "dtd"
    "aaa"
  </Recipe>
  <!-- Mercury, the mech (data 1), from the towers that are its weapons: "暴雨" (rifle), 高能射线塔 (laser), 序章炮塔
       (cannon), two 高爆榴弹塔 (missiles, grenades), with two alloy plates, the boss's blueprint (used up) and an energy
       core; player level 8 (user, 2026-10-20: only the mech has a level requirement; Wonderful Era's vehicle keys ask 4-10). -->
  <Recipe Result="MDDeploymentBlock:1" ResultCount="1" RequiredHeatLevel="0" RequiredPlayerLevel="8" p="mddeploy:4" l="mddeploy:15" c="mddeploy:8" a="mdpart:1" b="mdpart:4" g="mddeploy:6" e="mdpart:3" Description="水星机甲：由暴雨铳械塔、高能射线塔、序章炮塔、高爆榴弹塔和机甲设计图组装（消耗设计图，需要玩家等级 8）">
    "plc"
    "aba"
    "geg"
  </Recipe>
  <!-- hero mechs (0.9.0 round 5, user 2026-10-21, plan confirmed): 复仇女神 from an unmodified Mercury + one blueprint + two
       energy cores + two fire-control chips + three alloy plates, player level 12; 轩辕十四 from 复仇女神 + two blueprints + four
       energy cores + two chips, level 16. Blueprints are used up. -->
  <Recipe Result="MDDeploymentBlock:30" ResultCount="1" RequiredHeatLevel="0" RequiredPlayerLevel="12" m="mddeploy:1" a="mdpart:1" b="mdpart:2" c="mdpart:3" p="mdpart:4" Description="复仇女神机甲：由水星机甲升级（消耗机甲设计图，需要玩家等级 12）">
    "cac"
    "bmb"
    "apa"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:31" ResultCount="1" RequiredHeatLevel="0" RequiredPlayerLevel="16" n="mddeploy:30" b="mdpart:2" c="mdpart:3" p="mdpart:4" Description="轩辕十四机甲：由复仇女神机甲升级（消耗两张机甲设计图，需要玩家等级 16）">
    "cpc"
    "bnb"
    "cpc"
  </Recipe>
  <!-- materials: the robot route (furnace) and the fallbacks at five times the normal price (crafting table) -->
  <Recipe Result="MDPartBlock:1" ResultCount="1" RequiredHeatLevel="2" a="mdpart:0" b="ironingot" Description="合金板：机兵残骸×2 + 铁锭，熔炉炼制">
    "aab"
  </Recipe>
  <Recipe Result="MDPartBlock:1" ResultCount="1" RequiredHeatLevel="0" a="ironblock" b="ironingot" c="copperingot" Description="合金板（不打机兵的做法，贵 5 倍）：铁块 + 铁锭×3 + 铜锭×3">
    "bcb"
    "cac"
    " b "
  </Recipe>
  <Recipe Result="MDPartBlock:2" ResultCount="1" RequiredHeatLevel="0" a="semiconductorblock" g="germaniumchunk" c="copperingot" Description="火控芯片（不打机兵的做法，贵 5 倍）：半导体块 + 锗 + 铜锭×5">
    "cgc"
    "cac"
    " c "
  </Recipe>
  <Recipe Result="MDPartBlock:3" ResultCount="1" RequiredHeatLevel="0" d="diamond" k="copperblock" c="copperingot" Description="能量核心（不打机兵的做法，贵 5 倍）：钻石×5 + 铜块 + 铜锭">
    "dcd"
    "dkd"
    " d "
  </Recipe>
  <!-- the boss's beacon and the mech's weapon kits -->
  <Recipe Result="MDPartBlock:5" ResultCount="1" RequiredHeatLevel="0" l="lightbulb" w="wire" a="mdpart:1" x="mdpart:2" e="mdpart:3" Description="诱敌信标：把恶魔机甲引出来">
    "lwl"
    "axa"
    "aea"
  </Recipe>
  <Recipe Result="MDPartBlock:6" ResultCount="1" RequiredHeatLevel="0" r="rod" x="mdpart:2" a="mdpart:1" Description="武器改装件 Mk1：机甲武器伤害 ×1.15">
    "rxr"
    "aaa"
    " a "
  </Recipe>
  <Recipe Result="MDPartBlock:7" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" a="mdpart:1" e="mdpart:3" Description="武器改装件 Mk2：机甲武器伤害 ×1.3（先装 Mk1）">
    "xax"
    "aea"
    " a "
  </Recipe>
  <Recipe Result="MDPartBlock:8" ResultCount="1" RequiredHeatLevel="0" x="mdpart:2" a="mdpart:1" e="mdpart:3" Description="武器改装件 Mk3：机甲武器伤害 ×1.5（先装 Mk2）">
    "xex"
    "aea"
    " x "
  </Recipe>
</Recipes>
