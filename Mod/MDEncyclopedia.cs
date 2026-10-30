using System.Text;
using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game;

// 0.11.0 (user, 2026-10-24: "敌人图鉴（在帮助里可以看到敌人信息，包括数值什么的，炮塔这边也是）"; chosen the same day: the enemies in
// the game's own bestiary, the towers as text in Help, everything visible from the start). Every number comes from the live
// data (towers.json through MDTowerLibrary, the loaded recipes, MDRobots.Kinds, MDMechType, MDBoss), so the texts never
// disagree with the game. Design: docs/tasks/2026-10-24-mech-keys-and-encyclopedia.md.
public static class MDEncyclopedia {
    // ---- the bestiary lists templates with CreatureEggData (EggTypeIndex >= 0). tools/prepare_database.py gives these four one
    // (index 3900-3903, after the game's 0-49; ShowEgg false and no food value: a hidden egg type, no item, no recipe); skins B
    // and C stay out.
    public static readonly (string Template,string Kind)[] Bestiary=[("MDRobot05","RD05"),("MDRobot06","RD06"),("MDRobot07","RD07"),("MDDevilMech","RD17")];
    // the numbers for a bestiary entry of ours (null for any other creature). The list shows each creature's description in one
    // row, so the numbers go only on its own page (MDBestiaryPagePatch), computed when shown: the mod settings may have changed.
    public static string NumbersFor(BestiaryCreatureInfo info) {
        if(info?.EntityValuesDictionary==null)return null;
        foreach(var (template,kind) in Bestiary)
            if(ReferenceEquals(info.EntityValuesDictionary,DatabaseManager.FindEntityValuesDictionary(template,false)))return RobotNumbers(MDRobots.Kind(kind),info.AttackResilience);
        return null;
    }
    // The bestiary frames a model from its size to the power 0.75, so a 9.6 m mech with 15 m wings sticks out of the frame:
    // our robots are moved back until the whole model fits (only when it would not).
    public static void FrameModel(string modelName,ModelWidget w) {
        if(modelName==null||w==null||!Bestiary.Any(b=>modelName.StartsWith($"Imported/{b.Kind}/")))return;
        try {
            var model=ContentManager.Get<Model>(modelName);var bones=new Matrix[model.Bones.Count];model.CopyAbsoluteBoneTransformsTo(bones);
            var box=model.CalculateAbsoluteBoundingBox(bones);var size=box.Size();float aspect=w.Size.Y>0?Math.Max(1,w.Size.X/w.Size.Y):1;
            float half=.55f*Math.Max(size.Y,Math.Max(size.X,size.Z)/aspect),tan=MathF.Tan(w.ViewFov*.5f),dist=Vector3.Distance(w.ViewPosition,w.ViewTarget);
            if(tan<=0||half<=dist*tan)return;
            var dir=w.ViewPosition-w.ViewTarget;dir.Y=0;dir=dir.LengthSquared()>0?Vector3.Normalize(dir):-Vector3.UnitZ;
            w.ViewTarget=box.Center();w.ViewPosition=box.Center()+dir*(half/tan)+Vector3.UnitY*size.Y*.1f;
        } catch(Exception e){Log.Warning("[MechDefense] bestiary model: "+e.Message);}
    }
    // when it shows up, from the current mod settings (the same rules as MDRobots' wild spawns and MDRaids: elites 7 days later in
    // the wild, 3 days later in raids)
    static string Appear(MDRobotKind k) {
        if(k.Boss)return $"用诱敌信标召唤，{MDBoss.Countdown:0} 秒后从远处飞来"+(MDSettings.BossKills>0?$"；生存模式里每击毁 {MDSettings.BossKills} 台机兵也会自动出现（这个存档已击毁 {MDBossKills.Count} 台）":"");
        int d=MDSettings.StartDay;var parts=new List<string>();
        if(d>0&&MDSettings.SpawnTime!=MDSpawnTime.Off)parts.Add($"第 {d+(k.Elite?7:0)} 天起出现在野外（{MDSettings.SpawnTimeName(MDSettings.SpawnTime)}）");
        if(d>0&&MDSettings.RaidChance>0)parts.Add(k.Elite?$"第 {d+3} 天起混在夜袭里（{MDSettings.RaidChance:0}% 的夜晚有夜袭）":$"第 {d} 天起 {MDSettings.RaidChance:0}% 的夜晚成群来袭（附近有炮塔就打炮塔阵地，没有就冲着玩家来）");
        return parts.Count==0?"现在的模组设置不刷新敌方机兵":string.Join("；",parts)+"（模组设置里可改）";
    }
    static string WeaponName(MDRobotKind k)=>k.Weapon switch{"twin"=>"双管机炮点射","missile"=>"追踪导弹齐射（锁定时目标身上有预警）","cannon"=>"曲射炮弹","devil"=>"导弹齐射、射线扫射、光剑冲刺三连斩",_=>k.Weapon};
    public static string RobotNumbers(MDRobotKind k,float hp) {
        var s=new StringBuilder("【数值】");
        s.Append($"\n耐久：{(k.Boss?$"{MDBoss.BaseArmour:0} 起，按附近炮塔和机甲加强（每座一 / 二 / 三阶塔 +1000 / 2000 / 3000，每台机甲 +3000～5000，最高 160000）":$"{hp:0}")}");
        s.Append(k.Boss?$"\n移动：飞行 {k.Speed:0.#} 米／秒，冲刺 {MDBossState.ChargeSpeed:0} 米／秒":$"\n移动：{k.Speed:0.#} 米／秒");
        s.Append($"\n武器：{WeaponName(k)}；每轮 {k.Shots} 发，每 {k.Interval:0.#} 秒一轮；射程 {k.Range:0} 米{(k.MinRange>0?$"，{k.MinRange:0} 米内打不到":"")}{(k.Splash>0?$"，爆炸半径 {k.Splash:0.#} 米":"")}");
        s.Append($"\n每发伤害：炮塔／机甲 {k.VsStructure:0}，玩家约 {k.VsCreature*10:0}% 生命（未计护甲）");
        if(k.Boss)s.Append($"\n限伤：每次受到攻击最多扣 {MDBoss.MaxHitShare*100:0}% 耐久");
        if(k.Boss)s.Append($"\n弱点：每 {MDBossState.WeakPeriod:0} 秒在冲击、爆炸、能量之间变化，打中弱点的伤害全额，其他只有 {MDBossState.OtherFactor*100:0}%；脚下光环的颜色就是当前弱点（白 = 冲击，橙 = 爆炸，蓝 = 能量）");
        s.Append("\n出现："+Appear(k));
        s.Append($"\n优先攻击：打过它的玩家或机甲（一直追打，直到对方被消灭、离开 {MDRobots.LoseRange:0} 米或离开游戏；创造模式也是）> 机甲 > 炮塔 > 玩家");
        s.Append(MDSettings.AggroRange>0?$"；看见 {MDSettings.AggroRange:0} 米内的玩家就主动攻击":"；附近没有炮塔、机甲时，玩家不打它它就不攻击");
        return s.ToString();
    }

    // ---- Help: five topics after the game's own (overview, tier 1 / 2 / 3 towers, the mechs). The screen is built while the game
    // loads, maybe before the recipes and the mod settings: the titles go in then, each text is written when its topic opens.
    public const float FirstIndex=900;
    public const string Prefix="MechDefense.";
    static readonly (string Name,string Title)[] topics=[("Towers","机甲防线 · 防御塔总览"),("Tier1","机甲防线 · 一阶防御塔"),("Tier2","机甲防线 · 二阶防御塔"),("Tier3","机甲防线 · 三阶防御塔"),("Mechs","机甲防线 · 主角机甲")];
    public static void AddHelp(HelpScreen help) {
        try {
            for(int i=0;i<topics.Length;i++){var t=new HelpTopic{Index=FirstIndex+i,Name=Prefix+topics[i].Name,Title=topics[i].Title,Text=""};if(help.m_topics.TryAdd(t.Name,t))help.m_topicsList.AddItem(t);}
        } catch(Exception e){Log.Warning("[MechDefense] help topics: "+e.Message);}
    }
    public static void Fill(HelpTopic t) {
        if(t?.Name?.StartsWith(Prefix)!=true)return;
        try{t.Text=Text(t.Name[Prefix.Length..]);}catch(Exception e){t.Text="（暂时无法生成：" +e.Message+"）";Log.Warning("[MechDefense] help text: "+e);}
    }
    public static string Text(string name) {
        var towers=MDTowerLibrary.All.OrderBy(t=>t.data).ToList();
        var tiers=towers.GroupBy(Tier).ToDictionary(g=>g.Key,g=>g.ToList());
        List<MDTowerDef> T(int tier)=>tiers.GetValueOrDefault(tier)??[];
        return name switch{"Towers"=>Overview(towers.Count,T(1).Count,T(2).Count,T(3).Count),"Tier1"=>string.Join("\n\n",T(1).Select(Tower)),
            "Tier2"=>string.Join("\n\n",T(2).Select(Tower)),"Tier3"=>string.Join("\n\n",T(3).Select(Tower)),"Mechs"=>Mechs(),_=>""};
    }
    static string Overview(int all,int t1,int t2,int t3)=>
        $"共 {all} 座防御塔：一阶 {t1}、二阶 {t2}、三阶 {t3}。各阶的数值见后面三条。\n\n"+
        "部署：手持防御塔右键地面。炮塔对齐方格固定，需要完整平整的支撑面和足够净高；占地写在各塔后面（如 2×2 格），占地格里不能再放方块。\n\n"+
        "升级：一阶塔用原版材料在工作台合成；二阶 = 同类的一阶塔 + 合金板等；三阶 = 同类的二阶塔 + 合金板、火控芯片等（射线塔、医疗塔没有同类低阶，直接合成）。合金板、火控芯片、能量核心来自击毁敌方机兵（见“帮助 → 生物图鉴”里的敌人条目）。\n\n"+
        $"维修：手持机兵残骸（每块修 {MDTowerMaintenance.ScrapPoints:0} 点耐久）或合金板（每块 {MDTowerMaintenance.AlloyPoints:0} 点）右键炮塔；铁、铜只能修机甲。医疗塔会慢慢治疗附近的炮塔。拆卸：用镐敲 {MDTowerMaintenance.HitsToDismantle} 下，拆下的炮塔保留耐久，受损的要部署修好后才能当升级材料。\n\n"+
        "雷达：范围内所有防御塔射程变长。医疗塔：治疗附近的玩家、机甲和炮塔。\n\n"+
        "数值说明：射程和占地以格（米）计；“每秒伤害”是持续开火时对单个目标的平均值，溅射、喷射、闪电弹射另外再伤附近的敌人。";
    // the tier from the recipe: fire-control chips -> 3, alloy plates -> 2, else 1 (the same rule the lab checks)
    static int Tier(MDTowerDef d){var ing=Recipe(d)?.Ingredients??[];return ing.Contains("mdpart:2")?3:ing.Contains("mdpart:1")?2:1;}
    static CraftingRecipe Recipe(MDTowerDef d)=>Recipe(d.data);
    static CraftingRecipe Recipe(int data) {
        int block=BlocksManager.GetBlockIndex<MDDeploymentBlock>();
        return CraftingRecipesManager.Recipes.FirstOrDefault(r=>Terrain.ExtractContents(r.ResultValue)==block&&(Terrain.ExtractData(r.ResultValue)&0xFF)==data);
    }
    // "合成：铁锭×3、铜锭、木棍×2"; with exactly one tower or mech among the ingredients "由X升级，另加：…" (水星 takes five towers:
    // a plain list)
    static string RecipeText(CraftingRecipe r) {
        if(r==null)return "没有配方";
        string Name(string ing) {
            if(ing.StartsWith("mddeploy:")&&int.TryParse(ing[9..],out int dd))return MDTowerLibrary.ByData(dd)?.name??(MDMechType.ByData(dd) is {} mt?mt.Name+"机甲":ing);
            try {
                CraftingRecipesManager.DecodeIngredient(ing,out var id,out var data);
                var b=BlocksManager.FindBlocksByCraftingId(id).FirstOrDefault();
                return b==null?ing:b.GetDisplayName(null,Terrain.MakeBlockValue(b.BlockIndex,0,data??0));
            } catch{return ing;}
        }
        var all=r.Ingredients.Where(i=>!string.IsNullOrEmpty(i)).ToList();
        var units=all.Where(i=>i.StartsWith("mddeploy:")).ToList();var from=units.Count==1?units[0]:null;
        var parts=all.Where(i=>i!=from).GroupBy(i=>i).Select(g=>g.Count()>1?$"{Name(g.Key)}×{g.Count()}":Name(g.Key));
        var s=(from!=null?$"由{Name(from)}升级，另加：":"合成：")+string.Join("、",parts);
        return r.RequiredPlayerLevel>1?s+$"（玩家等级 {r.RequiredPlayerLevel:0}）":s;
    }
    static string Targets(MDTowerAttack a)=>a.type is "heal" or "radar"?"":a.groundOnly?"只打地面":a.airFirst?$"优先打空中（地面伤害 ×{a.groundFactor:0.##}）":"地面和空中";
    static string AttackText(MDTowerDef d) {
        var a=d.attack;float per=a.interval>0?a.power/a.interval:0;
        var s=a.type switch{
            "radar"=>$"范围 {a.range:0} 格内所有防御塔射程 ×{a.boost:0.##}",
            "heal"=>$"每 {a.interval:0.#} 秒治疗附近的玩家、机甲和炮塔 {a.power:0.#}",
            "pulse"=>$"每 {a.interval:0.#} 秒一圈脉冲{(a.stun>0?$"，瘫痪 {a.stun:0.#} 秒（首领不受影响）":"")}{(a.power>0?$"，伤害 {a.power:0}":"")}",
            "chain"=>$"每 {a.interval:0.#} 秒一道闪电 {a.power:0}（每秒 {per:0.#}）{(a.bounces>0?$"，再弹射 {a.bounces} 个附近的敌人":"")}",
            "flame"=>$"喷火，喷射带上每个敌人每秒 {per:0.#}",
            "jet"=>(a.power>0?$"喷射，喷射带上每个敌人每秒 {per:0.#}":"喷射，不造成伤害")+(a.knockback>0?"，把敌人冲退":""),
            "missile"=>$"追踪导弹，每 {a.interval:0.##} 秒一发 {a.power:0.#}（每秒 {per:0.#}），必定命中",
            "beam"=>$"激光，每 {a.interval:0.#} 秒一束 {a.power:0}（每秒 {per:0.#}）",
            _=>$"每 {a.interval:0.##} 秒 {a.power:0.#}（每秒 {per:0.#}）"};
        var extra=new List<string>();
        if(a.splash>0&&a.type is not ("flame" or "jet"))extra.Add($"爆炸半径 {a.splash:0.#} 格");
        if(a.slow>0&&a.slow<1)extra.Add($"减速到 ×{a.slow:0.##}");
        if(a.freeze>0)extra.Add($"冻结 {a.freeze:0.#} 秒");
        if(a.stun>0&&a.type!="pulse")extra.Add($"晕眩 {a.stun:0.#} 秒");
        if(a.burn>0)extra.Add($"着火 {a.burn:0.#} 秒");
        if(a.dot>0)extra.Add($"毒雾每秒 {a.dot:0.#}");
        if(a.armorDown>1)extra.Add($"破甲（受到伤害 ×{a.armorDown:0.##}）");
        if(a.zone>0)extra.Add($"留下 {a.zone:0.#} 格范围 {a.zoneTime:0} 秒");
        if(a.minRange>0)extra.Add($"{a.minRange:0} 格内打不到");
        if(a.directional)extra.Add($"只打正前方 {a.range:0}×{a.width:0} 格");
        return extra.Count>0?s+"；"+string.Join("，",extra):s;
    }
    public static string Tower(MDTowerDef d) {
        var a=d.attack;var t=Targets(a);
        return $"{d.name}：{d.summary}\n血量 {d.hp:0}，射程 {a.range:0.#} 格，{AttackText(d)}{(t.Length>0?"，"+t:"")}；占地 {d.cells[0]}×{d.cells[2]} 格。\n{RecipeText(Recipe(d))}";
    }
    static string Mechs() {
        var s=new StringBuilder("可以驾驶的机甲：靠近后按骑乘键上车。装甲耗尽会被摧毁；驾驶时玩家不受伤害。五种武器：步枪、导弹（按住开火扫过目标锁定，松开发射追踪导弹）、火炮（开火时展开）、激光、榴弹。\n");
        foreach(var m in MDMechType.All) {
            s.Append($"\n{m.Name}：装甲 {m.Armour}，武器伤害 ×{m.Damage:0.##}，能量 {m.EnergyMax:0}，导弹最多锁定 {m.Locks} 个");
            if(m.BurstRifle)s.Append("，步枪三连发，受到爆炸伤害 ×"+m.ExplosionTaken.ToString("0.##"));
            if(m.Speed>1)s.Append($"，移动速度 ×{m.Speed:0.##}");
            s.Append($"。\n{RecipeText(Recipe(m.Data))}\n");
        }
        s.Append($"\n操作（当前按键，可在“模组设置 → 机甲防线 → 机甲按键”里改；手机用铃兰触控把屏幕按钮映射到这些键）：{MDMechPilot.KeyHints(true)}。");
        return s.ToString();
    }
}

// The game's Help: our topics after its own (the screen is built once, while the game loads); their text when one opens
[HarmonyPatch(typeof(HelpScreen),MethodType.Constructor)]
public static class MDHelpPatch { public static void Postfix(HelpScreen __instance)=>MDEncyclopedia.AddHelp(__instance); }
[HarmonyPatch(typeof(HelpScreen),nameof(HelpScreen.ShowTopic))]
public static class MDHelpTopicPatch { public static void Prefix(HelpTopic helpTopic)=>MDEncyclopedia.Fill(helpTopic); }

// A robot's own bestiary page: its description, then the numbers
[HarmonyPatch(typeof(BestiaryDescriptionScreen),nameof(BestiaryDescriptionScreen.UpdateCreatureProperties))]
public static class MDBestiaryPagePatch {
    public static void Postfix(BestiaryDescriptionScreen __instance) {
        if(__instance.m_index<0||__instance.m_infoList==null||__instance.m_index>=__instance.m_infoList.Count)return;
        var info=__instance.m_infoList[__instance.m_index];
        if(MDEncyclopedia.NumbersFor(info) is {} n)__instance.m_descriptionWidget.Text=info.Description+"\n\n"+n;
    }
}
// the bestiary's model framing (list and page), for our big robots
[HarmonyPatch(typeof(BestiaryScreen),nameof(BestiaryScreen.SetupBestiaryModelWidget),[typeof(string),typeof(string),typeof(ModelWidget),typeof(Vector3),typeof(bool),typeof(bool)])]
public static class MDBestiaryModelPatch { public static void Postfix(string modelName,ModelWidget modelWidget)=>MDEncyclopedia.FrameModel(modelName,modelWidget); }
