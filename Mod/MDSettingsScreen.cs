using Engine;
using Engine.Graphics;
using Engine.Input;

namespace Game;

// The mod's own settings (user, 2026-10-21, round 4: "把模组设置都放在一个页面（类似于cs枪械）", and the panel adjusted on a game
// page with fingers or mouse and keys, "可缩放，旋转之类"). The game's "模组设置" lists one entry for this mod
// (modinfo.json Settings, MDSettingsEntryWidget) that opens MDSettingsScreen: every option on one page, plus the HUD editor
// (MDHudLayoutScreen). Built in code like the CS weapons mod's pages: touch targets of at least 48 units, rows that stack on
// narrow screens, a frozen picture of the game behind (MDWorldBackground). Edits work on a copy: Save writes them
// (MDSettings, ModSettings.xml), Cancel and Back leave everything as it was.
public static class MDUi {
    public const float Touch=48;
    public static readonly Color Text=new(235,238,240),Dim=new(165,172,178),Accent=new(90,210,225),Panel=new(16,22,28,228),PanelEdge=new(64,74,82,235);
    public static LabelWidget Label(string text,float scale=1,Color? color=null)=>new(){Text=text,FontScale=scale,Color=color??Text,DropShadow=true,VerticalAlignment=WidgetAlignment.Center};
    public static BevelledButtonWidget Button(string text,float width=140)=>new(){Text=text,Size=new Vector2(width,Touch),FontScale=.82f,VerticalAlignment=WidgetAlignment.Center,HorizontalAlignment=WidgetAlignment.Center};
    public static CheckboxWidget Toggle(string text,bool value){var b=new CheckboxWidget{Text=text,IsChecked=value,IsAutoCheckingEnabled=true,VerticalAlignment=WidgetAlignment.Center};b.Size=new Vector2(-1,Touch);return b;}
    public static SliderWidget Slider(float min,float max,float step,float value)=>new(){MinValue=min,MaxValue=max,Granularity=step,Value=Math.Clamp(value,min,max),Size=new Vector2(260,Touch),VerticalAlignment=WidgetAlignment.Center};
    public static BevelledRectangleWidget Frame()=>new(){CenterColor=Panel,BevelColor=PanelEdge,BevelSize=2,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch,DirectionalLight=0,AmbientLight=1};
    public static LabelWidget Heading(string text)=>new(){Text=text,FontScale=1.05f,Color=Accent,DropShadow=true,Margin=new Vector2(0,10),HorizontalAlignment=WidgetAlignment.Near};
    public static LabelWidget Note(string text)=>new(){Text=text,FontScale=.7f,Color=Dim,WordWrap=true,HorizontalAlignment=WidgetAlignment.Stretch,Margin=new Vector2(0,2)};
    // a label on the left and one control on the right; stacked when narrow
    public static StackPanelWidget Row(Widget left,Widget right,bool narrow) {
        var row=new StackPanelWidget{Direction=narrow?LayoutDirection.Vertical:LayoutDirection.Horizontal,HorizontalAlignment=WidgetAlignment.Stretch,Margin=new Vector2(0,4)};
        left.HorizontalAlignment=narrow?WidgetAlignment.Near:WidgetAlignment.Stretch;left.VerticalAlignment=WidgetAlignment.Center;
        right.HorizontalAlignment=narrow?WidgetAlignment.Near:WidgetAlignment.Far;right.VerticalAlignment=WidgetAlignment.Center;
        row.Children.Add(left);row.Children.Add(right);return row;
    }
    public static StackPanelWidget Bar(params Widget[] items){var bar=new StackPanelWidget{Direction=LayoutDirection.Horizontal,HorizontalAlignment=WidgetAlignment.Center,Margin=new Vector2(0,4)};foreach(var w in items){w.Margin=new Vector2(3,0);bar.Children.Add(w);}return bar;}
    public static bool Inside(Widget w,Vector2 screenPoint){if(w==null||!w.IsVisible||w.ActualSize.X<1)return false;var p=w.ScreenToWidget(screenPoint);return p.X>=0&&p.Y>=0&&p.X<=w.ActualSize.X&&p.Y<=w.ActualSize.Y;}
}

// The one entry in the game's "模组设置": as soon as its page is shown it opens the mod's own settings page. Coming back
// enters the game's list again (its root), which does not hold this widget, so Back cannot bounce into the page again.
public sealed class MDSettingsEntryWidget:UniformSpacingPanelSettingWidget {
    bool opened;
    public override bool Supports(Type type)=>type==typeof(bool);
    public override void Initialize(ModSettingItem descriptor,object currentValue,string name,string description){base.Initialize(descriptor,currentValue,name,description);opened=false;Assemble(MDUi.Note("正在打开机甲防线的设置……"));}
    public override void Update(){base.Update();if(!opened&&ScreensManager.CurrentScreen is ModSettingsScreen&&IsVisible&&ParentWidget!=null){opened=true;MDSettingsScreen.Open();}}
}

// A frozen picture of the game (the 3D views of the game screen, drawn once into a texture when a page opens; no GUI, so not
// the panel itself) behind the settings pages; a plain colour outside a world. After the CS weapons mod's ScGunWorldBackground.
public sealed class MDWorldBackground:Widget {
    readonly DrawContext context=new();RenderTarget2D target;object project;int width,height;bool tried;
    public MDWorldBackground(){IsHitTestVisible=false;Display.DeviceReset+=Reset;}
    public void Reset(){tried=false;}
    public void Release(){tried=false;project=null;target?.Dispose();target=null;}
    public override void MeasureOverride(Vector2 availableSize){IsDrawRequired=true;}
    void Fallback(DrawContext dc){var b=dc.PrimitivesRenderer2D.FlatBatch();int first=b.TriangleVertices.Count;b.QueueQuad(Vector2.Zero,ActualSize,0,new Color(28,36,42)*GlobalColorTransform);b.TransformTriangles(GlobalTransform,first);}
    void Capture(ViewWidget[] views,int w,int h) {
        target?.Dispose();target=new RenderTarget2D(w,h,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        var rt=Display.RenderTarget;var viewport=Display.Viewport;var scissor=Display.ScissorRectangle;
        try {
            Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,w,h);Display.ScissorRectangle=new Rectangle(0,0,w,h);Display.Clear(new Color(28,36,42),1f,0);
            foreach(var view in views) {
                var color=view.m_globalColorTransform;   // the outgoing game screen is faded
                try{view.m_globalColorTransform=Color.White;view.Draw(context);}finally{view.m_globalColorTransform=color;}
            }
            context.PrimitivesRenderer3D.Flush(Matrix.Identity);context.PrimitivesRenderer2D.Flush();
        } finally {
            if(!ReferenceEquals(Display.RenderTarget,rt))Display.RenderTarget=rt;Display.Viewport=viewport;Display.ScissorRectangle=scissor;
            foreach(var view in views)view.GameWidget.ActiveCamera.PrepareForDrawing(null);
            context.PrimitivesRenderer2D.Clear();context.PrimitivesRenderer3D.Clear();
        }
    }
    public override void Draw(DrawContext dc) {
        var game=ScreensManager.FindScreen<Screen>("Game");
        if(GameManager.Project==null||game==null){Release();Fallback(dc);return;}
        dc.PrimitivesRenderer3D.Flush(Matrix.Identity);dc.PrimitivesRenderer2D.Flush();
        int w=Math.Max(1,Display.Viewport.Width),h=Math.Max(1,Display.Viewport.Height);
        try {
            if(!ReferenceEquals(project,GameManager.Project)||w!=width||h!=height)tried=false;
            if(!tried) {
                tried=true;project=GameManager.Project;width=w;height=h;target?.Dispose();target=null;
                var views=game.AllChildren.OfType<ViewWidget>().Where(v=>v.ActualSize.X>0&&v.ActualSize.Y>0&&v.GameWidget?.PlayerData?.ComponentPlayer!=null&&v.GameWidget.PlayerData.IsReadyForPlaying).ToArray();
                if(views.Length>0)Capture(views,w,h);
            }
            if(target==null){Fallback(dc);return;}
            var batch=dc.PrimitivesRenderer2D.TexturedBatch(target,false,0,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp);
            int first=batch.TriangleVertices.Count;batch.QueueQuad(Vector2.Zero,ActualSize,0,Vector2.Zero,Vector2.One,GlobalColorTransform);batch.TransformTriangles(GlobalTransform,first);
            dc.PrimitivesRenderer2D.Flush();
        } catch(Exception e){if(target!=null){target.Dispose();target=null;}Log.Warning("[MechDefense] settings background: "+e.Message);Fallback(dc);}
    }
    public override void Dispose(){Display.DeviceReset-=Reset;Release();base.Dispose();}
}

// The settings page: the mech panel (opens the editor) and the robots, on one scrolling page with 恢复默认 / 取消 / 保存.
public sealed class MDSettingsScreen:Screen {
    public const string ScreenName="MechDefenseSettings";
    public static void EnsureScreens() {
        if(!ScreensManager.m_screens.ContainsKey(ScreenName))ScreensManager.AddScreen(ScreenName,new MDSettingsScreen());
        if(!ScreensManager.m_screens.ContainsKey(MDHudLayoutScreen.ScreenName))ScreensManager.AddScreen(MDHudLayoutScreen.ScreenName,new MDHudLayoutScreen());
    }
    public static void Open(){EnsureScreens();ScreensManager.SwitchScreen(ScreenName);}
    // the robot options being edited
    public sealed class Robots { public MDSpawnTime Time; public float Interval,Density,Chance,Size,Aggro; public int StartDay,BossKills,Fx,Growth; public bool Reactions,Numbers,Endless; public float Medic; public int Difficulty;
        public static Robots Current()=>new(){Time=MDSettings.SpawnTime,Interval=MDSettings.SpawnInterval,Density=MDSettings.SpawnDensity,Chance=MDSettings.RaidChance,Size=MDSettings.RaidSize,StartDay=MDSettings.StartDay,Aggro=MDSettings.AggroRange,BossKills=MDSettings.BossKills,Fx=MDSettings.FxLevel,Growth=MDSettings.Growth,Reactions=MDSettings.Reactions,Numbers=MDSettings.DamageNumbers,Medic=MDSettings.MedicCapacity,Difficulty=MDSettings.ThreatDifficulty,Endless=MDSettings.Endless};
        public static Robots Default()=>new(){Time=MDSpawnTime.Night,Interval=60,Density=1,Chance=75,Size=1,StartDay=3,Aggro=16,BossKills=50,Fx=1,Growth=10,Reactions=true,Numbers=true,Medic=50000,Difficulty=1,Endless=false};
        public void Apply(){MDSettings.SpawnTime=Time;MDSettings.SpawnInterval=Interval;MDSettings.SpawnDensity=Density;MDSettings.RaidChance=Chance;MDSettings.RaidSize=Size;MDSettings.StartDay=StartDay;MDSettings.AggroRange=Aggro;MDSettings.BossKills=BossKills;MDSettings.FxLevel=Fx;MDSettings.Growth=Growth;MDSettings.Reactions=Reactions;MDSettings.DamageNumbers=Numbers;MDSettings.MedicCapacity=Medic;MDSettings.ThreatDifficulty=Difficulty;MDSettings.Endless=Endless;MDSettings.Normalize();} }
    public Robots Working;
    // the mech's keys being edited (MDMechKeys); capturing: the action waiting for a key press
    public Dictionary<MDMechAction,Key> WorkingKeys;public MDMechAction? Capturing;
    readonly Dictionary<MDMechAction,ButtonWidget> keyButtons=[];ButtonWidget pickKey,keysDefault;
    Screen back;bool returning,built,narrow;
    readonly MDWorldBackground background=new();
    readonly CanvasWidget root=new(){Size=new Vector2(float.PositiveInfinity)};
    readonly LabelWidget title=new(){Text="机甲防线 · 模组设置",FontScale=1.1f,TextAnchor=TextAnchor.HorizontalCenter,DropShadow=true};
    readonly CanvasWidget scrollHost=new();
    readonly ScrollPanelWidget scroll=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch};
    readonly StackPanelWidget content=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch};
    readonly StackPanelWidget bar;readonly LabelWidget status=MDUi.Note("");
    readonly ButtonWidget defaults=MDUi.Button("恢复默认",150),cancel=MDUi.Button("取消",130),save=MDUi.Button("保存",130),editHud=MDUi.Button("调整机甲面板（位置、大小、旋转）",360);
    ButtonWidget less,more,input,time,fx,reactions,growth,numbers,difficulty,endless;LabelWidget startLabel,hudInfo,densityNote;SliderWidget interval,density,chance,size,aggro,bossKills,medic;
    public MDSettingsScreen() {
        Children.Add(background);Children.Add(MDUi.Frame());
        scroll.Children.Add(content);scrollHost.Children.Add(scroll);
        bar=MDUi.Bar(defaults,cancel,save);
        root.Children.Add(title);root.Children.Add(scrollHost);root.Children.Add(bar);root.Children.Add(status);
        status.HorizontalAlignment=WidgetAlignment.Center;Children.Add(root);
    }
    public override void Enter(object[] parameters) {
        background.Reset();
        if(!returning){back=ScreensManager.PreviousScreen;Working=Robots.Current();WorkingKeys=MDMechKeys.Current();}
        returning=false;built=false;status.Text="";Capturing=null;
    }
    public override void Leave(){background.Release();base.Leave();}
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Max(280,available.X),h=Math.Max(220,available.Y);bool n=w<650;
        if(Working!=null&&(!built||n!=narrow))Build(n);
        root.SetWidgetPosition(title,new Vector2(12,8));title.Size=new Vector2(w-24,40);
        root.SetWidgetPosition(scrollHost,new Vector2(12,54));scrollHost.Size=new Vector2(w-24,Math.Max(60,h-54-100));
        float bw=Math.Min(150,(w-48)/3);foreach(var b in new[]{defaults,cancel,save})((CanvasWidget)b).Size=new Vector2(bw,48);
        root.SetWidgetPosition(bar,new Vector2((w-3*(bw+6))/2,h-92));
        root.SetWidgetPosition(status,new Vector2(12,h-38));status.Size=new Vector2(w-24,32);
        base.MeasureOverride(available);
    }
    void Build(bool n) {
        narrow=n;built=true;content.Children.Clear();
        content.Children.Add(MDUi.Heading("机甲面板"));
        editHud.ParentWidget?.Children.Remove(editHud);editHud.HorizontalAlignment=WidgetAlignment.Near;content.Children.Add(editHud);
        hudInfo=MDUi.Note("");content.Children.Add(hudInfo);
        content.Children.Add(MDUi.Heading("机甲按键"));   // 0.11.0 (user: "机甲绑定按键编辑（类似于CS枪械适配铃兰触控那样）")
        keyButtons.Clear();
        foreach(var a in MDMechKeys.All){var b=MDUi.Button("",170);keyButtons[a]=b;content.Children.Add(MDUi.Row(MDUi.Label(MDMechKeys.ActionName(a)),b,n));}
        pickKey=MDUi.Button("从列表选择按键",200);keysDefault=MDUi.Button("按键恢复默认",180);content.Children.Add(MDUi.Bar(pickKey,keysDefault));
        content.Children.Add(MDUi.Note("点一个动作的按钮，再按下新按键（Esc 取消，Delete 清空）；没有键盘时点「从列表选择按键」。手机：用铃兰触控在屏幕上加按钮，映射到这些键。"+
            "开火、上下车另外照常可以用鼠标左键／点屏、游戏的骑乘键。只对机甲有效，不改游戏原来的按键；驾驶机甲时，和游戏共用的键只做机甲动作。"));
        content.Children.Add(MDUi.Heading("画面"));
        fx=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("特效数量"),fx,n));
        content.Children.Add(MDUi.Note("炮塔和机兵的烟雾、火花、碎屑数量。手机发热时选“极简”；“完整”是 0.12.0 及以前的效果。"));
        content.Children.Add(MDUi.Heading("敌方机兵"));
        less=MDUi.Button("−",64);more=MDUi.Button("+",64);input=MDUi.Button("输入",96);startLabel=MDUi.Label("");
        content.Children.Add(MDUi.Row(startLabel,MDUi.Bar(less,more,input),n));
        time=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("野外刷新时段"),time,n));
        interval=MDUi.Slider(15,600,15,Working.Interval);content.Children.Add(MDUi.Row(MDUi.Label("野外刷新间隔"),interval,n));
        density=MDUi.Slider(.25f,4,.25f,Working.Density);content.Children.Add(MDUi.Row(MDUi.Label("野外刷新密度"),density,n));
        densityNote=MDUi.Note("");content.Children.Add(densityNote);
        chance=MDUi.Slider(0,100,5,Working.Chance);content.Children.Add(MDUi.Row(MDUi.Label("夜袭概率"),chance,n));
        size=MDUi.Slider(.5f,3,.25f,Working.Size);content.Children.Add(MDUi.Row(MDUi.Label("夜袭规模"),size,n));
        content.Children.Add(MDUi.Note("夜袭在天黑后不久到来：玩家 80 米内有炮塔时打炮塔阵地，没有炮塔时直接冲着玩家来。"));
        aggro=MDUi.Slider(0,MDSettings.MaxAggroRange,2,Working.Aggro);content.Children.Add(MDUi.Row(MDUi.Label("主动攻击距离"),aggro,n));
        content.Children.Add(MDUi.Note("机兵看见这个距离内的玩家就主动攻击；0 为只在被打时还手。附近有炮塔、机甲时仍先打它们，谁打了它就先打谁。"));
        bossKills=MDUi.Slider(0,MDSettings.MaxBossKills,10,Working.BossKills);content.Children.Add(MDUi.Row(MDUi.Label("首领出现"),bossKills,n));
        content.Children.Add(MDUi.Note("每击毁这么多台机兵，就会有一台首领机甲在 30 秒后到来（从五种首领里随机来一台，按存档计数）；0 为只用诱敌信标召唤。"));
        growth=MDUi.Button("",300);content.Children.Add(MDUi.Row(MDUi.Label("敌人成长"),growth,n));
        content.Children.Add(MDUi.Note("从开始天数起每 7 天一个阶段：机兵的耐久和伤害按阶段增加，最多 5 个阶段。新兵种按阶段出现（开始天数后第 3 天战车和小无人机，第 7 天精英，第 14 天维修无人机），和这一项无关。"));
        content.Children.Add(MDUi.Heading("防线核心"));
        difficulty=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("防线难度"),difficulty,n));
        endless=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("无尽模式"),endless,n));
        content.Children.Add(MDUi.Note("放下防线核心后，附近击毁机兵会让威胁度上涨，满了引来多波次袭击（倒计时会说出下一波的兵种和数量）。难度越高，威胁度满得越快、每波越多。无尽模式：袭击不停、越来越强，直到核心被摧毁。原来的夜袭和“首领出现”不受影响。"));
        content.Children.Add(MDUi.Heading("战斗"));
        reactions=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("元素反应"),reactions,n));
        content.Children.Add(MDUi.Note("热、冷、电、腐蚀伤害会给敌人挂上状态（头顶有图标），另一种元素再打中时发生反应，例如热 + 冷 = 热冲击。关掉后只保留伤害类型、抗性和状态。"));
        medic=MDUi.Slider(0,MDSettings.MaxMedicCapacity,10000,Working.Medic);content.Children.Add(MDUi.Row(MDUi.Label("医疗塔储能"),medic,n));
        content.Children.Add(MDUi.Note("每座医疗塔能治疗的总量（按被治疗的炮塔、机甲耐久点数算，玩家的整条生命算 100 点），用完就不再治疗；首领掉落的“医疗充能核心”右键医疗塔可以充满。医疗塔头上的槽：绿色是剩余，灰色是用掉的。拉到最左为不限。"));
        numbers=MDUi.Button("",220);content.Children.Add(MDUi.Row(MDUi.Label("伤害数字"),numbers,n));
        content.Children.Add(MDUi.Note("炮塔、机甲和玩家打出的伤害显示在敌人头上，颜色是伤害类型，反应伤害是大字。同一个敌人短时间内的伤害合成一个数；同屏数字有上限（特效数量“精简”8 个，“极简”4 个），敌人很多时不会满屏。"));
        content.Children.Add(MDUi.Note("所有存档通用，保存后立即生效。开始天数为 0 时不刷新：野外机兵和夜袭都没有，诱敌信标照常可用。"));
        Refresh();
    }
    void Refresh() {
        var r=Working;startLabel.Text=MDSettings.StartDayText(r.StartDay);time.Text=MDSettings.SpawnTimeName(r.Time);
        interval.Text=$"{r.Interval:0} 秒";density.Text=$"{r.Density:0.##} 倍";chance.Text=r.Chance<=0?"不夜袭":$"{r.Chance:0}%";size.Text=$"{r.Size:0.##} 倍";
        fx.Text=MDSettings.FxNames[Math.Clamp(r.Fx,0,2)];reactions.Text=r.Reactions?"开":"关";numbers.Text=r.Numbers?"开":"关";difficulty.Text=MDThreat.DifficultyNames[Math.Clamp(r.Difficulty,0,3)];endless.Text=r.Endless?"开":"关";medic.Text=MDSettings.MedicText(r.Medic);growth.Text=MDSettings.GrowthText(r.Growth);
        aggro.Text=MDSettings.AggroText(r.Aggro);bossKills.Text=MDSettings.BossKillsText(r.BossKills);
        int cap=Math.Max(1,(int)MathF.Round(4*r.Density)),lo=Math.Max(1,(int)MathF.Round(r.Density)),hi=Math.Clamp((int)MathF.Round(2.5f*r.Density),1,cap);
        densityNote.Text=$"每批 {lo}～{hi} 台，玩家 80 米内最多 {cap} 台。";
        foreach(var (a,b) in keyButtons){var t=Capturing==a?"请按键…":MDMechKeys.KeyName(WorkingKeys.GetValueOrDefault(a,Key.Null));if(b.Text!=t)b.Text=t;}
        var p=MDSettings.Hud;
        hudInfo.Text=$"显示：{(MDSettings.HudShow?"开":"关")} · 按键提示：{(MDSettings.HudKeys?"开":"关")} · 位置：{(p.Custom?$"自定义（大小 {p.Scale*100:0}%，旋转 {p.Rotation*180/MathF.PI:0}°）":"默认（右下角）")}";
    }
    // also used by the lab
    public void OpenEditor(){returning=true;ScreensManager.SwitchScreen(MDHudLayoutScreen.ScreenName);}
    public void SaveNow(){Working.Apply();MDMechKeys.Apply(WorkingKeys);MDSettings.Persist();Close();}
    // the sliders show Working (Update copies them back every frame): after Working changes in code (defaults, the lab)
    public void SyncSliders(){interval.Value=Working.Interval;density.Value=Working.Density;chance.Value=Working.Chance;size.Value=Working.Size;aggro.Value=Working.Aggro;bossKills.Value=Working.BossKills;medic.Value=Working.Medic;}
    // a key for an action on the page (also used by the lab); refused when another mech action has it
    public bool SetKey(MDMechAction a,Key k) {
        if(k!=Key.Null&&!MDMechKeys.Bindable(k)){status.Text="这个键不能用。";return false;}
        if(MDMechKeys.Holder(WorkingKeys,a,k) is {} other){status.Text=$"「{MDMechKeys.KeyName(k)}」已用于「{MDMechKeys.ActionName(other)}」，换一个键，或先改那个动作。";return false;}
        if(k!=Key.Null&&MDMechKeys.GameKey("ToggleMount")==k){status.Text=$"「{MDMechKeys.KeyName(k)}」是游戏的骑乘键，已经用来上下机甲，换一个键。";return false;}
        WorkingKeys[a]=k;Capturing=null;var shared=MDMechKeys.SharedWith(k);
        status.Text=$"「{MDMechKeys.ActionName(a)}」改为 {MDMechKeys.KeyName(k)}（保存后生效）。"+(shared.Length>0?$"这个键在游戏里也是「{shared}」，驾驶机甲时只做机甲动作。":"");return true;
    }
    void Close()=>ScreensManager.SwitchScreen(back??ScreensManager.FindScreen<Screen>("ModSettings")??ScreensManager.FindScreen<Screen>("MainMenu"));
    public override void Update() {
        if(!built)return;
        if(DialogsManager.HasDialogs(this))return;
        if(Capturing is {} ca) {   // waiting for a key: Escape cancels (and must not close the page), Delete clears
            if(Input.IsKeyDownOnce(Key.Escape)){Capturing=null;status.Text="已取消。";Refresh();return;}
            if(Input.IsKeyDownOnce(Key.Delete)){SetKey(ca,Key.Null);Refresh();return;}
            foreach(var k in Enum.GetValues<Key>())if(MDMechKeys.Bindable(k)&&Input.IsKeyDownOnce(k)){SetKey(ca,k);Refresh();return;}
        }
        foreach(var (a,b) in keyButtons)if(b.IsClicked){Capturing=a;status.Text=$"按下新按键给「{MDMechKeys.ActionName(a)}」（Esc 取消，Delete 清空），或点「从列表选择按键」。";}
        if(pickKey.IsClicked) {
            if(Capturing is not {} pa)status.Text="先点要改的动作。";
            else DialogsManager.ShowDialog(this,new ListSelectionDialog($"给「{MDMechKeys.ActionName(pa)}」选择按键",
                new object[]{Key.Null}.Concat(Enum.GetValues<Key>().Where(MDMechKeys.Bindable).Cast<object>()).ToArray(),56,k=>MDMechKeys.KeyName((Key)k),k=>{if(k is Key key)SetKey(pa,key);Refresh();}));
        }
        if(keysDefault.IsClicked){WorkingKeys=MDMechKeys.Defaults();Capturing=null;status.Text="机甲按键已恢复默认（保存后生效）。";}
        Working.Interval=interval.Value;Working.Density=density.Value;Working.Chance=chance.Value;Working.Size=size.Value;Working.Aggro=aggro.Value;Working.BossKills=(int)MathF.Round(bossKills.Value);Working.Medic=medic.Value;
        if(less.IsClicked)Working.StartDay=Math.Max(0,Working.StartDay-1);
        if(more.IsClicked)Working.StartDay=Math.Min(MDSettings.MaxStartDay,Working.StartDay+1);
        if(input.IsClicked)DialogsManager.ShowDialog(this,new TextBoxDialog($"开始天数（0～{MDSettings.MaxStartDay}，0 为不刷新）",Working.StartDay.ToString(),5,text=>{
            if(text==null)return;
            if(!int.TryParse(text,out int d)||d<0||d>MDSettings.MaxStartDay){status.Text=$"请输入 0～{MDSettings.MaxStartDay} 的整数。";return;}
            Working.StartDay=d;status.Text="";}));
        if(time.IsClicked)Working.Time=(MDSpawnTime)(((int)Working.Time+1)%3);
        if(fx.IsClicked)Working.Fx=(Working.Fx+1)%3;
        if(reactions.IsClicked)Working.Reactions=!Working.Reactions;
        if(numbers.IsClicked)Working.Numbers=!Working.Numbers;
        if(difficulty.IsClicked)Working.Difficulty=(Working.Difficulty+1)%4;
        if(endless.IsClicked)Working.Endless=!Working.Endless;
        if(growth.IsClicked){int i=Array.IndexOf(MDSettings.GrowthSteps,Working.Growth);Working.Growth=MDSettings.GrowthSteps[(i+1)%MDSettings.GrowthSteps.Length];}
        if(defaults.IsClicked){Working=Robots.Default();WorkingKeys=MDMechKeys.Defaults();Capturing=null;SyncSliders();status.Text="已恢复默认（保存后生效）。机甲面板在调整页里单独恢复。";}
        if(editHud.IsClicked){OpenEditor();return;}
        Refresh();
        if(cancel.IsClicked||Input.Back||Input.Cancel){Close();return;}
        if(save.IsClicked)SaveNow();
    }
}

// Two fingers on the panel: they move it, pinch to scale, twist to turn. A new finger pair starts again from where the
// panel is, without a jump. After the CS weapons mod's ScHudGesture.
public sealed class MDHudGesture {
    int first=-1,second=-1;Vector2 startMid,startCentre;float startDistance,startAngle;MDHudPlacement start;
    public void Reset(){first=second=-1;start=null;}
    public bool Step(IEnumerable<TouchLocation> touches,MDHudPlacement hud,Vector2 area,Vector2 size,Func<Vector2,Vector2> local,Func<Vector2,bool> eligible) {
        var active=touches.Where(t=>t.State!=TouchLocationState.Released).ToArray();
        if(active.Length<2){Reset();return false;}
        var a=active.FirstOrDefault(t=>t.Id==first);var b=active.FirstOrDefault(t=>t.Id==second);
        if(start==null||!active.Any(t=>t.Id==first)||!active.Any(t=>t.Id==second)) {
            Reset();var pair=active.Where(t=>eligible(t.Position)).Take(2).ToArray();if(pair.Length<2)return false;
            a=pair[0];b=pair[1];var pa=local(a.Position);var pb=local(b.Position);
            if(!hud.Hit(pa,area,size)&&!hud.Hit(pb,area,size)&&!hud.Hit((pa+pb)/2,area,size))return false;   // one finger or their middle starts on the panel
            hud.MakeCustom(area,size);first=a.Id;second=b.Id;start=hud.Copy();startMid=(pa+pb)/2;startCentre=hud.Centre(area,size);
            var d=pb-pa;startDistance=Math.Max(d.Length(),10);startAngle=MathF.Atan2(d.Y,d.X);return true;
        }
        var p=local(a.Position);var q=local(b.Position);var delta=q-p;var moved=startCentre+(p+q)/2-startMid;
        hud.X=moved.X/area.X;hud.Y=moved.Y/area.Y;hud.Scale=start.Scale*delta.Length()/startDistance;hud.Rotation=start.Rotation+MathF.Atan2(delta.Y,delta.X)-startAngle;hud.Normalize();return true;
    }
}

// The panel editor: the game behind it, the real panel on top where it will be. Drag it (mouse or one finger); two fingers
// move, pinch and twist; the mouse wheel scales, Shift + wheel or Q / E turns; the buttons do the same. Nothing here can
// fire or move the mech. Works on a copy: Save writes it, Cancel and Back leave the saved one.
public sealed class MDHudLayoutScreen:Screen {
    public const string ScreenName="MechDefenseHudLayout";
    public const float ScaleStep=1.1f,TurnStep=MathF.PI/12;
    public MDHudPlacement Working=new();public bool WorkingShow=true,WorkingKeys=true;
    readonly MDWorldBackground background=new();
    readonly CanvasWidget preview=new(){Size=new Vector2(float.PositiveInfinity),ClampToBounds=true,IsHitTestVisible=false};
    public readonly MDStatusWidget Sample=new();
    readonly CanvasWidget host=new(){Size=new Vector2(float.PositiveInfinity),IsHitTestVisible=false};
    readonly CanvasWidget panel=new();readonly CanvasWidget scrollHost=new();
    readonly ScrollPanelWidget scroll=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch};
    readonly StackPanelWidget controls=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch,Margin=new Vector2(8,6)};
    readonly StackPanelWidget footer;
    readonly ButtonWidget bigger=MDUi.Button("放大",100),smaller=MDUi.Button("缩小",100),left=MDUi.Button("左转",100),right=MDUi.Button("右转",100),
        reset=MDUi.Button("恢复默认",130),side=MDUi.Button("面板换边",130),collapse=MDUi.Button("收起面板",130),expand=MDUi.Button("展开面板",130),cancel=MDUi.Button("取消",120),save=MDUi.Button("保存",120);
    readonly CheckboxWidget show=MDUi.Toggle("显示状态面板",true),keys=MDUi.Toggle("显示按键提示",true);
    readonly LabelWidget info=MDUi.Note("");
    readonly MDHudGesture gesture=new();
    Screen back;bool collapsed,dragging,wasMoving,sideChecked;Vector2 dragOffset,area;
    public MDHudLayoutScreen() {
        Children.Add(background);preview.Children.Add(Sample);Children.Add(preview);Sample.Sample();
        controls.Children.Add(MDUi.Label("机甲面板 · 位置和大小",1,MDUi.Accent));
        controls.Children.Add(show);controls.Children.Add(keys);
        controls.Children.Add(MDUi.Bar(bigger,smaller));controls.Children.Add(MDUi.Bar(left,right));controls.Children.Add(MDUi.Bar(reset,side));controls.Children.Add(MDUi.Bar(collapse));
        controls.Children.Add(info);
        controls.Children.Add(MDUi.Note("拖动面板移动。手机：双指移动、捏合缩放、转动旋转。电脑：拖动移动，滚轮缩放，Shift＋滚轮或 Q / E 旋转。面板挡住时先收起。"));
        scroll.Children.Add(controls);scrollHost.Children.Add(scroll);
        footer=MDUi.Bar(cancel,save);
        panel.Children.Add(MDUi.Frame());panel.Children.Add(scrollHost);panel.Children.Add(footer);
        host.Children.Add(panel);Children.Add(host);
        expand.HorizontalAlignment=WidgetAlignment.Near;expand.VerticalAlignment=WidgetAlignment.Near;expand.Margin=new Vector2(12,12);expand.IsVisible=false;Children.Add(expand);
    }
    public override void Enter(object[] parameters) {
        background.Reset();back=ScreensManager.PreviousScreen;
        Working=MDSettings.Hud.Copy();WorkingShow=MDSettings.HudShow;WorkingKeys=MDSettings.HudKeys;show.IsChecked=WorkingShow;keys.IsChecked=WorkingKeys;
        collapsed=dragging=wasMoving=sideChecked=false;gesture.Reset();panel.HorizontalAlignment=WidgetAlignment.Near;panel.VerticalAlignment=WidgetAlignment.Center;
    }
    public override void Leave(){background.Release();base.Leave();}
    public override void MeasureOverride(Vector2 available) {
        area=available;
        float pw=Math.Min(400,Math.Max(260,available.X*.42f)),ph=Math.Max(200,available.Y-24);
        panel.Size=new Vector2(pw,ph);panel.Margin=new Vector2(12,12);
        panel.SetWidgetPosition(scrollHost,new Vector2(8,8));scrollHost.Size=new Vector2(pw-16,ph-16-60);
        panel.SetWidgetPosition(footer,new Vector2((pw-2*126)/2,ph-60));
        Sample.ShowKeys=WorkingKeys;var corner=Working.Corner(area,Sample.Size,out var m);preview.SetWidgetPosition(Sample,corner);Sample.RenderTransform=m;
        base.MeasureOverride(available);
    }
    bool OverControls(Vector2 p)=>!collapsed&&MDUi.Inside(panel,p)||collapsed&&MDUi.Inside(expand,p);
    // edits (the buttons, the lab)
    public void Scale(float factor){Working.MakeCustom(area,Sample.Size);Working.Scale*=factor;Working.Normalize();}
    public void Turn(float radians){Working.MakeCustom(area,Sample.Size);Working.Rotation+=radians;Working.Normalize();}
    public void MoveTo(Vector2 fraction){Working.MakeCustom(area,Sample.Size);Working.X=fraction.X;Working.Y=fraction.Y;Working.Normalize();AutoSide();}
    public void SaveNow(){MDSettings.Hud=Working.Copy().Normalize();MDSettings.HudShow=WorkingShow;MDSettings.HudKeys=WorkingKeys;MDSettings.Persist();Close();}
    void Close()=>ScreensManager.SwitchScreen(back??ScreensManager.FindScreen<Screen>(MDSettingsScreen.ScreenName));
    public override void Update() {
        panel.IsVisible=!collapsed;expand.IsVisible=collapsed;
        WorkingShow=show.IsChecked;WorkingKeys=keys.IsChecked;
        Sample.ColorTransform=WorkingShow?Color.White:new Color((byte)150,(byte)150,(byte)150,(byte)110);   // a hidden panel stays visible here, dimmed
        if(area.X>1&&area.Y>1) {
            bool moving=Pointer();
            if(!sideChecked||wasMoving&&!moving){sideChecked=true;AutoSide();}   // on opening and when a drag ends
            wasMoving=moving;
        }
        info.Text=$"位置：{(Working.Custom?"自定义":"默认（右下角）")}　大小 {Working.Scale*100:0}%　旋转 {Working.Rotation*180/MathF.PI:0}°"+(WorkingShow?"":"　（面板已隐藏）");
        if(bigger.IsClicked)Scale(ScaleStep);
        if(smaller.IsClicked)Scale(1/ScaleStep);
        if(left.IsClicked)Turn(-TurnStep);
        if(right.IsClicked)Turn(TurnStep);
        if(reset.IsClicked){Working=new MDHudPlacement();show.IsChecked=keys.IsChecked=true;}
        if(side.IsClicked)panel.HorizontalAlignment=panel.HorizontalAlignment==WidgetAlignment.Near?WidgetAlignment.Far:WidgetAlignment.Near;
        if(collapse.IsClicked)collapsed=true;
        if(expand.IsClicked)collapsed=false;
        if(cancel.IsClicked||Input.Back||Input.Cancel){Close();return;}
        if(save.IsClicked)SaveNow();
    }
    // the control panel moves to the other side when the panel being edited sits under it
    void AutoSide() {
        float c=Working.Centre(area,Sample.Size).X,w=panel.Size.X+24;
        if(panel.HorizontalAlignment==WidgetAlignment.Near&&c<w)panel.HorizontalAlignment=WidgetAlignment.Far;
        else if(panel.HorizontalAlignment==WidgetAlignment.Far&&c>area.X-w)panel.HorizontalAlignment=WidgetAlignment.Near;
    }
    public bool ControlsOnRight=>panel.HorizontalAlignment==WidgetAlignment.Far;
    // mouse, keys and fingers on the preview (never on the control panel); true while the panel is being moved
    bool Pointer() {
        Vector2 Local(Vector2 p)=>preview.ScreenToWidget(p);
        if(gesture.Step(Input.TouchLocations,Working,area,Sample.Size,Local,p=>!OverControls(p))){dragging=false;return true;}
        if(Input.Scroll is Vector3 wheel&&wheel.Z!=0&&!OverControls(new Vector2(wheel.X,wheel.Y))) {
            if(Input.IsKeyDown(Key.Shift))Turn(wheel.Z*MathF.PI/36);else Scale(MathF.Pow(ScaleStep,wheel.Z));
        }
        if(Input.IsKeyDownOnce(Key.Q))Turn(-MathF.PI/36);
        if(Input.IsKeyDownOnce(Key.E))Turn(MathF.PI/36);
        if(Input.Press is not Vector2 press){dragging=false;return false;}
        var point=Local(press);
        if(!dragging) {
            if(OverControls(press)||!Working.Hit(point,area,Sample.Size))return false;
            Working.MakeCustom(area,Sample.Size);dragging=true;dragOffset=point-Working.Centre(area,Sample.Size);return true;
        }
        var c=point-dragOffset;Working.X=c.X/area.X;Working.Y=c.Y/area.Y;Working.Normalize();return true;
    }
}
