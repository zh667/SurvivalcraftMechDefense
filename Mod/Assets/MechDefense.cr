<?xml version="1.0" encoding="utf-8"?>
<!-- MechDefense tower recipes (crafting table, vanilla materials only). Result data = tower data value in
     tools/towers.json; upgrades take the lower tower as an ingredient ("mddeploy:<data>"). -->
<Recipes>
  <Recipe Result="MDDeploymentBlock:0" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" c="copperingot" i="ironingot" Description="铳械塔：基础防御塔">
    "rgr"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:5" ResultCount="1" RequiredHeatLevel="0" b="bomb" c="copperingot" i="ironingot" Description="榴弹塔：基础群攻塔">
    " b "
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:12" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" r="rod" w="waterbucket" c="copperingot" i="ironingot" Description="激流塔：近防推退，返还空桶">
    "rwr"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:3" ResultCount="1" RequiredHeatLevel="0" r="rod" g="gunpowder" t="mddeploy:0" c="copperingot" i="ironingot" Description="扩装铳械塔：由铳械塔升级">
    "rgr"
    "gtg"
    "ici"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:4" ResultCount="1" RequiredHeatLevel="0" r="rod" d="diamond" g="gunpowder" t="mddeploy:3" m="ironblock" i="ironingot" Description="“暴雨”铳械塔：由扩装铳械塔升级">
    "rdr"
    "gtg"
    "imi"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:6" ResultCount="1" RequiredHeatLevel="0" b="bomb" t="mddeploy:5" c="copperingot" i="ironingot" Description="高爆榴弹塔：由榴弹塔升级">
    "bbb"
    "btb"
    "ici"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:7" ResultCount="1" RequiredHeatLevel="0" l="glass" r="rod" d="diamond" m="ironblock" i="ironingot" Description="哨戒塔：远程点杀">
    "lrr"
    "idi"
    "imi"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:9" ResultCount="1" RequiredHeatLevel="0" s="sulphurchunk" l="glass" c="copperingot" i="ironingot" Description="“酸洗”MK-Ⅰ：降低敌人防御">
    "sls"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:10" ResultCount="1" RequiredHeatLevel="0" p="purpleflower" l="glass" s="sulphurchunk" c="copperingot" i="ironingot" Description="“毒沼”MK-Ⅰ：毒雾减速并持续伤害">
    "plp"
    "scs"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:11" ResultCount="1" RequiredHeatLevel="0" e="ice" l="glass" c="copperingot" i="ironingot" Description="液氮塔：减速并冻结敌人">
    "ele"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:14" ResultCount="1" RequiredHeatLevel="0" l="lightbulb" g="germaniumchunk" b="battery" i="ironingot" Description="射线塔：充能射线">
    " l "
    "gbg"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:15" ResultCount="1" RequiredHeatLevel="0" l="lightbulb" d="diamond" b="battery" t="mddeploy:14" i="ironingot" Description="高能射线塔：由射线塔升级">
    "ldl"
    "btb"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:16" ResultCount="1" RequiredHeatLevel="0" w="wire" b="battery" c="copperingot" i="ironingot" Description="电涌塔：弹射电击">
    "wbw"
    "cbc"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:17" ResultCount="1" RequiredHeatLevel="0" n="magnet" d="diamond" b="battery" t="mddeploy:16" i="ironingot" Description="甲二型“电荷”：由电涌塔升级">
    "ndn"
    "btb"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:18" ResultCount="1" RequiredHeatLevel="0" s="soundgenerator" b="battery" c="copperingot" i="ironingot" Description="全向声波塔：范围晕眩">
    "sbs"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:19" ResultCount="1" RequiredHeatLevel="0" w="cottonwad" l="glass" b="battery" i="ironingot" Description="医疗塔：治疗玩家、修复友方炮塔">
    "wlw"
    "ibi"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:20" ResultCount="1" RequiredHeatLevel="0" Remains="EmptyBucketBlock" RemainsCount="1" r="rod" m="magmabucket" c="copperingot" i="ironingot" Description="AF1“熔甲者”：近防喷火，返还空桶">
    "rmr"
    "ici"
    "iii"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:13" ResultCount="1" RequiredHeatLevel="0" d="diamond" k="largegunpowderkeg" m="ironblock" i="ironingot" Description="重锤迫击炮：远程重炮">
    "idi"
    "iki"
    "mmm"
  </Recipe>
  <Recipe Result="MDDeploymentBlock:8" ResultCount="1" RequiredHeatLevel="0" m="ironblock" d="diamond" k="largegunpowderkeg" Description="序章炮塔：大型攻城重炮">
    "mdm"
    "mkm"
    "mmm"
  </Recipe>
</Recipes>
