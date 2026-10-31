using Engine;
using Engine.Graphics;

namespace Game;

// 0.16.0 the mod's handbook (user, 2026-10-30: "怎么让玩家更好的认识每种炮塔以及炮弹属性反应呢？可以做一个说明书（物品）"; chosen: craftable,
// and every world gives each player one the first time it is opened). MDPartBlock.Kind.Manual; right-click opens this screen:
// chapters with the damage-type and status icons (Towers/status, the same as over the robots), every text built from the live data
// (MDEncyclopedia, MDElements, MDRobots, MDMechType, MDBossState), so it never disagrees with the game.
public sealed class MDManualScreen:Screen {
    public const string ScreenName="MechDefenseManual";
    public static readonly string[] Chapters=["说明","防御塔","伤害与状态","元素反应","机甲","敌人","首领","防线核心"];
    public static void Open(){if(!ScreensManager.m_screens.ContainsKey(ScreenName))ScreensManager.AddScreen(ScreenName,new MDManualScreen());ScreensManager.SwitchScreen(ScreenName);}
    public int Chapter;   // the page shown (the lab picks one)
    Screen back;bool built,narrow;int builtChapter=-1;float builtWidth;
    readonly MDWorldBackground background=new();
    readonly CanvasWidget root=new(){Size=new Vector2(float.PositiveInfinity)};
    readonly LabelWidget title=new(){Text="机甲防线手册",FontScale=1.1f,TextAnchor=TextAnchor.HorizontalCenter,DropShadow=true};
    readonly StackPanelWidget tabs=new(){Direction=LayoutDirection.Horizontal,HorizontalAlignment=WidgetAlignment.Center};
    readonly ScrollPanelWidget tabScroll=new(){Direction=LayoutDirection.Horizontal,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch};
    readonly CanvasWidget tabHost=new();
    readonly CanvasWidget scrollHost=new();
    readonly ScrollPanelWidget scroll=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch};
    readonly StackPanelWidget content=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch};
    readonly ButtonWidget close=MDUi.Button("返回",140);
    readonly List<ButtonWidget> tabButtons=[];
    public MDManualScreen() {
        Children.Add(background);Children.Add(MDUi.Frame());
        foreach(var c in Chapters){var b=MDUi.Button(c,112);b.Margin=new Vector2(2,0);tabButtons.Add(b);tabs.Children.Add(b);}
        tabScroll.Children.Add(tabs);tabHost.Children.Add(tabScroll);scroll.Children.Add(content);scrollHost.Children.Add(scroll);
        root.Children.Add(title);root.Children.Add(tabHost);root.Children.Add(scrollHost);root.Children.Add(close);Children.Add(root);
    }
    public override void Enter(object[] parameters){background.Reset();back=ScreensManager.PreviousScreen;built=false;}
    public override void Leave(){background.Release();base.Leave();}
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Max(280,available.X),h=Math.Max(220,available.Y);bool n=w<650;
        if(!built||n!=narrow||builtChapter!=Chapter||MathF.Abs(builtWidth-w)>1)Build(n,w);
        root.SetWidgetPosition(title,new Vector2(12,6));title.Size=new Vector2(w-24,36);
        root.SetWidgetPosition(tabHost,new Vector2(12,44));tabHost.Size=new Vector2(w-24,52);
        root.SetWidgetPosition(scrollHost,new Vector2(12,100));scrollHost.Size=new Vector2(w-24,Math.Max(60,h-100-60));
        root.SetWidgetPosition(close,new Vector2((w-140)/2,h-54));
        base.MeasureOverride(available);
    }
    public override void Update() {
        for(int i=0;i<tabButtons.Count;i++)if(tabButtons[i].IsClicked){Chapter=i;scroll.ScrollPosition=0;}
        if(close.IsClicked||Input.Back||Input.Cancel)ScreensManager.SwitchScreen(back??ScreensManager.FindScreen<Screen>("Game")??ScreensManager.FindScreen<Screen>("MainMenu"));
    }

    // ---- the pages
    static Texture2D icons;
    static Widget Icon(MDIcon k,float size=30) {
        icons??=ContentManager.Get<Texture2D>("Towers/status");int i=(int)k;float u=i%4/4f,v=i/4/4f,q=1/4f;
        return new RectangleWidget{Size=new Vector2(size,size),Subtexture=new Subtexture(icons,new Vector2(u,v),new Vector2(u+q,v+q)),FillColor=Color.White,OutlineColor=Color.Transparent,Margin=new Vector2(2,2),VerticalAlignment=WidgetAlignment.Center};
    }
    public static MDIcon Of(MDElement e)=>(MDIcon)((int)e-1);
    float textWidth;
    // one entry: a line of icons and a title, the text below it
    void Entry(IEnumerable<MDIcon> marks,string head,string text,Color? color=null) {
        var line=new StackPanelWidget{Direction=LayoutDirection.Horizontal,Margin=new Vector2(0,6)};
        foreach(var m in marks)line.Children.Add(Icon(m));
        line.Children.Add(new LabelWidget{Text=head,FontScale=.9f,Color=color??MDUi.Text,DropShadow=true,VerticalAlignment=WidgetAlignment.Center,Margin=new Vector2(8,0)});
        content.Children.Add(line);
        if(!string.IsNullOrEmpty(text))content.Children.Add(new LabelWidget{Text=text,FontScale=.72f,Color=MDUi.Dim,WordWrap=true,Size=new Vector2(textWidth,-1),Margin=new Vector2(4,2)});
    }
    void Head(string s)=>content.Children.Add(MDUi.Heading(s));
    void Note(string s)=>content.Children.Add(new LabelWidget{Text=s,FontScale=.75f,Color=MDUi.Text,WordWrap=true,Size=new Vector2(textWidth,-1),Margin=new Vector2(0,4)});
    public static int Entries;   // tests: entries on the page just built
    void Build(bool n,float w) {
        narrow=n;built=true;builtChapter=Chapter;builtWidth=w;textWidth=w-60;content.Children.Clear();
        for(int i=0;i<tabButtons.Count;i++)tabButtons[i].Text=i==Chapter?$"【{Chapters[i]}】":Chapters[i];
        int before=content.Children.Count;
        try{Page(Chapter);}catch(Exception e){Note("（暂时无法生成：" +e.Message+"）");Log.Warning("[MechDefense] manual: "+e);}
        Entries=content.Children.Count-before;
    }
    void Page(int chapter) {
        switch(Chapters[chapter]) {
            case "说明":
                Head("机甲防线手册");
                Note("这本手册讲本模组的防御塔、伤害类型、元素反应、机甲、敌人、首领和防线核心。上面的按钮切换章节；手册里的图标和游戏里敌人头上、炮塔头上的图标一样。");
                Note("木板 + 煤块在工作台合成一本；每个存档第一次进入时会送你一本。更多数值在游戏的“帮助”和“图鉴”里（机甲防线的条目）。");
                Entry([MDIcon.Heat,MDIcon.Cold],"敌人头上的图标","表示它现在身上的状态。另一种元素打中带状态的敌人会发生反应，头上飘出反应的名字。");
                break;
            case "防御塔":
                foreach(int tier in new[]{1,2,3}) {
                    Head($"{tier} 阶");
                    foreach(var d in MDTowerLibrary.All.OrderBy(t=>t.data).Where(t=>MDEncyclopedia.TierOf(t)==tier)) {
                        var e=MDElements.Of(d);
                        Entry(e==MDElement.None?[]:[Of(e)],d.name+(e==MDElement.None?"":$"（{MDElements.Name(e)}）"),MDEncyclopedia.Tower(d));
                    }
                }
                break;
            case "伤害与状态":
                Head("六种伤害");
                for(var e=MDElement.Kinetic;e<=MDElement.Corrosive;e++) {
                    var towers=MDTowerLibrary.All.OrderBy(t=>t.data).Where(t=>MDElements.Of(t)==e).Select(t=>t.name).ToList();
                    Entry([Of(e)],MDElements.Name(e)+(MDElements.Aura(e)?$" —— 命中留下“{MDElements.Statuses[(int)e]}”":" —— 不留状态，但能和已有的状态反应"),"炮塔："+string.Join("、",towers));
                }
                Head("状态");
                Note($"灼烧、冰寒、带电、腐蚀持续 {MDElements.AuraTime:0} 秒，再次命中刷新；着火算灼烧，冻结算冰寒，中毒算腐蚀。");
                Entry([MDIcon.Frozen],"冻结","不能移动、不能开火（液氮塔、晶化反应）。");
                Entry([MDIcon.Paralysed],"瘫痪","不能移动、不能开火（电磁脉冲、全向声波、过载和雷暴反应）。");
                Entry([MDIcon.Slowed],"减速","移动变慢（冰冻塔、毒沼、冻蚀反应）。");
                Entry([MDIcon.ArmourDown],"破甲","受到的伤害提高（酸洗塔、腐燃、冻蚀、熔解反应）。");
                Entry([MDIcon.Brittle],"脆化",$"受到的动能、爆炸伤害 ×1.35，持续 6 秒（脆化反应）。");
                break;
            case "元素反应":
                Head("两种元素");
                Note($"另一种元素打中带状态的敌人时发生。同一个敌人 {MDElements.GlobalCooldown:0.#} 秒内只反应一次，同一种反应 {MDElements.SameCooldown:0} 秒一次。");
                for(int i=1;i<MDElements.Reactions.Length;i++) {
                    var r=(MDReaction)i;var info=MDElements.Reactions[i];
                    if(MDElements.Triads.TryGetValue(r,out var three)) {
                        if(r==MDReaction.Storm){Head("三种元素");Note($"两种元素反应后 {MDElements.ResidueTime:0} 秒内（或敌人身上同时有两种状态时）用第三种元素打中，伤害倍率更高；同一种 {MDElements.TriadCooldown:0} 秒一次。");}
                        Entry(three.Select(Of),info.Name,info.Effect,new Color(255,225,120));
                    }
                    else Entry(info.B==MDElement.None?[Of(info.A)]:[Of(info.A),Of(info.B)],info.Name+(info.B==MDElement.None?"（"+MDElements.Name(info.A)+" + 任一状态）":""),info.Effect);
                }
                Note($"首领：反应的额外伤害只算 {MDElements.BossShare*100:0}%，但会填首领的失衡槽（见“首领”）。模组设置里可以关掉元素反应。");
                break;
            case "机甲":
                Note($"按 {MDMechKeys.Hint(MDMechAction.Ammo)} 切换弹药元素（机甲按键页可改）。标准弹：步枪动能，火炮、导弹、榴弹爆炸；元素弹：除激光外的武器都打这种元素。激光固定是电。");
                foreach(var m in MDMechType.All)Entry(m.Ammo.Select(e=>e==MDElement.Kinetic?MDIcon.Kinetic:Of(e)),m.Name+"机甲",$"装甲 {m.Armour}，武器伤害 ×{m.Damage:0.##}；弹药："+string.Join("、",m.Ammo.Select(MDMechState.AmmoName)));
                break;
            case "敌人":
                foreach(var k in MDRobots.Kinds.Where(k=>!k.Boss)) {
                    var rl=MDElements.ResistList(k).ToList();
                    Entry(rl.Select(x=>Of(x.e)),k.Name,(rl.Count==0?"":"受到伤害："+string.Join("，",rl.Select(x=>$"{MDElements.Name(x.e)} ×{x.f:0.##}"))+"。")+Tip(k.Id));
                }
                break;
            case "首领":
                Note($"首领头上有三条：蓝色是当前这层装甲，红色是耐久，黄色是失衡。反应、它免疫的控制、重炮和机甲火炮会填失衡槽，炮塔造成的每秒最多 {MDBossState.TowerPoiseRate:0} 点，开机甲去打（机甲的元素弹药触发反应、火炮命中）填得快得多；满了它失衡 {MDBossState.PoiseStagger:0} 秒，之后 {MDBossState.PoiseWindow:0} 秒受到的伤害 ×{MDBossState.ExposedFactor:0.#} 并无视装甲层，之后 {MDBossState.PoiseLock:0} 秒不会再失衡，下次要攒的更多。");
                foreach(var k in MDRobots.Kinds.Where(k=>k.Boss)){var rl=MDElements.ResistList(k).ToList();Entry(rl.Select(x=>Of(x.e)),k.Name,MDBossState.Hint(k.Id));}
                break;
            case "防线核心":
                Note(MDThreat.ManualText());
                break;
        }
    }
    static string Tip(string id)=>id switch{
        "RD05"=>"成群贴上来，被你瞄准时会横移，残血会后撤：用范围伤害，别只盯一台。",
        "RD06"=>"锁定后齐射导弹（目标身上有预警）：看到锁定就找掩体，导弹能被甩开。射程比一、二阶铳械塔远。",
        "RD07"=>"远程曲射，落点有红圈：离开红圈；冲到它近身它转身很慢。射程比一、二阶铳械塔远。",
        "RD14"=>"正面装甲厚（×0.75）、背后弱（×1.4），炮塔转得慢：绕到侧后打，或用爆炸、腐蚀。",
        "RD09"=>"小无人机，会找落单的玩家：防空塔、电击最有效。",
        "EN02"=>"精英无人机：防空塔优先打它，加农炮打不到。",
        "RP09"=>"维修无人机，不开火，给附近受伤的机兵回血：先打掉它。",
        _=>""};
}
