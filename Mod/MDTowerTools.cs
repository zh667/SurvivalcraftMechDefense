using Engine;
using Engine.Graphics;
using GameEntitySystem;

namespace Game;

// 0.17.0 the tower tools (user, 2026-11-02: "希望加个工具，可以开关一键显示所有防御塔攻击范围……在放置塔的时候能看到攻击范围（并且塔放
// 下去还可以重新放置）"). After the factory building of a game the user named: a device's panel there has 移动 (put it somewhere
// else, turning it, its attack range shown while it is placed) and 收纳; here the pickaxe takes a tower down as before.
// - The ranges key (MDMechAction.Ranges, Z) on a tower shows that tower's area of attack as a ring on the ground, again on it hides
//   it, at no tower hides them all (user, 11-02: all at once was "太乱了，看不清楚"; first cut 0.17.0 showed every tower's).
// - Holding a tower's deploy item shows where a right-click puts it: its footprint (green, or red and the reason on the bar) and
//   its ring; only the towers marked with Z show theirs too (user: moving one showed them all).
// - Looking at a tower, the move key (MoveTower, M) picks it up: its ghost follows the crosshair (MoveReach m), the rotate key
//   (RotateTower, R; the mount key is kept from the game meanwhile) turns it by 90 degrees, a left-click (a tap on a phone) puts it
//   there with everything it had (health, charge, upgrades), a right-click or the move key again leaves it where it was.
//   A bar over the screen names the keys and says why a spot does not take it. Drawn by MDTowerWorld.DrawRanges (MDTowerRanges.cs).
public static class MDTowerTools {
    public sealed class Ghost { public MDTowerDef Def; public Vector3 At; public Quaternion Rotation=Quaternion.Identity; public bool Has,Ok; public string Reason; public ComponentMDUnit Moving; }
    public static readonly HashSet<ComponentMDUnit> Shown=[];   // towers whose ring (and, since 0.18.0, health bar) Z or the core turned on
    // 0.18.0 (players via the user, 2026-11-04: "点击防线核心，可以一键显示或隐藏所有防御塔的攻击范围和耐久条，方便摆放和维修"): a
    // right-click on the live core (with anything but a repair material, which repairs it) puts every tower in Shown (rings and
    // health bars, MDTowerRanges.cs), again takes them all out. One set for both, so they agree: Z on one tower while all are shown
    // hides that one (and the core's next click shows all again), Z at nothing hides all; a tower placed while all are shown joins.
    // The core's own 120 m ring is left out of "all" (Z on the core still shows it).
    public static bool ShowAll; static double coreClickAt=-1;
    public static int CoreToggles;   // tests
    public static void SetAll(Project p,bool on) {
        ShowAll=on;Shown.Clear();
        if(on)foreach(var u in p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()))if(u?.Tower!=null&&!u.Destroyed&&u.Tower.attack.type!="core")Shown.Add(u);
    }
    // a newly placed tower while all are shown (MDTowerWorld.DrawRanges, once a second)
    public static void Refresh(Project p){if(ShowAll)foreach(var u in p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()))if(u?.Tower!=null&&!u.Destroyed&&u.Tower.attack.type!="core")Shown.Add(u);}
    public static bool CoreClick(ComponentPlayer p) {
        var ray=p.ComponentInput.PlayerInput.Interact;if(!ray.HasValue)return false;
        var u=p.ComponentMiner.Raycast<BodyRaycastResult>(ray.Value,RaycastMode.Interaction)?.ComponentBody.Entity.FindComponent<ComponentMDUnit>();
        if(u?.Tower?.attack.type!="core"||u.Destroyed)return false;
        double now=u.Clock.GameTime;if(now-coreClickAt<.5&&now>=coreClickAt)return true;coreClickAt=now;   // a held button repeats every frame
        ToggleAll(p);return true;
    }
    public static void ToggleAll(ComponentPlayer p) {
        SetAll(p.Project,!ShowAll);CoreToggles++;
        Say(p,ShowAll?$"已显示全部 {Shown.Count} 座防御塔的攻击范围和耐久条（再点核心全部隐藏；对着某座按 {K(MDMechAction.Ranges)} 只开关那一座）":"已隐藏全部防御塔的攻击范围和耐久条");
    }
    public static Ghost Current;   // this frame's ghost of the local player (drawn by MDTowerWorld)
    public static ComponentMDUnit Moving; static int turns; static MDMoveBar bar; static ComponentPlayer mover;
    public const float MoveReach=24,PickReach=12;
    public static int Moves; public static string LastText="",LastInput="";   // tests

    static void Say(ComponentPlayer p,string text){LastText=text;MDPlayer.Message(p,text);}
    static string K(MDMechAction a)=>MDMechKeys.Hint(a);
    public static Ray3 CameraRay(ComponentPlayer p){var c=p.GameWidget.ActiveCamera;return new Ray3(c.ViewPosition,c.ViewDirection);}
    static bool Solid(int v)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v);

    // every frame after the game's input and the mech's (MDMechInputPatch)
    public static void Input(ComponentPlayer p) {
        if(p==null||p.GameWidget==null)return;
        if(mover!=null&&mover!=p)return;   // one player at a time moves a tower
        var gi=p.GameWidget.Input;ref var v=ref p.ComponentInput.m_playerInput;
        Current=null;
        if(p.ComponentHealth.Health<=0||DialogsManager.HasDialogs(p.GuiWidget)||p.ComponentGui.ModalPanelWidget!=null){if(Moving!=null)Stop(p,"");return;}
        if(MDMechKeys.Pressed(gi,MDMechAction.Ranges)) {
            MDMechKeys.ConsumePressed(gi,MDMechAction.Ranges);Shown.RemoveWhere(x=>!x.Entity.IsAddedToProject);
            if(Under(p) is {} t){if(Shown.Remove(t)){ShowAll=false;Say(p,$"已隐藏{t.Tower.name}的攻击范围和耐久条");}else{Shown.Add(t);Say(p,$"显示{t.Tower.name}的攻击范围和耐久条（对着它再按 {K(MDMechAction.Ranges)} 隐藏，对着空处按全部隐藏）");}}
            else if(Shown.Count>0){Shown.Clear();ShowAll=false;Say(p,"已隐藏全部攻击范围和耐久条");}
            else Say(p,$"对准一座炮塔按 {K(MDMechAction.Ranges)} 显示它的攻击范围");
        }
        if(MDMechPilot.Mounted(p)!=null){if(Moving!=null)Stop(p,"");return;}   // driving: the mech's keys
        if(Moving==null) {
            if(MDMechKeys.Pressed(gi,MDMechAction.MoveTower)){MDMechKeys.ConsumePressed(gi,MDMechAction.MoveTower);Pick(p);}
            if(Moving==null){Held(p);return;}
            Clear(ref v);return;
        }
        var u=Moving;
        if(!u.Entity.IsAddedToProject||u.Destroyed||Vector3.Distance(u.Body.Position,p.ComponentBody.Position)>64){Stop(p,u.Destroyed?"炮塔已损毁，移动取消。":"移动已取消。");return;}
        bool mouse=(gi.Devices&WidgetInputDevice.Mouse)!=0;
        Ray3? click=mouse?v.Hit:v.Interact??v.Hit;   // a left-click; on a phone a tap
        bool cancel=MDMechKeys.Pressed(gi,MDMechAction.MoveTower)||mouse&&v.Interact.HasValue;
        if(MDMechKeys.Pressed(gi,MDMechAction.RotateTower))turns=(turns+1)%4;
        MDMechKeys.ConsumePressed(gi,MDMechAction.MoveTower,MDMechAction.RotateTower);
        if(cancel){Clear(ref v);Stop(p,"已取消移动，炮塔留在原处。");return;}
        var ray=click??CameraRay(p);
        var g=Aim(p,u.Tower,ray,MoveReach,u.Body,Facing(u));g.Moving=u;Current=g;
        LastInput=$"devices {gi.Devices}, hit {v.Hit.HasValue}, interact {v.Interact.HasValue}, ray {ray.Position} {ray.Direction}";
        if(click.HasValue){if(g.Ok)Put(p,u,g);else Say(p,g.Reason??"对准地面再放。");}
        Clear(ref v);bar?.Show(u,g);
    }
    // the clicks and the mount key are the move's while a tower is picked up
    static void Clear(ref PlayerInput v){v.Dig=v.Hit=v.Interact=v.Aim=v.PickBlockType=null;v.ToggleMount=false;}
    // the tower under the crosshair (within PickReach, in front of the terrain)
    public static ComponentMDUnit Under(ComponentPlayer p,float reach=PickReach*4) {
        var ray=CameraRay(p);var to=ray.Position+ray.Direction*reach;
        var t=p.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(ray.Position,to,false,true,(x,_)=>Solid(x));
        var hit=p.Project.FindSubsystem<SubsystemBodies>(true).Raycast(ray.Position,to,.25f,(b,_)=>b.Entity.FindComponent<ComponentMDUnit>()?.Kind=="Turret");
        return hit is {} h&&(t==null||h.Distance<t.Value.Distance)?h.ComponentBody.Entity.FindComponent<ComponentMDUnit>():null;
    }
    static void Pick(ComponentPlayer p) {
        var u=Under(p,PickReach);
        if(u==null){Say(p,$"对准要移动的炮塔再按 {K(MDMechAction.MoveTower)}。");return;}
        if(u.Destroyed){Say(p,"已损毁的炮塔不能移动（可以先维修）。");return;}
        Moving=u;mover=p;turns=0;
        bar??=new MDMoveBar();if(bar.ParentWidget==null)p.ComponentGui.ControlsContainerWidget.Children.Add(bar);
        Say(p,$"正在移动{u.Tower.name}");
    }
    public static void Stop(ComponentPlayer p,string text) {
        Moving=null;mover=null;turns=0;Current=null;
        if(bar?.ParentWidget is ContainerWidget c)c.Children.Remove(bar);
        if(!string.IsNullOrEmpty(text))Say(p,text);
    }
    static Quaternion Facing(ComponentMDUnit u){var f=u.Body.Matrix.Forward;float yaw=MathF.Atan2(-f.X,-f.Z)+turns*MathF.PI/2;return Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.Round(yaw/(MathF.PI/2))*MathF.PI/2);}
    // holding a tower's deploy item: where the right-click (ComponentMiner.Use, MDDeployPatch) would put it, the same ray and reach
    static void Held(ComponentPlayer p) {
        int value=p.ComponentMiner.ActiveBlockValue;
        if(Terrain.ExtractContents(value)!=BlocksManager.GetBlockIndex<MDDeploymentBlock>()||MDDeploymentBlock.Tower(value) is not {} d)return;
        var rot=MDTowerPlacement.SnapRotation(p.ComponentBody.Rotation);
        var hit=p.ComponentMiner.Raycast<TerrainRaycastResult>(CameraRay(p),RaycastMode.Digging);
        Current=hit.HasValue?Find(p,d,hit.Value,null,rot):new Ghost{Def=d,Rotation=rot};
    }
    public static Ghost Aim(ComponentPlayer p,MDTowerDef d,Ray3 ray,float reach,ComponentBody ignore,Quaternion rot) {
        var t=p.Project.FindSubsystem<SubsystemTerrain>(true);
        var hit=t.Raycast(ray.Position,ray.Position+ray.Direction*reach,true,true,(x,_)=>Solid(x)||MDTowerPlacement.Replaceable(x));
        return hit.HasValue?Find(p,d,hit.Value,ignore,rot):new Ghost{Def=d,Rotation=rot,Reason="对准地面再放。"};
    }
    static Ghost Find(ComponentPlayer p,MDTowerDef d,TerrainRaycastResult hit,ComponentBody ignore,Quaternion rot) {
        bool ok=MDTowerPlacement.Find(p.Project.FindSubsystem<SubsystemTerrain>(true),p.Project.FindSubsystem<SubsystemBodies>(true),hit,d,out var at,out var reason,ignore);
        return new Ghost{Def=d,At=at,Rotation=rot,Has=at!=default,Ok=ok,Reason=ok?null:reason};
    }
    // put down where the ghost is: the same entity, fixed at the new place and facing
    static void Put(ComponentPlayer p,ComponentMDUnit u,Ghost g) {
        MDTowerPlacement.ClearPlants(p.Project.FindSubsystem<SubsystemTerrain>(true),g.At,u.Tower);
        var body=u.Entity.FindComponent<ComponentMDTowerBody>(true);body.FixAt(g.At,g.Rotation);
        var f=body.Matrix.Forward;u.Yaw=u.AimYaw=MathF.Atan2(-f.X,-f.Z);Moves++;
        p.Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/BlockPlaced",1,0,g.At,4,true);
        MDTowerWorld.For(p.Project).Sparks(g.At+Vector3.UnitY*.1f,Vector3.UnitY,8,new Color(255,220,150),3,.8f,.3f,.04f);
        Stop(p,$"{u.Tower.name}已移到新位置。");
    }
    public static string BarText(ComponentMDUnit u)=>$"移动「{u.Tower.name}」：对准新位置，左键（手机点屏）放下 · {K(MDMechAction.RotateTower)} 旋转 90° · 右键或 {K(MDMechAction.MoveTower)} 取消";
}

// The bar over the screen while a tower is being moved: the keys, and whether the spot takes the tower.
public sealed class MDMoveBar:CanvasWidget {
    readonly RectangleWidget back=new(){FillColor=new Color(10,14,18,190),OutlineColor=new Color(120,200,255,140)};
    readonly LabelWidget keys=new(){FontScale=.75f,Color=Color.White,DropShadow=true,TextAnchor=TextAnchor.HorizontalCenter};
    readonly LabelWidget state=new(){FontScale=.7f,DropShadow=true,TextAnchor=TextAnchor.HorizontalCenter};
    public MDMoveBar(){Size=new Vector2(float.PositiveInfinity);IsHitTestVisible=false;Children.Add(back);Children.Add(keys);Children.Add(state);}
    public Vector2 BoxSize=>back.ActualSize;public Vector2 BoxAt=>GetWidgetPosition(back)??Vector2.Zero;public string Line=>keys.Text+" / "+state.Text;   // the lab
    public void Show(ComponentMDUnit u,MDTowerTools.Ghost g) {
        keys.Text=MDTowerTools.BarText(u);
        state.Text=g.Ok?"可以放在这里":"不能放在这里："+(g.Reason??"对准地面。");state.Color=g.Ok?new Color(130,255,160):new Color(255,140,120);
    }
    public override void MeasureOverride(Vector2 available) {
        if(float.IsFinite(available.X)&&available.X>0) {
            float w=Math.Min(available.X-24,760);back.Size=new Vector2(w,58);keys.Size=new Vector2(w-16,26);state.Size=new Vector2(w-16,24);
            float x=(available.X-w)/2,y=Math.Max(60,available.Y*.12f);
            SetWidgetPosition(back,new Vector2(x,y));SetWidgetPosition(keys,new Vector2(x+8,y+5));SetWidgetPosition(state,new Vector2(x+8,y+31));
        }
        base.MeasureOverride(available);
    }
}
